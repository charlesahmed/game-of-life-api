using GameOfLife.Api.Models.Requests;
using GameOfLife.Api.Models.Responses;
using GameOfLife.Core.Domain;
using GameOfLife.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace GameOfLife.Api.Controllers;

/// <summary>Conway's Game of Life board management.</summary>
[ApiController]
[Route("api/boards")]
[Produces("application/json")]
public sealed class BoardsController(
    IBoardRepository repository,
    IGameOfLifeService gameService,
    IMemoryCache cache,
    ILogger<BoardsController> logger,
    IOptions<GameOfLifeOptions> options) : ControllerBase
{
    private readonly IBoardRepository _repository = repository;
    private readonly IGameOfLifeService _gameService = gameService;
    private readonly IMemoryCache _cache = cache;
    private readonly ILogger<BoardsController> _logger = logger;
    private readonly GameOfLifeOptions _options = options.Value;

    /// <summary>Upload a new board state and receive a unique board ID.</summary>
    /// <response code="201">Board stored successfully.</response>
    /// <response code="400">Board dimensions are invalid.</response>
    [HttpPost]
    [ProducesResponseType(typeof(UploadBoardResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UploadBoard(
        [FromBody] UploadBoardRequest request,
        CancellationToken cancellationToken)
    {
        if (ValidateCells(request.Cells) is { } problem)
            return BadRequest(problem);

        var board = new Board(Guid.NewGuid(), request.Cells, DateTimeOffset.UtcNow);
        await _repository.CreateAsync(board, cancellationToken);

        _logger.LogInformation("Board {BoardId} created ({Rows}x{Cols})", board.Id, board.Rows, board.Columns);

        var response = new UploadBoardResponse
        {
            Id = board.Id,
            Rows = board.Rows,
            Columns = board.Columns,
            CreatedAt = board.CreatedAt
        };

        return CreatedAtAction(nameof(GetBoard), new { id = board.Id }, response);
    }

    /// <summary>Retrieve the original uploaded board state.</summary>
    /// <param name="id">Board ID returned from the upload endpoint.</param>
    /// <param name="cancellationToken">Propagated from the HTTP request lifecycle.</param>
    /// <response code="200">Board found.</response>
    /// <response code="404">Board not found.</response>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(BoardDetailsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetBoard(Guid id, CancellationToken cancellationToken)
    {
        var board = await _repository.GetByIdAsync(id, cancellationToken);
        if (board is null)
            return Problem(detail: $"No board with ID '{id}' exists.", title: "Board not found", statusCode: StatusCodes.Status404NotFound);

        // Boards are immutable after creation; allow downstream caches to hold the response.
        Response.Headers.CacheControl = "public, max-age=86400";

        return Ok(new BoardDetailsResponse
        {
            Id = board.Id,
            Cells = board.Cells,
            Rows = board.Rows,
            Columns = board.Columns,
            CreatedAt = board.CreatedAt
        });
    }

    /// <summary>Return the board state after exactly one generation.</summary>
    /// <param name="id">Board ID returned from the upload endpoint.</param>
    /// <param name="cancellationToken">Propagated from the HTTP request lifecycle.</param>
    /// <response code="200">Next generation computed.</response>
    /// <response code="404">Board not found.</response>
    [HttpGet("{id:guid}/next")]
    [ProducesResponseType(typeof(BoardStateResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetNextState(Guid id, CancellationToken cancellationToken)
    {
        var board = await _repository.GetByIdAsync(id, cancellationToken);
        if (board is null)
            return Problem(detail: $"No board with ID '{id}' exists.", title: "Board not found", statusCode: StatusCodes.Status404NotFound);

        // Deterministic for a given board ID; safe for clients/CDNs to cache.
        Response.Headers.CacheControl = "public, max-age=3600";

        var next = await Task.Run(() => _gameService.ComputeNextGeneration(board.Cells), cancellationToken);
        return Ok(ToBoardStateResponse(next, generationsAdvanced: 1));
    }

    /// <summary>Return the board state after N generations.</summary>
    /// <param name="id">Board ID returned from the upload endpoint.</param>
    /// <param name="n">Number of generations to advance (1 – 10 000).</param>
    /// <param name="cancellationToken">Propagated from the HTTP request lifecycle.</param>
    /// <response code="200">Board state after N generations.</response>
    /// <response code="400">N is out of range.</response>
    /// <response code="404">Board not found.</response>
    [HttpGet("{id:guid}/states/{n:int}")]
    [EnableRateLimiting(RateLimitPolicies.Computation)]
    [ProducesResponseType(typeof(BoardStateResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> GetStatesAhead(Guid id, int n, CancellationToken cancellationToken)
    {
        if (n <= 0)
            return Problem(detail: "N must be a positive integer.", title: "Invalid generation count", statusCode: StatusCodes.Status400BadRequest);

        if (n > _options.MaxGenerationsAhead)
        {
            return Problem(
                detail: $"N cannot exceed {_options.MaxGenerationsAhead}.",
                title: "Generation count too large",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var board = await _repository.GetByIdAsync(id, cancellationToken);
        if (board is null)
            return Problem(detail: $"No board with ID '{id}' exists.", title: "Board not found", statusCode: StatusCodes.Status404NotFound);

        // Deterministic for (boardId, n); safe for clients/CDNs to cache.
        Response.Headers.CacheControl = "public, max-age=3600";

        var result = await Task.Run(() => _gameService.ComputeNGenerations(board.Cells, n, cancellationToken), cancellationToken);
        return Ok(ToBoardStateResponse(result, generationsAdvanced: n));
    }

    /// <summary>
    /// Return the final stable state of the board (static equilibrium or detected cycle).
    /// Returns 422 if the board has not stabilised within the configured iteration limit.
    /// </summary>
    /// <param name="id">Board ID returned from the upload endpoint.</param>
    /// <param name="cancellationToken">Propagated from the HTTP request lifecycle.</param>
    /// <response code="200">Stable state reached.</response>
    /// <response code="404">Board not found.</response>
    /// <response code="422">Board did not converge within the iteration limit.</response>
    /// <response code="429">Too many concurrent computation requests.</response>
    [HttpGet("{id:guid}/final")]
    [EnableRateLimiting(RateLimitPolicies.Computation)]
    [ProducesResponseType(typeof(BoardStateResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> GetFinalState(Guid id, CancellationToken cancellationToken)
    {
        var board = await _repository.GetByIdAsync(id, cancellationToken);
        if (board is null)
            return Problem(detail: $"No board with ID '{id}' exists.", title: "Board not found", statusCode: StatusCodes.Status404NotFound);

        // Check in-process cache before running the expensive convergence loop.
        // Boards are immutable, so a cached result is always valid for the lifetime of this process.
        var cacheKey = $"final_state:{id}";
        if (_cache.TryGetValue(cacheKey, out bool[][]? cached))
        {
            Response.Headers.CacheControl = "public, max-age=3600";
            return Ok(ToBoardStateResponse(cached!, generationsAdvanced: null));
        }

        var (finalCells, converged) = await Task.Run(() => _gameService.ComputeFinalState(board.Cells, _options.MaxFinalStateIterations, cancellationToken), cancellationToken);

        if (!converged)
        {
            _logger.LogWarning(
                "Board {BoardId} did not reach a stable state within {MaxIter} iterations",
                id, _options.MaxFinalStateIterations);

            return Problem(
                detail: $"The board did not reach a stable or cyclic state within {_options.MaxFinalStateIterations} iterations.",
                title: "No stable state found",
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        _cache.Set(cacheKey, finalCells, TimeSpan.FromHours(1));

        Response.Headers.CacheControl = "public, max-age=3600";
        return Ok(ToBoardStateResponse(finalCells, generationsAdvanced: null));
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private ProblemDetails? ValidateCells(bool[][] cells)
    {
        if (cells is null || cells.Length == 0)
            return CreateProblem("The board must contain at least one row.", "Invalid board");

        if (cells.Length > _options.MaxBoardDimension)
            return CreateProblem($"Row count cannot exceed {_options.MaxBoardDimension}.", "Board too large");

        if (cells[0] is null || cells[0].Length == 0)
            return CreateProblem("Each row must contain at least one cell.", "Invalid board");

        var cols = cells[0].Length;

        if (cols > _options.MaxBoardDimension)
            return CreateProblem($"Column count cannot exceed {_options.MaxBoardDimension}.", "Board too large");

        if (cells.Any(row => row is null || row.Length != cols))
            return CreateProblem("All rows must have equal length.", "Invalid board");

        return null;
    }

    private static ProblemDetails CreateProblem(string detail, string title, int statusCode = StatusCodes.Status400BadRequest)
        => new() { Title = title, Detail = detail, Status = statusCode };

    private static BoardStateResponse ToBoardStateResponse(bool[][] cells, int? generationsAdvanced) => new()
    {
        Cells = cells,
        Rows = cells.Length,
        Columns = cells.Length > 0 ? cells[0].Length : 0,
        GenerationsAdvanced = generationsAdvanced
    };
}
