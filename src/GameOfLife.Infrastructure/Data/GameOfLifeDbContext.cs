using Microsoft.EntityFrameworkCore;

namespace GameOfLife.Infrastructure.Data;

public sealed class GameOfLifeDbContext(DbContextOptions<GameOfLifeDbContext> options) : DbContext(options)
{
    public DbSet<BoardEntity> Boards => Set<BoardEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BoardEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.CellsJson).IsRequired();
            entity.Property(e => e.CreatedAt).IsRequired();
        });
    }
}
