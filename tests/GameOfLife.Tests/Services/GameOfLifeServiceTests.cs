using FluentAssertions;
using GameOfLife.Core.Services;
using Xunit;

namespace GameOfLife.Tests.Services;

public sealed class GameOfLifeServiceTests
{
    private readonly GameOfLifeService _sut = new();

    // ── ComputeNextGeneration ────────────────────────────────────────────────

    [Fact]
    public void NextGeneration_AllDeadBoard_RemainsAllDead()
    {
        var board = B(["000", "000", "000"]);
        _sut.ComputeNextGeneration(board).Should().BeEquivalentTo(board);
    }

    [Fact]
    public void NextGeneration_SingleLiveCell_Dies()
    {
        // A lone cell has 0 neighbours → underpopulation
        var board = B(["010", "000", "000"]);
        var next = _sut.ComputeNextGeneration(board);
        next[0][1].Should().BeFalse();
    }

    [Fact]
    public void NextGeneration_Blinker_Oscillates()
    {
        // Horizontal blinker
        //  . . .       . X .
        //  X X X  -->  . X .
        //  . . .       . X .
        var horizontal = B(["000", "111", "000"]);
        var vertical = B(["010", "010", "010"]);

        _sut.ComputeNextGeneration(horizontal).Should().BeEquivalentTo(vertical);
        _sut.ComputeNextGeneration(vertical).Should().BeEquivalentTo(horizontal);
    }

    [Fact]
    public void NextGeneration_Block_IsStill()
    {
        // 2×2 block is a still life
        var block = B(["0000", "0110", "0110", "0000"]);
        _sut.ComputeNextGeneration(block).Should().BeEquivalentTo(block);
    }

    [Fact]
    public void NextGeneration_Glider_AdvancesCorrectly()
    {
        // Glider in a 6×6 grid, one step
        var initial = B(
        [
            "010000",
            "001000",
            "111000",
            "000000",
            "000000",
            "000000"
        ]);

        var expected = B(
        [
            "000000",
            "101000",
            "011000",
            "010000",
            "000000",
            "000000"
        ]);

        _sut.ComputeNextGeneration(initial).Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void NextGeneration_DeadCellWithThreeNeighbors_BecomesAlive()
    {
        // Dead centre cell has exactly 3 live neighbours
        var board = B(["110", "100", "000"]);
        var next = _sut.ComputeNextGeneration(board);
        next[1][1].Should().BeTrue();
    }

    [Fact]
    public void NextGeneration_LiveCellWithFourNeighbors_Dies()
    {
        // Centre cell has 4 live neighbours → overpopulation
        var board = B(["010", "111", "010"]);
        var next = _sut.ComputeNextGeneration(board);
        next[1][1].Should().BeFalse();
    }

    [Fact]
    public void NextGeneration_Toad_Oscillates()
    {
        // Toad: period-2 oscillator with different geometry from the Blinker.
        // Exercises the interior hot path for a non-trivial pattern.
        var phaseA = B(
        [
            "000000",
            "001110",
            "011100",
            "000000"
        ]);
        var phaseB = B(
        [
            "000100",
            "010010",
            "010010",
            "001000"
        ]);

        _sut.ComputeNextGeneration(phaseA).Should().BeEquivalentTo(phaseB);
        _sut.ComputeNextGeneration(phaseB).Should().BeEquivalentTo(phaseA);
    }

    [Fact]
    public void NextGeneration_SingleCellBoard_Dies()
    {
        // 1×1 board: a single live cell has zero neighbours and dies.
        // Exercises the border-only code path with both row and column guards firing.
        var board = new[] { new[] { true } };
        var next = _sut.ComputeNextGeneration(board);
        next[0][0].Should().BeFalse();
    }

    [Fact]
    public void NextGeneration_SingleRowBoard_AllCellsDie()
    {
        // 1×N board: every cell is on the border; max neighbours is 2 → all die or stay dead.
        var board = B(["11011"]);
        var next = _sut.ComputeNextGeneration(board);
        next[0].Should().AllBeEquivalentTo(false);
    }

    [Fact]
    public void NextGeneration_NullInput_Throws()
    {
        Action act = () => _sut.ComputeNextGeneration(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // ── ComputeNGenerations ─────────────────────────────────────────────────

    [Fact]
    public void NGenerations_Zero_ReturnsSameBoard()
    {
        var board = B(["010", "111", "010"]);
        _sut.ComputeNGenerations(board, 0).Should().BeEquivalentTo(board);
    }

    [Fact]
    public void NGenerations_Blinker_TwoSteps_ReturnsOriginal()
    {
        var horizontal = B(["000", "111", "000"]);
        // Period-2 oscillator: 2 steps returns to original state
        _sut.ComputeNGenerations(horizontal, 2).Should().BeEquivalentTo(horizontal);
    }

    [Fact]
    public void NGenerations_DoesNotMutateInput()
    {
        // Double-buffer optimisation must not write back into the caller's array.
        // Take a snapshot of the input, run a non-trivial computation, assert input is unchanged.
        var input = B(["000", "111", "000"]);
        var snapshot = input.Select(r => (bool[])r.Clone()).ToArray();

        _ = _sut.ComputeNGenerations(input, 5);

        input.Should().BeEquivalentTo(snapshot);
    }

    // ── ComputeFinalState ────────────────────────────────────────────────────

    [Fact]
    public void FinalState_AllDeadBoard_ConvergesImmediately()
    {
        var board = B(["000", "000", "000"]);
        var (finalState, converged) = _sut.ComputeFinalState(board, maxIterations: 100);

        converged.Should().BeTrue();
        finalState.Should().BeEquivalentTo(board);
    }

    [Fact]
    public void FinalState_Block_IsAlreadyStable()
    {
        var block = B(["0000", "0110", "0110", "0000"]);
        var (finalState, converged) = _sut.ComputeFinalState(block, maxIterations: 100);

        converged.Should().BeTrue();
        finalState.Should().BeEquivalentTo(block);
    }

    [Fact]
    public void FinalState_Blinker_ConvergesAsCycle()
    {
        var blinker = B(["000", "111", "000"]);
        var (_, converged) = _sut.ComputeFinalState(blinker, maxIterations: 100);
        converged.Should().BeTrue();
    }

    [Fact]
    public void FinalState_VeryLowMaxIterations_ReportsNotConverged()
    {
        // A glider in a bounded grid will not settle in 1 iteration
        var glider = B(
        [
            "010000",
            "001000",
            "111000",
            "000000",
            "000000",
            "000000"
        ]);

        var (_, converged) = _sut.ComputeFinalState(glider, maxIterations: 1);
        converged.Should().BeFalse();
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>Builds a bool[][] from an array of '0'/'1' strings.</summary>
    private static bool[][] B(string[] rows) =>
        rows.Select(r => r.Select(c => c == '1').ToArray()).ToArray();
}
