namespace GameOfLife.Api.Models.Responses;

public sealed class UploadBoardResponse
{
    public Guid Id { get; set; }
    public int Rows { get; set; }
    public int Columns { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
