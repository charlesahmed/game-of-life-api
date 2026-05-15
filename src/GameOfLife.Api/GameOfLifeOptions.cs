using System.ComponentModel.DataAnnotations;

namespace GameOfLife.Api;

public sealed class GameOfLifeOptions
{
    public const string Section = "GameOfLife";

    [Range(1, 100_000)]
    public int MaxFinalStateIterations { get; set; } = 1_000;

    [Range(1, 1_000_000)]
    public int MaxGenerationsAhead { get; set; } = 10_000;

    [Range(1, 10_000)]
    public int MaxBoardDimension { get; set; } = 1_000;
}
