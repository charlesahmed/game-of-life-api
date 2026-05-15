using System.Text.Json;
using GameOfLife.Core.Domain;
using GameOfLife.Core.Interfaces;
using GameOfLife.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GameOfLife.Infrastructure.Repositories;

public sealed class BoardRepository(GameOfLifeDbContext context) : IBoardRepository
{
    private readonly GameOfLifeDbContext _context = context;

    public async Task<Board> CreateAsync(Board board, CancellationToken cancellationToken = default)
    {
        var entity = new BoardEntity
        {
            Id = board.Id,
            CellsJson = JsonSerializer.Serialize(board.Cells),
            Rows = board.Rows,
            Columns = board.Columns,
            CreatedAt = board.CreatedAt
        };

        _context.Boards.Add(entity);
        await _context.SaveChangesAsync(cancellationToken);
        return board;
    }

    public async Task<Board?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await _context.Boards
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == id, cancellationToken);

        if (entity is null) return null;

        var cells = JsonSerializer.Deserialize<bool[][]>(entity.CellsJson);
        if (cells is null) return null;

        return new Board(entity.Id, cells, entity.CreatedAt);
    }
}
