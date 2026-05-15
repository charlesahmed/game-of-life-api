using System.Security.Cryptography;
using GameOfLife.Core.Interfaces;

namespace GameOfLife.Core.Services;

public sealed class GameOfLifeService : IGameOfLifeService
{
    public bool[][] ComputeNextGeneration(bool[][] cells)
    {
        ArgumentNullException.ThrowIfNull(cells);

        int rows = cells.Length, cols = rows > 0 ? cells[0].Length : 0;
        var next = AllocateBoard(rows, cols);
        FillNextGeneration(cells, next, rows, cols);
        return next;
    }

    public bool[][] ComputeNGenerations(bool[][] cells, int n, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cells);

        if (n == 0) return cells;

        int rows = cells.Length, cols = rows > 0 ? cells[0].Length : 0;

        // Two pre-allocated boards swapped each generation.
        // Cuts allocations from O(n × rows) down to O(rows) regardless of n.
        var current = DeepCopy(cells, rows, cols);
        var buffer = AllocateBoard(rows, cols);

        for (var i = 0; i < n; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            FillNextGeneration(current, buffer, rows, cols);
            (current, buffer) = (buffer, current);
        }

        return current;
    }

    public (bool[][] FinalState, bool Converged) ComputeFinalState(
        bool[][] cells, int maxIterations, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cells);

        int rows = cells.Length, cols = rows > 0 ? cells[0].Length : 0;

        // Pre-size to avoid the 6+ internal rehashes that happen with the default capacity-16 start.
        var seen = new HashSet<string>(maxIterations + 1);

        var current = DeepCopy(cells, rows, cols);
        var buffer = AllocateBoard(rows, cols);

        for (var i = 0; i <= maxIterations; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var hash = GetStateHash(current);
            if (!seen.Add(hash))
                return (current, true);

            FillNextGeneration(current, buffer, rows, cols);
            (current, buffer) = (buffer, current);
        }

        return (current, false);
    }

    // Writes the next generation of `current` into the pre-allocated `next` board.
    // Splits into two paths so bounds checking is paid only on border cells:
    //   • interior  – O((R-2)(C-2)) cells, no bounds check, row refs hoisted for cache locality
    //   • border    – O(2R + 2C)    cells, bounded CountLiveNeighbors
    private static void FillNextGeneration(bool[][] current, bool[][] next, int rows, int cols)
    {
        // ── interior hot path ─────────────────────────────────────────────────
        for (var r = 1; r < rows - 1; r++)
        {
            var prev = current[r - 1];
            var curr = current[r];
            var below = current[r + 1];
            var dest = next[r];

            for (var c = 1; c < cols - 1; c++)
            {
                var neighbors =
                    (prev[c - 1] ? 1 : 0) + (prev[c] ? 1 : 0) + (prev[c + 1] ? 1 : 0) +
                    (curr[c - 1] ? 1 : 0) + (curr[c + 1] ? 1 : 0) +
                    (below[c - 1] ? 1 : 0) + (below[c] ? 1 : 0) + (below[c + 1] ? 1 : 0);

                dest[c] = curr[c] ? neighbors is 2 or 3 : neighbors == 3;
            }
        }

        // ── border cold path ──────────────────────────────────────────────────
        // Guards prevent double-writes when rows == 1 (top == bottom) or cols == 1 (left == right).
        if (rows == 0 || cols == 0) return;

        // Top row
        for (var c = 0; c < cols; c++)
            ApplyRule(current, next, 0, c, rows, cols);

        // Bottom row — only if distinct from top
        if (rows > 1)
        {
            for (var c = 0; c < cols; c++)
                ApplyRule(current, next, rows - 1, c, rows, cols);
        }

        // Left column (excluding corners already handled)
        for (var r = 1; r < rows - 1; r++)
            ApplyRule(current, next, r, 0, rows, cols);

        // Right column — only if distinct from left
        if (cols > 1)
        {
            for (var r = 1; r < rows - 1; r++)
                ApplyRule(current, next, r, cols - 1, rows, cols);
        }
    }

    private static void ApplyRule(bool[][] current, bool[][] next, int r, int c, int rows, int cols)
    {
        var neighbors = CountLiveNeighbors(current, r, c, rows, cols);
        next[r][c] = current[r][c] ? neighbors is 2 or 3 : neighbors == 3;
    }

    private static int CountLiveNeighbors(bool[][] cells, int row, int col, int rows, int cols)
    {
        var count = 0;
        for (var dr = -1; dr <= 1; dr++)
        {
            for (var dc = -1; dc <= 1; dc++)
            {
                if (dr == 0 && dc == 0) continue;
                int nr = row + dr, nc = col + dc;
                if ((uint)nr < (uint)rows && (uint)nc < (uint)cols && cells[nr][nc])
                    count++;
            }
        }

        return count;
    }

    // Bit-packs the grid (1 bit per cell → rows*cols/8 bytes) then hashes with SHA256.
    // For a 1000×1000 board this stores 32 bytes in the HashSet instead of a 1 MB string,
    // cutting cycle-detection memory from O(iterations × R × C) down to O(iterations).
    private static string GetStateHash(bool[][] cells)
    {
        int rows = cells.Length, cols = rows > 0 ? cells[0].Length : 0;
        var packed = new byte[(rows * cols + 7) / 8];
        var bit = 0;
        foreach (var row in cells)
        {
            foreach (var cell in row)
            {
                if (cell) packed[bit >> 3] |= (byte)(1 << (bit & 7));
                bit++;
            }
        }

        return Convert.ToHexString(SHA256.HashData(packed));
    }

    private static bool[][] AllocateBoard(int rows, int cols)
    {
        var board = new bool[rows][];
        for (var r = 0; r < rows; r++) board[r] = new bool[cols];
        return board;
    }

    // Array.Copy is a JIT-intrinsic memcpy — faster than a manual loop over bools.
    private static bool[][] DeepCopy(bool[][] source, int rows, int cols)
    {
        var copy = new bool[rows][];
        for (var r = 0; r < rows; r++)
        {
            copy[r] = new bool[cols];
            Array.Copy(source[r], copy[r], cols);
        }
        return copy;
    }
}
