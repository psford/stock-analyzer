namespace EodhdLoader.Models;

/// <summary>
/// Represents progress during ETF constituent ingestion.
/// </summary>
public record IngestProgress(
    string EtfTicker,
    int CurrentEtf,
    int TotalEtfs,
    int HoldingsProcessed,
    int TotalHoldings,
    IngestStats Stats
);

/// <summary>
/// Statistics for a single ETF ingestion operation.
/// </summary>
public record IngestStats(
    int Parsed,
    int Matched,
    int Created,
    int Inserted,
    int SkippedExisting,
    int Failed,
    int IdentifiersSet
);
