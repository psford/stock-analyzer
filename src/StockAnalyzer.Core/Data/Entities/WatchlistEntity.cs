namespace StockAnalyzer.Core.Data.Entities;

/// <summary>
/// EF Core entity for watchlist persistence in SQL database.
/// Maps to the Watchlists table.
/// </summary>
public class WatchlistEntity
{
    public string Id { get; set; } = string.Empty;
    public string? UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string WeightingMode { get; set; } = "equal";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Navigation properties
    public ICollection<WatchlistTickerEntity> Tickers { get; set; } = new List<WatchlistTickerEntity>();
    public ICollection<TickerHoldingEntity> Holdings { get; set; } = new List<TickerHoldingEntity>();
}

/// <summary>
/// EF Core entity for ticker symbols in a watchlist.
/// Maps to the WatchlistTickers table.
/// </summary>
public class WatchlistTickerEntity
{
    public int Id { get; set; }
    public string WatchlistId { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;

    // Navigation property
    public WatchlistEntity? Watchlist { get; set; }
}

/// <summary>
/// EF Core entity for ticker holdings in a watchlist.
/// Maps to the TickerHoldings table.
/// </summary>
public class TickerHoldingEntity
{
    public int Id { get; set; }
    public string WatchlistId { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public decimal? Shares { get; set; }
    public decimal? DollarValue { get; set; }

    // Navigation property
    public WatchlistEntity? Watchlist { get; set; }
}
