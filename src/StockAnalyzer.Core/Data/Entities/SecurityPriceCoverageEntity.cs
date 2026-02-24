namespace StockAnalyzer.Core.Data.Entities;

/// <summary>
/// Per-security price coverage metadata. One row per security.
/// Tracks actual price count, date range, expected count, and gap days
/// to replace expensive full-table scans on the 43M+ row Prices table.
/// Stored in the 'data' schema.
/// </summary>
public class SecurityPriceCoverageEntity
{
    /// <summary>Foreign key to SecurityMaster. Primary key (not auto-generated).</summary>
    public int SecurityAlias { get; set; }

    /// <summary>Number of price records loaded for this security.</summary>
    public int PriceCount { get; set; }

    /// <summary>Earliest price date loaded. Null if no prices exist.</summary>
    public DateTime? FirstDate { get; set; }

    /// <summary>Latest price date loaded. Null if no prices exist.</summary>
    public DateTime? LastDate { get; set; }

    /// <summary>Expected number of business days between FirstDate and LastDate, computed from BusinessCalendar.</summary>
    public int? ExpectedCount { get; set; }

    /// <summary>
    /// Computed persisted column: ISNULL(ExpectedCount, 0) - PriceCount.
    /// Positive value indicates missing price data. Zero means fully covered.
    /// </summary>
    public int GapDays { get; private set; }

    /// <summary>UTC timestamp of last coverage update.</summary>
    public DateTime LastUpdatedAt { get; set; }

    /// <summary>Navigation property to SecurityMaster.</summary>
    public SecurityMasterEntity Security { get; set; } = null!;
}
