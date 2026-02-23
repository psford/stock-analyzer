namespace StockAnalyzer.Core.Data.Entities;

/// <summary>
/// EF Core entity for the security identifier history table.
/// Implements Slowly Changing Dimension (SCD) Type 2 pattern for tracking identifier changes over time.
/// When a SecurityIdentifier value changes, the old record is snapshot here with an effective date range.
/// Stored in the 'data' schema to separate domain data from operational tables.
/// </summary>
public class SecurityIdentifierHistEntity
{
    /// <summary>
    /// Auto-incrementing primary key for this history record.
    /// Each snapshot gets a unique identifier.
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// Foreign key reference to SecurityMaster.
    /// Identifies which security this historical identifier belonged to.
    /// </summary>
    public int SecurityAlias { get; set; }

    /// <summary>
    /// Type of identifier that changed (e.g., "CUSIP", "ISIN", "SEDOL").
    /// </summary>
    public string IdentifierType { get; set; } = string.Empty;

    /// <summary>
    /// The identifier value during the effective period.
    /// </summary>
    public string IdentifierValue { get; set; } = string.Empty;

    /// <summary>
    /// The date when this identifier became active (inclusive).
    /// Stored as DATE type (no time component).
    /// When an identifier is first created, EffectiveFrom is the creation/update date.
    /// </summary>
    public DateTime EffectiveFrom { get; set; }

    /// <summary>
    /// The date when this identifier was superseded (inclusive).
    /// Stored as DATE type (no time component).
    /// Set to the day before a new identifier value took effect.
    /// </summary>
    public DateTime EffectiveTo { get; set; }

    /// <summary>
    /// Foreign key reference to Sources table.
    /// Identifies who provided this historical identifier.
    /// </summary>
    public int SourceId { get; set; }

    /// <summary>
    /// Navigation property: reference to SecurityMaster.
    /// </summary>
    public SecurityMasterEntity Security { get; set; } = null!;
}
