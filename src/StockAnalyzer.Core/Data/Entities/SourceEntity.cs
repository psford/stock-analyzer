namespace StockAnalyzer.Core.Data.Entities;

/// <summary>
/// EF Core entity for the Sources table.
/// Dictionary-style table for business calendar sources (e.g., US, UK, etc.).
/// Stored in the 'data' schema to separate domain data from operational tables.
/// </summary>
public class SourceEntity
{
    /// <summary>
    /// Primary key identifier for the source.
    /// </summary>
    public int SourceId { get; set; }

    /// <summary>
    /// Short identifier for the source (e.g., "US", "UK", "JP").
    /// </summary>
    public string SourceShortName { get; set; } = string.Empty;

    /// <summary>
    /// Full descriptive name for the source (e.g., "US Business Day Calendar").
    /// </summary>
    public string SourceLongName { get; set; } = string.Empty;

    /// <summary>
    /// Navigation property for related business calendar entries.
    /// </summary>
    public ICollection<BusinessCalendarEntity> BusinessCalendarEntries { get; set; } = new List<BusinessCalendarEntity>();
}
