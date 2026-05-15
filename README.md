# Conway's Game of Life — REST API

A production-ready .NET 8 Web API for Conway's Game of Life with persistent board storage, cycle-detection convergence, structured error responses, and GitHub Actions CI.

---

## Quick start

```bash
cd src/GameOfLife.Api
dotnet run
# Swagger UI   → http://localhost:5000/swagger
# Health check → http://localhost:5000/health
```

```bash
# Run all tests (unit + integration, no external DB required)
dotnet test --logger "console;verbosity=normal"
```

---

## API endpoints

| Method | Route | Description |
|--------|-------|-------------|
| `POST` | `/api/boards` | Upload a board; returns a stable `id` |
| `GET` | `/api/boards/{id}` | Retrieve the original uploaded board |
| `GET` | `/api/boards/{id}/next` | Board state after exactly one generation |
| `GET` | `/api/boards/{id}/states/{n}` | Board state after N generations (1 ≤ N ≤ 10 000) |
| `GET` | `/api/boards/{id}/final` | Final stable or cyclic state |
| `GET` | `/health` | Health check (includes SQLite DB probe) |

### Upload a board

```http
POST /api/boards
Content-Type: application/json

{
  "cells": [
    [false, true,  false],
    [false, false, true ],
    [true,  true,  true ]
  ]
}
```

`201 Created` — `Location` header points to the new resource:

```json
{ "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6", "rows": 3, "columns": 3, "createdAt": "2025-01-01T00:00:00Z" }
```

### Compute states

```http
GET /api/boards/{id}/next
GET /api/boards/{id}/states/10
GET /api/boards/{id}/final
```

All three return `BoardStateResponse`:

```json
{ "cells": [[false, false], [false, false]], "rows": 2, "columns": 2, "generationsAdvanced": 10 }
```

`generationsAdvanced` is `null` for `/final`; convergence depth is not bounded by definition.  
`/final` returns `422 Unprocessable Entity` when the board has not stabilised within the configured iteration limit.

### Error shape

