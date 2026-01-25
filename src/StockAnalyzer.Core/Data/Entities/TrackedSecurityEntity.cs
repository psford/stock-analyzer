namespace StockAnalyzer.Core.Data.Entities;

/// <summary>
/// Tracks which securities are in our monitored universe and why.
/// Provides detailed tracking information beyond the simple IsTracked flag on SecurityMaster.
/// Synced with SecurityMaster.IsTracked for query efficiency.
/// </summary>
public class TrackedSecurityEntity
{
    /// <summary>
    /// Primary key - auto-incrementing ID.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Foreign key to SecurityMaster.
    /// </summary>
    public int SecurityAlias { get; set; }

    /// <summary>
    /// Why this security is tracked (e.g., "S&P 500", "NASDAQ 100", "User Added", "Portfolio Holding").
    /// </summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>
    /// Optional priority level (1=highest priority, higher numbers = lower priority).
    /// Used to prioritize gap-filling order.
    /// </summary>
    public int Priority { get; set; } = 1;

    /// <summary>
    /// Optional notes about why this security was added.
    /// </summary>
    public string? Notes { get; set; }

    /// <summary>
    /// When this tracking entry was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Who/what added this tracking entry (e.g., "system", "user", "index-sync").
    /// </summary>
    public string AddedBy { get; set; } = "system";

    /// <summary>
    /// Navigation property to SecurityMaster.
    /// </summary>
    public SecurityMasterEntity? Security { get; set; }
}
