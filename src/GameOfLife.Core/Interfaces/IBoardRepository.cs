using GameOfLife.Core.Domain;

namespace GameOfLife.Core.Interfaces;

public interface IBoardRepository
{
    Task<Board> CreateAsync(Board board, CancellationToken cancellationToken = default);
    Task<Board?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
}
