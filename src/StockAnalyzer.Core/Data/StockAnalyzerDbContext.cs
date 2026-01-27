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

    // Operational tables (dbo schema)
    public DbSet<WatchlistEntity> Watchlists => Set<WatchlistEntity>();
    public DbSet<WatchlistTickerEntity> WatchlistTickers => Set<WatchlistTickerEntity>();
    public DbSet<TickerHoldingEntity> TickerHoldings => Set<TickerHoldingEntity>();
    public DbSet<SymbolEntity> Symbols => Set<SymbolEntity>();
    public DbSet<CachedImageEntity> CachedImages => Set<CachedImageEntity>();
    public DbSet<CachedSentimentEntity> CachedSentiments => Set<CachedSentimentEntity>();

    // Domain data tables (data schema)
    public DbSet<SecurityMasterEntity> SecurityMaster => Set<SecurityMasterEntity>();
    public DbSet<PriceEntity> Prices => Set<PriceEntity>();
    public DbSet<SourceEntity> Sources => Set<SourceEntity>();
    public DbSet<BusinessCalendarEntity> BusinessCalendar => Set<BusinessCalendarEntity>();
    public DbSet<TrackedSecurityEntity> TrackedSecurities => Set<TrackedSecurityEntity>();

    // Aggregation tables (data schema)
    public DbSet<CoverageSummaryEntity> CoverageSummary => Set<CoverageSummaryEntity>();

    // Staging tables (staging schema)
    public DbSet<PriceStagingEntity> PriceStaging => Set<PriceStagingEntity>();

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

        // Symbol configuration (for fast local ticker search)
        modelBuilder.Entity<SymbolEntity>(entity =>
        {
            entity.ToTable("Symbols");
            entity.HasKey(e => e.Symbol);
            entity.Property(e => e.Symbol).HasMaxLength(20);
            entity.Property(e => e.DisplaySymbol).HasMaxLength(50);
            entity.Property(e => e.Description).HasMaxLength(500);
            entity.Property(e => e.Type).HasMaxLength(50);
            entity.Property(e => e.Exchange).HasMaxLength(50);
            entity.Property(e => e.Mic).HasMaxLength(20);
            entity.Property(e => e.Currency).HasMaxLength(10);
            entity.Property(e => e.Figi).HasMaxLength(50);
            entity.Property(e => e.Country).HasMaxLength(10).HasDefaultValue("US");
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.LastUpdated).HasDefaultValueSql("GETUTCDATE()");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("GETUTCDATE()");

            // Indexes for fast search
            entity.HasIndex(e => e.Description).HasDatabaseName("IX_Symbols_Description");
            entity.HasIndex(e => e.Type).HasDatabaseName("IX_Symbols_Type");
            entity.HasIndex(e => new { e.Country, e.IsActive }).HasDatabaseName("IX_Symbols_Country_Active");
        });

        // CachedImage configuration (for persistent image cache)
        modelBuilder.Entity<CachedImageEntity>(entity =>
        {
            entity.ToTable("CachedImages");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).UseIdentityColumn();
            entity.Property(e => e.ImageType).HasMaxLength(10).IsRequired();
            entity.Property(e => e.ImageData).IsRequired();  // VARBINARY(MAX)
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("GETUTCDATE()");

            // Index for efficient type filtering
            entity.HasIndex(e => e.ImageType).HasDatabaseName("IX_CachedImages_ImageType");
        });

        // CachedSentiment configuration (for FinBERT sentiment cache)
        modelBuilder.Entity<CachedSentimentEntity>(entity =>
        {
            entity.ToTable("CachedSentiments");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).UseIdentityColumn();
            entity.Property(e => e.HeadlineHash).HasMaxLength(64).IsRequired();  // SHA256 = 64 hex chars
            entity.Property(e => e.Headline).HasMaxLength(1000).IsRequired();
            entity.Property(e => e.Sentiment).HasMaxLength(20);
            entity.Property(e => e.Confidence).HasPrecision(5, 4);
            entity.Property(e => e.PositiveProb).HasPrecision(5, 4);
            entity.Property(e => e.NegativeProb).HasPrecision(5, 4);
            entity.Property(e => e.NeutralProb).HasPrecision(5, 4);
            entity.Property(e => e.AnalyzerVersion).HasMaxLength(50);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("GETUTCDATE()");

            // Index for fast hash lookup
            entity.HasIndex(e => e.HeadlineHash)
                .IsUnique()
                .HasDatabaseName("IX_CachedSentiments_HeadlineHash");

            // Index for finding pending items
            entity.HasIndex(e => new { e.IsPending, e.CreatedAt })
                .HasDatabaseName("IX_CachedSentiments_Pending");
        });

        // ========================================================================
        // Domain Data Tables (data schema)
        // These tables store the core business data the application serves,
        // separate from operational/infrastructure tables above.
        // ========================================================================

        // SecurityMaster configuration
        modelBuilder.Entity<SecurityMasterEntity>(entity =>
        {
            entity.ToTable("SecurityMaster", "data");
            entity.HasKey(e => e.SecurityAlias);
            entity.Property(e => e.SecurityAlias).UseIdentityColumn();
            entity.Property(e => e.PrimaryAssetId).HasMaxLength(50);
            entity.Property(e => e.IssueName).HasMaxLength(200).IsRequired();
            entity.Property(e => e.TickerSymbol).HasMaxLength(20).IsRequired();
            entity.Property(e => e.Exchange).HasMaxLength(50);
            entity.Property(e => e.SecurityType).HasMaxLength(50);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("GETUTCDATE()");

            // Unique index on ticker symbol - most common lookup
            entity.HasIndex(e => e.TickerSymbol)
                .IsUnique()
                .HasDatabaseName("IX_SecurityMaster_TickerSymbol");

            // Index for filtering active securities
            entity.HasIndex(e => e.IsActive)
                .HasDatabaseName("IX_SecurityMaster_IsActive");

            // Index for filtering tracked securities (for Crawler)
            entity.HasIndex(e => e.IsTracked)
                .HasDatabaseName("IX_SecurityMaster_IsTracked");

            // Composite index for common query: active + tracked
            entity.HasIndex(e => new { e.IsActive, e.IsTracked })
                .HasDatabaseName("IX_SecurityMaster_IsActive_IsTracked");

            // One-to-many: SecurityMaster -> Prices
            entity.HasMany(e => e.Prices)
                .WithOne(p => p.Security)
                .HasForeignKey(p => p.SecurityAlias)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Prices configuration
        modelBuilder.Entity<PriceEntity>(entity =>
        {
            entity.ToTable("Prices", "data");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).UseIdentityColumn();
            entity.Property(e => e.EffectiveDate).HasColumnType("date");  // DATE only, no time
            entity.Property(e => e.Open).HasPrecision(18, 4);
            entity.Property(e => e.High).HasPrecision(18, 4);
            entity.Property(e => e.Low).HasPrecision(18, 4);
            entity.Property(e => e.Close).HasPrecision(18, 4);
            entity.Property(e => e.Volatility).HasPrecision(10, 6);
            entity.Property(e => e.AdjustedClose).HasPrecision(18, 4);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("GETUTCDATE()");

            // Composite unique index: one price per security per date
            // This is the primary workhorse index for range queries
            entity.HasIndex(e => new { e.SecurityAlias, e.EffectiveDate })
                .IsUnique()
                .HasDatabaseName("IX_Prices_SecurityAlias_EffectiveDate");

            // Index for batch date lookups (e.g., "all prices for 2025-01-15")
            entity.HasIndex(e => e.EffectiveDate)
                .HasDatabaseName("IX_Prices_EffectiveDate");
        });

        // Sources configuration (dictionary table for business calendars)
        modelBuilder.Entity<SourceEntity>(entity =>
        {
            entity.ToTable("Sources", "data");
            entity.HasKey(e => e.SourceId);
            entity.Property(e => e.SourceId).ValueGeneratedNever(); // Manually assigned IDs
            entity.Property(e => e.SourceShortName).HasMaxLength(20).IsRequired();
            entity.Property(e => e.SourceLongName).HasMaxLength(200).IsRequired();

            // Unique index on short name
            entity.HasIndex(e => e.SourceShortName)
                .IsUnique()
                .HasDatabaseName("IX_Sources_SourceShortName");

            // One-to-many: Source -> BusinessCalendar
            entity.HasMany(e => e.BusinessCalendarEntries)
                .WithOne(b => b.Source)
                .HasForeignKey(b => b.SourceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // BusinessCalendar configuration
        modelBuilder.Entity<BusinessCalendarEntity>(entity =>
        {
            entity.ToTable("BusinessCalendar", "data");
            // Composite primary key: SourceId + EffectiveDate
            entity.HasKey(e => new { e.SourceId, e.EffectiveDate });
            entity.Property(e => e.EffectiveDate).HasColumnType("date"); // DATE only, no time
            entity.Property(e => e.IsBusinessDay).HasDefaultValue(true);
            entity.Property(e => e.IsHoliday).HasDefaultValue(false);
            entity.Property(e => e.IsMonthEnd).HasDefaultValue(false);
            entity.Property(e => e.IsLastBusinessDayMonthEnd).HasDefaultValue(false);

            // Index for filtering business days within a date range
            entity.HasIndex(e => new { e.SourceId, e.IsBusinessDay, e.EffectiveDate })
                .HasDatabaseName("IX_BusinessCalendar_SourceId_IsBusinessDay_EffectiveDate");
        });

        // TrackedSecurities configuration
        modelBuilder.Entity<TrackedSecurityEntity>(entity =>
        {
            entity.ToTable("TrackedSecurities", "data");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).UseIdentityColumn();
            entity.Property(e => e.Source).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Priority).HasDefaultValue(1);
            entity.Property(e => e.Notes).HasMaxLength(500);
            entity.Property(e => e.AddedBy).HasMaxLength(100).HasDefaultValue("system");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("GETUTCDATE()");

            // Each security can only be tracked once (avoid duplicates)
            entity.HasIndex(e => e.SecurityAlias)
                .IsUnique()
                .HasDatabaseName("IX_TrackedSecurities_SecurityAlias");

            // Index for filtering by source (e.g., find all S&P 500 components)
            entity.HasIndex(e => e.Source)
                .HasDatabaseName("IX_TrackedSecurities_Source");

            // Index for priority ordering
            entity.HasIndex(e => e.Priority)
                .HasDatabaseName("IX_TrackedSecurities_Priority");

            // Foreign key to SecurityMaster
            entity.HasOne(e => e.Security)
                .WithOne()
                .HasForeignKey<TrackedSecurityEntity>(e => e.SecurityAlias)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // CoverageSummary configuration (pre-aggregated heatmap data)
        modelBuilder.Entity<CoverageSummaryEntity>(entity =>
        {
            entity.ToTable("CoverageSummary", "data");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).UseIdentityColumn();
            entity.Property(e => e.LastUpdatedAt).HasDefaultValueSql("GETUTCDATE()");

            // Each (Year, ImportanceScore) cell has exactly one row
            entity.HasIndex(e => new { e.Year, e.ImportanceScore })
                .IsUnique()
                .HasDatabaseName("IX_CoverageSummary_Year_Score");
        });

        // ========================================================================
        // Staging Tables (staging schema)
        // These tables buffer incoming data before merge to production.
        // No foreign key constraints for maximum insert speed.
        // ========================================================================

        // PriceStaging configuration
        modelBuilder.Entity<PriceStagingEntity>(entity =>
        {
            entity.ToTable("PriceStaging", "staging");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).UseIdentityColumn();
            entity.Property(e => e.Ticker).HasMaxLength(20).IsRequired();
            entity.Property(e => e.EffectiveDate).HasColumnType("date");
            entity.Property(e => e.Open).HasPrecision(18, 4);
            entity.Property(e => e.High).HasPrecision(18, 4);
            entity.Property(e => e.Low).HasPrecision(18, 4);
            entity.Property(e => e.Close).HasPrecision(18, 4);
            entity.Property(e => e.AdjustedClose).HasPrecision(18, 4);
            entity.Property(e => e.Status).HasMaxLength(20).HasDefaultValue("pending");
            entity.Property(e => e.ErrorMessage).HasMaxLength(500);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("GETUTCDATE()");

            // Index for finding pending records to process
            entity.HasIndex(e => new { e.Status, e.CreatedAt })
                .HasDatabaseName("IX_PriceStaging_Status_CreatedAt");

            // Index for batch processing
            entity.HasIndex(e => e.BatchId)
                .HasDatabaseName("IX_PriceStaging_BatchId");

            // Index for ticker lookups during merge
            entity.HasIndex(e => new { e.Ticker, e.EffectiveDate })
                .HasDatabaseName("IX_PriceStaging_Ticker_EffectiveDate");
        });
    }
}
