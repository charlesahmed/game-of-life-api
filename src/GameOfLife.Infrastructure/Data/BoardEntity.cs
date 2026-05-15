namespace GameOfLife.Infrastructure.Data;

public sealed class BoardEntity
{
    public Guid Id { get; set; }

    /// <summary>JSON-serialised bool[][] – chosen over a separate junction table for simplicity
    /// while keeping the schema flat and the round-trip cheap.</summary>
    public string CellsJson { get; set; } = string.Empty;

    public int Rows { get; set; }
    public int Columns { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
