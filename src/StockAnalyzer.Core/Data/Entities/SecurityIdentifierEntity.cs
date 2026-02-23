namespace StockAnalyzer.Core.Data.Entities;

/// <summary>
/// EF Core entity for the security identifier table.
/// Stores current (active) alternative identifier values for securities (CUSIP, ISIN, SEDOL).
/// When an identifier value changes, the old value is snapshot to SecurityIdentifierHist (SCD Type 2)
/// and the new value replaces the current record.
/// Stored in the 'data' schema to separate domain data from operational tables.
/// </summary>
public class SecurityIdentifierEntity
{
    /// <summary>
    /// Foreign key reference to SecurityMaster (composite PK part 1).
    /// Identifies the security this identifier belongs to.
    /// </summary>
    public int SecurityAlias { get; set; }

    /// <summary>
    /// Type of identifier (composite PK part 2).
    /// Examples: "CUSIP", "ISIN", "SEDOL"
    /// Enables multiple identifiers per security without duplication.
    /// </summary>
    public string IdentifierType { get; set; } = string.Empty;

    /// <summary>
    /// The actual identifier value (e.g., "037833100" for CUSIP).
    /// </summary>
    public string IdentifierValue { get; set; } = string.Empty;

    /// <summary>
    /// Foreign key reference to Sources table.
    /// Identifies who provided this identifier (e.g., iShares, FactSet, CUSIP Service Bureau).
    /// </summary>
    public int SourceId { get; set; }

    /// <summary>
    /// User or system that last updated this identifier (e.g., "ishares-ingest", "manual-upload").
    /// Enables audit trail for identifier changes.
    /// </summary>
    public string? UpdatedBy { get; set; }

    /// <summary>
    /// UTC timestamp when this identifier was last updated.
    /// Used to determine EffectiveFrom date when snapshotting to history.
    /// </summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// Navigation property: reference to SecurityMaster.
    /// </summary>
    public SecurityMasterEntity Security { get; set; } = null!;
}
