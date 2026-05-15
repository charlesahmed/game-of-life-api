namespace GameOfLife.Core.Domain;

public sealed class Board
{
    public Guid Id { get; init; }
    public bool[][] Cells { get; init; }
    public int Rows => Cells.Length;
    public int Columns => Cells.Length > 0 ? Cells[0].Length : 0;
    public DateTimeOffset CreatedAt { get; init; }

    public Board(Guid id, bool[][] cells, DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(cells);
        Id = id;
        Cells = cells;
        CreatedAt = createdAt;
    }
}
