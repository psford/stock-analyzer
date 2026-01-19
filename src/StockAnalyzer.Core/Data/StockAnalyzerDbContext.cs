using Microsoft.EntityFrameworkCore;
using StockAnalyzer.Core.Data.Entities;

namespace StockAnalyzer.Core.Data;

/// <summary>
/// Entity Framework Core DbContext for Stock Analyzer.
/// Supports Azure SQL Database for production and SQLite/LocalDB for development.
/// </summary>
public class StockAnalyzerDbContext : DbContext
{
    public StockAnalyzerDbContext(DbContextOptions<StockAnalyzerDbContext> options)
        : base(options)
    {
    }

    public DbSet<WatchlistEntity> Watchlists => Set<WatchlistEntity>();
    public DbSet<WatchlistTickerEntity> WatchlistTickers => Set<WatchlistTickerEntity>();
    public DbSet<TickerHoldingEntity> TickerHoldings => Set<TickerHoldingEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Watchlist configuration
        modelBuilder.Entity<WatchlistEntity>(entity =>
        {
            entity.ToTable("Watchlists");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(450);
            entity.Property(e => e.UserId).HasMaxLength(450);
            entity.Property(e => e.Name).HasMaxLength(200).IsRequired();
            entity.Property(e => e.WeightingMode).HasMaxLength(50).HasDefaultValue("equal");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("GETUTCDATE()");

            entity.HasIndex(e => e.UserId);

            // One-to-many: Watchlist -> Tickers
            entity.HasMany(e => e.Tickers)
                .WithOne(t => t.Watchlist)
                .HasForeignKey(t => t.WatchlistId)
                .OnDelete(DeleteBehavior.Cascade);

            // One-to-many: Watchlist -> Holdings
            entity.HasMany(e => e.Holdings)
                .WithOne(h => h.Watchlist)
                .HasForeignKey(h => h.WatchlistId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // WatchlistTicker configuration
        modelBuilder.Entity<WatchlistTickerEntity>(entity =>
        {
            entity.ToTable("WatchlistTickers");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.WatchlistId).HasMaxLength(450).IsRequired();
            entity.Property(e => e.Symbol).HasMaxLength(20).IsRequired();
        });

        // TickerHolding configuration
        modelBuilder.Entity<TickerHoldingEntity>(entity =>
        {
            entity.ToTable("TickerHoldings");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.WatchlistId).HasMaxLength(450).IsRequired();
            entity.Property(e => e.Symbol).HasMaxLength(20).IsRequired();
            entity.Property(e => e.Shares).HasPrecision(18, 4);
            entity.Property(e => e.DollarValue).HasPrecision(18, 2);
        });
    }
}
