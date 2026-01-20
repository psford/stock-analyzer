namespace StockAnalyzer.Core.Models;

/// <summary>
/// Represents a user's holding in a single ticker.
/// Supports both share count and dollar value input modes.
/// </summary>
public record TickerHolding
{
    public required string Ticker { get; init; }

    /// <summary>
    /// Number of shares held. Null if using dollar value mode.
    /// </summary>
    public decimal? Shares { get; init; }

    /// <summary>
    /// Dollar value of holding. Null if using shares mode.
    /// </summary>
    public decimal? DollarValue { get; init; }
}

/// <summary>
/// Represents a user's stock watchlist containing a collection of ticker symbols.
/// Designed for single-user now, with UserId field for future multi-user support.
/// </summary>
public record Watchlist
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required List<string> Tickers { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }

    /// <summary>
    /// User ID for multi-user support. Null for single-user mode.
    /// </summary>
    public string? UserId { get; init; }

    /// <summary>
    /// Holdings information for each ticker. Empty list means equal weighting.
    /// </summary>
    public List<TickerHolding> Holdings { get; init; } = new();

    /// <summary>
    /// Weighting mode for combined view: "equal", "shares", or "dollars"
    /// </summary>
    public string WeightingMode { get; init; } = "equal";
}

/// <summary>
/// DTO for creating a new watchlist.
/// </summary>
public record CreateWatchlistRequest
{
    public required string Name { get; init; }
}

/// <summary>
/// DTO for updating a watchlist.
/// </summary>
public record UpdateWatchlistRequest
{
    public required string Name { get; init; }
}

/// <summary>
/// DTO for adding a ticker to a watchlist.
/// </summary>
public record AddTickerRequest
{
    public required string Ticker { get; init; }
}

/// <summary>
/// Container for watchlist storage (JSON file format).
/// </summary>
public record WatchlistStorage
{
    public List<Watchlist> Watchlists { get; init; } = new();
}

/// <summary>
/// DTO for updating holdings on a watchlist.
/// </summary>
public record UpdateHoldingsRequest
{
    /// <summary>
    /// Weighting mode: "equal", "shares", or "dollars"
    /// </summary>
    public required string WeightingMode { get; init; }

    /// <summary>
    /// Holdings for each ticker. Can be empty for equal weighting.
    /// </summary>
    public required List<TickerHolding> Holdings { get; init; }
}

/// <summary>
/// A single data point in the combined portfolio time series.
/// </summary>
public record PortfolioDataPoint
{
    public required DateTime Date { get; init; }
    public required decimal PortfolioValue { get; init; }
    public required decimal PercentChange { get; init; }
}

/// <summary>
/// Result of combined portfolio analysis for a watchlist.
/// </summary>
public record CombinedPortfolioResult
{
    public required string WatchlistId { get; init; }
    public required string WatchlistName { get; init; }
    public required string Period { get; init; }
    public required string WeightingMode { get; init; }
    public required List<PortfolioDataPoint> Data { get; init; }
    public required decimal TotalReturn { get; init; }
    public required decimal DayChange { get; init; }
    public required decimal DayChangePercent { get; init; }

    /// <summary>
    /// Current weight of each ticker in the portfolio (ticker -> weight as decimal 0-1)
    /// </summary>
    public required Dictionary<string, decimal> TickerWeights { get; init; }

    /// <summary>
    /// Optional benchmark comparison data
    /// </summary>
    public List<PortfolioDataPoint>? BenchmarkData { get; init; }

    /// <summary>
    /// Benchmark ticker symbol if comparison was requested
    /// </summary>
    public string? BenchmarkSymbol { get; init; }

    /// <summary>
    /// Significant moves (days with ±5% change) in the portfolio
    /// </summary>
    public List<PortfolioSignificantMove>? SignificantMoves { get; init; }
}

/// <summary>
/// Represents a significant move in the combined portfolio
/// </summary>
public record PortfolioSignificantMove
{
    public required DateTime Date { get; init; }
    public required decimal PercentChange { get; init; }
    public bool IsPositive => PercentChange > 0;
}
