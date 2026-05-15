namespace GameOfLife.Api.Models.Responses;

public sealed class BoardDetailsResponse
{
    public Guid Id { get; set; }
    public bool[][] Cells { get; set; } = [];
    public int Rows { get; set; }
    public int Columns { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
