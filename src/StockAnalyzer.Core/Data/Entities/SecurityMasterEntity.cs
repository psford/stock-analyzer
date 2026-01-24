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