All errors follow [RFC 7807 Problem Details](https://www.rfc-editor.org/rfc/rfc7807):

```json
{ "title": "Board not found", "detail": "No board with ID '…' exists.", "status": 404 }
```

---

## Architecture

```
GameOfLife/
├── src/
│   ├── GameOfLife.Core/            # Domain + pure computation — zero external dependencies
│   │   ├── Domain/Board.cs         # Board aggregate root
│   │   ├── Interfaces/             # IBoardRepository, IGameOfLifeService
│   │   └── Services/GameOfLifeService.cs
│   ├── GameOfLife.Infrastructure/  # EF Core 8 + SQLite persistence
│   │   ├── Data/                   # GameOfLifeDbContext, BoardEntity
│   │   └── Repositories/           # BoardRepository
│   └── GameOfLife.Api/             # ASP.NET Core 8 Web API
│       ├── Controllers/            # BoardsController
│       ├── Models/                 # Request / response DTOs
│       ├── GameOfLifeOptions.cs    # Typed, validated configuration
│       └── Program.cs
└── tests/
    └── GameOfLife.Tests/
        ├── Services/               # Unit tests — GameOfLifeService
        └── Api/                    # Integration tests — BoardsController
```

### Key design decisions

**Clean Architecture**  
`Core` carries no external dependencies — game logic compiles and tests without any framework. `Infrastructure` and `Api` both reference `Core` and are replaceable independently (swap SQLite → Postgres by changing one line in `Program.cs`).

**Board cells as JSON column**  
A 2-D boolean grid is stored as a JSON-serialised `bool[][]` in a single column. This avoids a three-table `(boards → rows → cells)` schema; reads are O(1) by primary key. `MaxBoardDimension = 1000` keeps the worst-case JSON payload under ~1 MB.

**Generation computation — O(R × C) time, O(R × C) space**  
`GameOfLifeService` uses a double-buffer swap (allocate once per call, never mid-loop) and separates the interior hot path from the border cold path to eliminate per-cell bounds checks in the common case. For a 1 000 × 1 000 board that is 1 M tight iterations with no branch on 99.6 % of cells.

**Cycle detection in `ComputeFinalState` — O(G × R × C) time, O(G × 32) space**  
Each generation's state is fingerprinted with SHA-256 over a bit-packed byte array: `byte[(R × C + 7) / 8]` — 1 bit per cell rather than 1 byte per character. A 1 000 × 1 000 board produces a 125 KB buffer hashed to 32 bytes, kept in a `HashSet<string>` (hex-encoded). The first repeated hash terminates the loop regardless of period length, correctly handling both still-lifes (period 1) and oscillators (period N). The hash set grows by exactly 32 bytes per generation: memory is O(G) in the number of generations, not in board size.

Alternatives considered and rejected:
- **Byte-per-cell string hash**: 8× larger buffer, same asymptotic complexity, no benefit.
- **Canonicalised state comparison** (direct 2-D array equality): O(R × C) per comparison, O(G × R × C) total memory — unacceptable for large G.
- **Bloom filter**: probabilistic false-positives would misidentify convergence; correctness trumps the marginal memory gain.

**Async offload of CPU-bound computation**  
All three computation paths (`/next`, `/states/{n}`, `/final`) use `await Task.Run(...)` to release the I/O thread-pool thread during the CPU loop. Under load this keeps the thread pool responsive to new HTTP requests rather than starving it with long-running synchronous work.

**In-process cache for `/final`**  
Boards are immutable after upload; their final state is therefore deterministic and worth memoising. `IMemoryCache` stores the result for 1 hour, keyed by board ID. Subsequent calls skip the convergence loop entirely. The `Cache-Control: public, max-age=3600` response header propagates this guarantee to CDN/reverse-proxy layers.

**Concurrency limiter on expensive endpoints**  
`/states/{n}` and `/final` are decorated with `[EnableRateLimiting("computation")]`. The concurrency limiter (permit=10, queue=5) prevents thread-pool exhaustion under burst load; requests beyond the queue receive `429 Too Many Requests` immediately.

**Fail-fast configuration validation**  
`GameOfLifeOptions` uses `[Range]` data annotations combined with `.ValidateOnStart()`. A misconfigured deployment fails at startup rather than silently degrading at runtime.

---

## Configuration

| Key | Default | Description |
|-----|---------|-------------|
| `Database:Path` | `gameoflife.db` | SQLite file path |
| `GameOfLife:MaxBoardDimension` | `1000` | Maximum rows or columns per uploaded board |
| `GameOfLife:MaxFinalStateIterations` | `1000` | Convergence loop iteration ceiling for `/final` |
| `GameOfLife:MaxGenerationsAhead` | `10000` | Maximum N accepted by `/states/{n}` |

---

## Complexity summary

| Operation | Time | Extra memory |
|-----------|------|-------------|
| Upload | O(R × C) validation | O(1) beyond request body |
| `/next` | O(R × C) | O(R × C) — one generation buffer |
| `/states/{n}` | O(N × R × C) | O(R × C) — double buffer, reused each step |
| `/final` (cache miss) | O(G × R × C) | O(G × 32 B) — hash set entries only |
| `/final` (cache hit) | O(1) | O(1) |

Where R = rows, C = columns, N = requested generations, G = generations until convergence.

---

## Running tests

```bash
dotnet test --logger "console;verbosity=normal"
```

- **Integration tests** use `WebApplicationFactory<Program>` with EF Core's InMemory provider — no SQLite file or external process required. Each test class gets its own named in-memory database via `IClassFixture<GameOfLifeFactory>`.
- **Unit tests** (`GameOfLifeServiceTests`) exercise all Conway rules in isolation: still-life stability (Block), oscillation (Blinker period 2), glider propagation, birth/survival/overpopulation corner cases, cycle detection, and convergence-failure signalling.

---

## CI pipeline

`.github/workflows/ci.yml` runs on every push and pull request:

1. **Restore** — `dotnet restore`
2. **Format check** — `dotnet format --verify-no-changes` (enforces `.editorconfig` and style rules; fails the build on any diff)
3. **Build** — `dotnet build /p:TreatWarningsAsErrors=true` (zero-warning policy)
4. **Test + coverage** — `dotnet test --collect:"XPlat Code Coverage"`
5. **Vulnerability scan** — `dotnet list package --vulnerable` (fails if any transitive dependency has a known CVE)
