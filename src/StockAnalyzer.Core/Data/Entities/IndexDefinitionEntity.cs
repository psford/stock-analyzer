namespace StockAnalyzer.Core.Data.Entities;

/// <summary>
/// EF Core entity for the index definition table.
/// Stores metadata about benchmark indices tracked by the system (S&P 500, Russell 2000, etc.).
/// Stored in the 'data' schema to separate domain data from operational tables.
/// </summary>
public class IndexDefinitionEntity
{
    /// <summary>
    /// Auto-incrementing primary key identifier for the index.
    /// </summary>
    public int IndexId { get; set; }

    /// <summary>
    /// Short code identifier for the index (e.g., "SP500", "R1000", "MSCI_EAFE").
    /// Unique within the system.
    /// </summary>
    public string IndexCode { get; set; } = string.Empty;

    /// <summary>
    /// Full descriptive name of the index (e.g., "S&P 500", "Russell 1000").
    /// </summary>
    public string IndexName { get; set; } = string.Empty;

    /// <summary>
    /// Family or provider of the index (e.g., "S&P", "Russell", "MSCI").
    /// Helps categorize indices by their source/maintainer.
    /// </summary>
    public string? IndexFamily { get; set; }

    /// <summary>
    /// How the index weights its constituents (e.g., "FloatAdjustedMarketCap", "EqualWeight", "Dividend").
    /// Reflects the methodology used to calculate the index.
    /// </summary>
    public string? WeightingMethod { get; set; }

    /// <summary>
    /// Geographic region covered by the index (e.g., "US", "Emerging Markets", "Global").
    /// </summary>
    public string? Region { get; set; }

    /// <summary>
    /// Ticker symbol of a proxy ETF that tracks this index (e.g., "IVV" for S&P 500).
    /// Used by iShares constituent ingest to download holdings data.
    /// </summary>
    public string? ProxyEtfTicker { get; set; }

    /// <summary>
    /// Navigation property for related constituent records.
    /// Represents the 1:N relationship with IndexConstituent.
    /// </summary>
    public ICollection<IndexConstituentEntity> Constituents { get; set; } = new List<IndexConstituentEntity>();
}
