namespace StockAnalyzer.Core.Data.Entities;

/// <summary>
/// EF Core entity for the security master table.
/// Central reference for all securities with auto-incrementing alias for efficient joins.
/// Stored in the 'data' schema to separate domain data from operational tables.
/// </summary>
public class SecurityMasterEntity
{
    /// <summary>
    /// Auto-incrementing primary key used as foreign key in related tables (e.g., Prices).
    /// Using an integer alias instead of ticker allows efficient joins and supports
    /// ticker changes/symbol history without breaking relationships.
    /// </summary>
    public int SecurityAlias { get; set; }

    /// <summary>
    /// Placeholder for future primary asset identifier integration.
    /// Could store CUSIP, ISIN, SEDOL, or other industry-standard identifiers.
    /// </summary>
    public string? PrimaryAssetId { get; set; }

    /// <summary>
    /// Full name of the security (e.g., "Apple Inc.", "Microsoft Corporation").
    /// </summary>
    public string IssueName { get; set; } = string.Empty;

    /// <summary>
    /// Ticker symbol (e.g., "AAPL", "MSFT").
    /// Note: May move to a separate cross-reference table later to support
    /// ticker changes and symbol history.
    /// </summary>
    public string TickerSymbol { get; set; } = string.Empty;

    /// <summary>
    /// Primary exchange where the security is traded (e.g., "NASDAQ", "NYSE").
    /// </summary>
    public string? Exchange { get; set; }

    /// <summary>
    /// Security type classification (e.g., "Common Stock", "ETF", "ADR", "Preferred").
    /// </summary>
    public string? SecurityType { get; set; }

    /// <summary>
    /// Country where the security is domiciled (e.g., "USA", "Canada").
    /// </summary>
    public string? Country { get; set; }

    /// <summary>
    /// Currency in which the security is traded (e.g., "USD", "CAD", "EUR").
    /// </summary>
    public string? Currency { get; set; }

    /// <summary>
    /// International Securities Identification Number (ISIN) - a globally recognized identifier.
    /// </summary>
    public string? Isin { get; set; }

    /// <summary>
    /// Whether this security is actively traded.
    /// Used for soft deletes - delisted securities are marked inactive rather than deleted.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Whether this security is in our tracked universe for gap-filling.
    /// When true, the Crawler will ensure complete price history.
    /// Synced with TrackedSecurities table for detailed tracking info.
    /// </summary>
    public bool IsTracked { get; set; } = false;

    /// <summary>
    /// Whether EODHD has no data available for this security.
    /// Set to true when the crawler detects EODHD returns no data for this ticker.
    /// When true, the crawler will skip this security during gap-filling.
    /// Typically affects OTC/Pink Sheet securities that EODHD doesn't cover.
    /// </summary>
    public bool IsEodhdUnavailable { get; set; } = false;

    /// <summary>
    /// Whether all available EODHD data has been loaded for this security.
    /// Set to true when a full-history load returns 0 new records, meaning any
    /// remaining business-calendar gaps are unfillable via EODHD.
    /// When true, the gap query skips this security to avoid wasting API calls.
    /// Can be reset to false to re-check (e.g., if EODHD adds more historical data).
    /// </summary>
    public bool IsEodhdComplete { get; set; } = false;

    /// <summary>
    /// Calculated importance score (1-10, 10=most important).
    /// Used to prioritize gap-filling for untracked securities.
    /// Calculated based on security type, exchange, and name patterns.
    /// Score algorithm: Base 5, adjusted by:
    ///   - Security Type: Common Stock +2, ETF +1, Preferred/Warrant -2, OTC indicators -3
    ///   - Exchange: NYSE/NASDAQ +2, ARCA/BATS +1, OTC/PINK -2, Unknown -1
    ///   - Ticker Length: 1-3 chars +1, 5+ chars -1
    ///   - Name Patterns: Inc/Corp/Ltd +1, Warrant/Right/Unit -2, Liquidating/Bankrupt -3
    /// </summary>
    public int ImportanceScore { get; set; } = 5;

    /// <summary>
    /// Timestamp when this record was created (UTC).
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Timestamp when this record was last updated (UTC).
    /// </summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// Navigation property for related price records.
    /// </summary>
    public ICollection<PriceEntity> Prices { get; set; } = new List<PriceEntity>();
}
