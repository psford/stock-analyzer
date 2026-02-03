namespace StockAnalyzer.Core.Data.Entities;

/// <summary>
/// Cached company description/bio. One row per security, joined on SecurityAlias.
/// Populated on first stock lookup from Wikipedia or financial data providers,
/// then served from Azure SQL on subsequent requests.
/// </summary>
public class CompanyBioEntity
{
    /// <summary>PK and FK to SecurityMaster (1:1 relationship).</summary>
    public int SecurityAlias { get; set; }

    /// <summary>The company description text.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Where the description came from: "wikipedia", "yahoo", "fmp", "twelvedata".</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>When the description was originally fetched.</summary>
    public DateTime FetchedAt { get; set; }

    /// <summary>When this row was last updated.</summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>Navigation property to SecurityMaster.</summary>
    public SecurityMasterEntity? Security { get; set; }
}
