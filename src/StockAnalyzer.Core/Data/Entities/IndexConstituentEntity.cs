namespace StockAnalyzer.Core.Data.Entities;

/// <summary>
/// EF Core entity for the index constituent table.
/// Stores snapshot data about the securities that compose an index on a given effective date.
/// Records are immutable once inserted (no updates), enabling historical tracking of index composition changes.
/// Stored in the 'data' schema to separate domain data from operational tables.
/// </summary>
public class IndexConstituentEntity
{
    /// <summary>
    /// Auto-incrementing surrogate key for this constituent record.
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// Foreign key reference to IndexDefinition.
    /// Identifies which index this security is a constituent of.
    /// </summary>
    public int IndexId { get; set; }

    /// <summary>
    /// Foreign key reference to SecurityMaster.
    /// Identifies the security that is a constituent.
    /// </summary>
    public int SecurityAlias { get; set; }

    /// <summary>
    /// The effective date of this snapshot (e.g., month-end date when index was rebalanced).
    /// Stored as DATE type (no time component).
    /// </summary>
    public DateTime EffectiveDate { get; set; }

    /// <summary>
    /// Weight of this holding in the index as a decimal fraction (0.065 = 6.5%).
    /// Nullable for edge cases where weight data is unavailable.
    /// Precision 18,8 supports values up to ~999,999,999.99999999
    /// </summary>
    public decimal? Weight { get; set; }

    /// <summary>
    /// Market value of this holding in USD.
    /// Nullable for holdings where market value data is unavailable.
    /// Precision 18,2 supports values up to ~999,999,999,999,999.99
    /// </summary>
    public decimal? MarketValue { get; set; }

    /// <summary>
    /// Number of shares held in the index portfolio.
    /// Nullable for cases where share count is not available.
    /// Precision 18,4 supports fractional shares and large quantities.
    /// </summary>
    public decimal? Shares { get; set; }

    /// <summary>
    /// GICS sector classification of this security (e.g., "Technology", "Healthcare", "Financials").
    /// Allows analysis of sector composition within the index.
    /// </summary>
    public string? Sector { get; set; }

    /// <summary>
    /// Domicile country of the security (e.g., "USA", "Canada", "Germany").
    /// Enables geographic composition analysis.
    /// </summary>
    public string? Location { get; set; }

    /// <summary>
    /// Currency in which the security is traded (e.g., "USD", "EUR", "JPY").
    /// </summary>
    public string? Currency { get; set; }

    /// <summary>
    /// Foreign key reference to Sources table.
    /// Identifies the data source that provided this constituent record (e.g., iShares).
    /// </summary>
    public int SourceId { get; set; }

    /// <summary>
    /// Ticker symbol as reported by the source (e.g., "AAPL" from iShares download).
    /// May differ from SecurityMaster.TickerSymbol if normalized elsewhere.
    /// </summary>
    public string? SourceTicker { get; set; }

    /// <summary>
    /// Navigation property: reference to IndexDefinition.
    /// </summary>
    public IndexDefinitionEntity IndexDefinition { get; set; } = null!;

    /// <summary>
    /// Navigation property: reference to SecurityMaster.
    /// </summary>
    public SecurityMasterEntity Security { get; set; } = null!;

    /// <summary>
    /// Navigation property: reference to Source.
    /// </summary>
    public SourceEntity Source { get; set; } = null!;
}
