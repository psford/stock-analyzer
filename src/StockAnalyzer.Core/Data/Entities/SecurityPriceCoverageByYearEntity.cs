namespace StockAnalyzer.Core.Data.Entities;

/// <summary>
/// Per-security-per-year price coverage metadata. One row per security per year.
/// Supports the Year dimension needed by CoverageSummary aggregation
/// to replace expensive full-table scans on the 43M+ row Prices table.
/// Stored in the 'data' schema.
/// </summary>
public class SecurityPriceCoverageByYearEntity
{
    /// <summary>Foreign key to SecurityMaster. Part of composite primary key.</summary>
    public int SecurityAlias { get; set; }

    /// <summary>Calendar year. Part of composite primary key.</summary>
    public int Year { get; set; }

    /// <summary>Number of price records for this security in this year.</summary>
    public int PriceCount { get; set; }

    /// <summary>UTC timestamp of last coverage update.</summary>
    public DateTime LastUpdatedAt { get; set; }

    /// <summary>Navigation property to SecurityMaster.</summary>
    public SecurityMasterEntity Security { get; set; } = null!;
}
