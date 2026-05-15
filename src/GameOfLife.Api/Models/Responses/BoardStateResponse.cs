namespace GameOfLife.Api.Models.Responses;

public sealed class BoardStateResponse
{
    public bool[][] Cells { get; set; } = [];
    public int Rows { get; set; }
    public int Columns { get; set; }
    /// <summary>Number of generations advanced from the original uploaded state.</summary>
    public int? GenerationsAdvanced { get; set; }
}
