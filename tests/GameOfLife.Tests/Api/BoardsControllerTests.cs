using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using GameOfLife.Api.Models.Requests;
using GameOfLife.Api.Models.Responses;
using GameOfLife.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GameOfLife.Tests.Api;

public sealed class GameOfLifeFactory : WebApplicationFactory<Program>
{
    private readonly string _dbName = $"TestDb_{Guid.NewGuid()}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<GameOfLifeDbContext>));
            if (descriptor is not null)
                services.Remove(descriptor);

            services.AddDbContext<GameOfLifeDbContext>(options =>
                options.UseInMemoryDatabase(_dbName));
        });
    }
}

public sealed class BoardsControllerTests(GameOfLifeFactory factory) : IClassFixture<GameOfLifeFactory>
{
    private readonly HttpClient _client = factory.CreateClient();
    private static readonly bool[] _singleFalse = [false];

    // ── Get board ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetBoard_KnownId_Returns200WithOriginalCells()
    {
        var id = await UploadAsync(Glider());

        var response = await _client.GetAsync($"/api/boards/{id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<BoardDetailsResponse>();
        body!.Id.Should().Be(id);
        body.Cells.Should().BeEquivalentTo(Glider());
        body.Rows.Should().Be(6);
        body.Columns.Should().Be(6);
    }

    [Fact]
    public async Task GetBoard_UnknownId_Returns404()
    {
        var response = await _client.GetAsync($"/api/boards/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Upload ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task UploadBoard_ValidBoard_Returns201WithId()
    {
        var request = new UploadBoardRequest
        {
            Cells = Glider()
        };

        var response = await _client.PostAsJsonAsync("/api/boards", request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<UploadBoardResponse>();
        body!.Id.Should().NotBeEmpty();
        body.Rows.Should().Be(6);
        body.Columns.Should().Be(6);
        response.Headers.Location.Should().NotBeNull();
    }

    [Fact]
    public async Task UploadBoard_EmptyBoard_Returns400()
    {
        var request = new UploadBoardRequest { Cells = [] };
        var response = await _client.PostAsJsonAsync("/api/boards", request);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UploadBoard_JaggedRows_Returns400()
    {
        var request = new UploadBoardRequest
        {
            Cells = [[true, false], [true]]  // unequal row lengths
        };
        var response = await _client.PostAsJsonAsync("/api/boards", request);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UploadBoard_DimensionsExceedMax_Returns400()
    {
        // MaxBoardDimension default = 1000 → 1001 rows must be rejected.
        var oversized = Enumerable.Range(0, 1001)
            .Select(_ => _singleFalse)
            .ToArray();
        var request = new UploadBoardRequest { Cells = oversized };

        var response = await _client.PostAsJsonAsync("/api/boards", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── Next state ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetNextState_KnownBoard_Returns200WithNextGeneration()
    {
        var id = await UploadAsync(Block());

        var response = await _client.GetAsync($"/api/boards/{id}/next");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<BoardStateResponse>();
        // Block is a still life → next generation equals itself
        body!.Cells.Should().BeEquivalentTo(Block());
        body.GenerationsAdvanced.Should().Be(1);
    }

    [Fact]
    public async Task GetNextState_UnknownId_Returns404()
    {
        var response = await _client.GetAsync($"/api/boards/{Guid.NewGuid()}/next");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── N states ahead ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetStatesAhead_Blinker2Steps_ReturnsOriginalState()
    {
        var id = await UploadAsync(Blinker());
        var response = await _client.GetAsync($"/api/boards/{id}/states/2");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<BoardStateResponse>();
        body!.Cells.Should().BeEquivalentTo(Blinker());
    }

    [Fact]
    public async Task GetStatesAhead_NZero_Returns400()
    {
        var id = await UploadAsync(Block());
        var response = await _client.GetAsync($"/api/boards/{id}/states/0");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetStatesAhead_NegativeN_Returns400()
    {
        var id = await UploadAsync(Block());
        var response = await _client.GetAsync($"/api/boards/{id}/states/-5");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetStatesAhead_NExceedsConfiguredMax_Returns400()
    {
        // MaxGenerationsAhead default = 10000 → 10001 must be rejected before computation runs.
        var id = await UploadAsync(Block());
        var response = await _client.GetAsync($"/api/boards/{id}/states/10001");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── Final state ───────────────────────────────────────────────────────────

    [Fact]
    public async Task GetFinalState_BlockBoard_Returns200SameState()
    {
        var id = await UploadAsync(Block());
        var response = await _client.GetAsync($"/api/boards/{id}/final");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<BoardStateResponse>();
        body!.Cells.Should().BeEquivalentTo(Block());
    }

    [Fact]
    public async Task GetFinalState_AllDeadBoard_Returns200AllDead()
    {
        var dead = new[] { new[] { false, false }, [false, false] };
        var id = await UploadAsync(dead);

        var response = await _client.GetAsync($"/api/boards/{id}/final");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<BoardStateResponse>();
        body!.Cells.Should().BeEquivalentTo(dead);
    }

    [Fact]
    public async Task GetFinalState_UnknownId_Returns404()
    {
        var response = await _client.GetAsync($"/api/boards/{Guid.NewGuid()}/final");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetFinalState_CalledTwice_ReturnsConsistentResultAndAdvertisesCaching()
    {
        // Cache-correctness check: two sequential calls for the same board must return
        // identical bodies and both must carry the Cache-Control header.
        var id = await UploadAsync(Block());

        var first = await _client.GetAsync($"/api/boards/{id}/final");
        var second = await _client.GetAsync($"/api/boards/{id}/final");

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.OK);

        var firstBody = await first.Content.ReadFromJsonAsync<BoardStateResponse>();
        var secondBody = await second.Content.ReadFromJsonAsync<BoardStateResponse>();

        secondBody!.Cells.Should().BeEquivalentTo(firstBody!.Cells);
        first.Headers.CacheControl!.MaxAge.Should().NotBeNull();
        second.Headers.CacheControl!.MaxAge.Should().NotBeNull();
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private async Task<Guid> UploadAsync(bool[][] cells)
    {
        var response = await _client.PostAsJsonAsync("/api/boards", new UploadBoardRequest { Cells = cells });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<UploadBoardResponse>();
        return body!.Id;
    }

    // Still life
    private static bool[][] Block() =>
    [
        [false, false, false, false],
        [false, true,  true,  false],
        [false, true,  true,  false],
        [false, false, false, false]
    ];

    // Period-2 oscillator (horizontal)
    private static bool[][] Blinker() =>
    [
        [false, false, false],
        [true,  true,  true],
        [false, false, false]
    ];

    private static bool[][] Glider() =>
    [
        [false, true,  false, false, false, false],
        [false, false, true,  false, false, false],
        [true,  true,  true,  false, false, false],
        [false, false, false, false, false, false],
        [false, false, false, false, false, false],
        [false, false, false, false, false, false]
    ];
}
