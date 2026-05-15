using System.Threading.RateLimiting;
using GameOfLife.Api;
using GameOfLife.Core.Interfaces;
using GameOfLife.Core.Services;
using GameOfLife.Infrastructure.Data;
using GameOfLife.Infrastructure.Repositories;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ── Options ──────────────────────────────────────────────────────────────────
builder.Services.AddOptions<GameOfLifeOptions>()
    .BindConfiguration(GameOfLifeOptions.Section)
    .ValidateDataAnnotations()
    .ValidateOnStart();

// ── Database ─────────────────────────────────────────────────────────────────
var dbPath = builder.Configuration.GetValue<string>("Database:Path") ?? "gameoflife.db";
builder.Services.AddDbContext<GameOfLifeDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}"));

// ── Domain services ───────────────────────────────────────────────────────────
builder.Services.AddScoped<IBoardRepository, BoardRepository>();
builder.Services.AddSingleton<IGameOfLifeService, GameOfLifeService>();

// ── Caching ───────────────────────────────────────────────────────────────────
// Used to memoize /final results: boards are immutable, so the final state is
// deterministic and worth keeping in-process rather than recomputing every call.
builder.Services.AddMemoryCache();

// ── Rate limiting ─────────────────────────────────────────────────────────────
// Caps concurrent expensive computations to prevent thread-pool exhaustion.
// The queue absorbs short bursts; requests beyond it receive 429 immediately.
builder.Services.AddRateLimiter(options =>
{
    options.AddConcurrencyLimiter(RateLimitPolicies.Computation, limiterOptions =>
    {
        limiterOptions.PermitLimit = 10;
        limiterOptions.QueueLimit = 5;
        limiterOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
    });
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

// ── Web infrastructure ────────────────────────────────────────────────────────
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new()
    {
        Title = "Conway's Game of Life API",
        Version = "v1",
        Description = "RESTful API for Conway's Game of Life with persistent board storage."
    });

    var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
        options.IncludeXmlComments(xmlPath);
});

builder.Services.AddProblemDetails();

builder.Services.AddHealthChecks()
    .AddDbContextCheck<GameOfLifeDbContext>("sqlite");

// ── Build ─────────────────────────────────────────────────────────────────────
var app = builder.Build();

// Apply migrations / ensure DB exists before accepting requests
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<GameOfLifeDbContext>();
    db.Database.EnsureCreated();
}

app.UseSwagger();
app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Game of Life v1"));

app.UseHttpsRedirection();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseRateLimiter();
app.MapControllers();
app.MapHealthChecks("/health");

app.Run();

// Expose Program for WebApplicationFactory in integration tests
public partial class Program { }
