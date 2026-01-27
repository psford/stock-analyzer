namespace StockAnalyzer.Core.Data.Entities;

/// <summary>
/// Pre-aggregated coverage summary for the bivariate heatmap.
/// Stores Year x ImportanceScore cell data to avoid expensive full-table scans
/// on the 3.5M+ row Prices table. Refreshed on demand via POST /api/admin/dashboard/refresh-summary.
/// Stored in the 'data' schema alongside other domain tables.
/// </summary>
public class CoverageSummaryEntity
{
    /// <summary>Auto-incrementing primary key.</summary>
    public int Id { get; set; }

    /// <summary>Calendar year (e.g., 2020).</summary>
    public int Year { get; set; }

    /// <summary>Importance score bucket (1-10).</summary>
    public int ImportanceScore { get; set; }

    /// <summary>Count of price records for tracked securities in this cell.</summary>
    public long TrackedRecords { get; set; }

    /// <summary>Count of price records for untracked securities in this cell.</summary>
    public long UntrackedRecords { get; set; }

    /// <summary>Distinct tracked securities with data in this cell.</summary>
    public int TrackedSecurities { get; set; }

    /// <summary>Distinct untracked securities with data in this cell.</summary>
    public int UntrackedSecurities { get; set; }

    /// <summary>Distinct trading dates in this cell.</summary>
    public int TradingDays { get; set; }

    /// <summary>When this summary row was last recomputed (UTC).</summary>
    public DateTime LastUpdatedAt { get; set; }
}
