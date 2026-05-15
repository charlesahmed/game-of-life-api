namespace GameOfLife.Core.Interfaces;

public interface IGameOfLifeService
{
    bool[][] ComputeNextGeneration(bool[][] cells);
    bool[][] ComputeNGenerations(bool[][] cells, int n, CancellationToken cancellationToken = default);

    /// <summary>
    /// Advances the simulation until the board reaches a static or cyclic steady state.
    /// Returns the converged state and a flag indicating whether convergence was achieved
    /// within <paramref name="maxIterations"/>.
    /// </summary>
    (bool[][] FinalState, bool Converged) ComputeFinalState(
        bool[][] cells, int maxIterations, CancellationToken cancellationToken = default);
}
