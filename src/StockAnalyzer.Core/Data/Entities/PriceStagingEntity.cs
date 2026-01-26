namespace StockAnalyzer.Core.Data.Entities;

/// <summary>
/// Staging table entity for buffering incoming price data before merge to production.
/// Used when EODHD bulk API returns data faster than we can insert it.
///
/// Flow:
/// 1. Bulk API returns thousands of records
/// 2. Records are quickly inserted into PriceStaging (no FK constraints)
/// 3. Background job merges from Staging to Price table
/// 4. Processed records are deleted from Staging
///
/// Stored in the 'staging' schema to separate from production data.
/// </summary>
public class PriceStagingEntity
{
    /// <summary>
    /// Auto-incrementing primary key for staging table.
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// Batch identifier for grouping records from same API call.
    /// Used for tracking and cleanup.
    /// </summary>
    public Guid BatchId { get; set; }

    /// <summary>
    /// Ticker symbol as received from EODHD (e.g., "AAPL").
    /// We store ticker here instead of SecurityAlias because we may not
    /// have the security in our master yet.
    /// </summary>
    public string Ticker { get; set; } = string.Empty;

    /// <summary>
    /// Trading date for this price record.
    /// </summary>
    public DateTime EffectiveDate { get; set; }

    /// <summary>
    /// Opening price for the trading day.
    /// </summary>
    public decimal Open { get; set; }

    /// <summary>
    /// Highest price reached during the trading day.
    /// </summary>
    public decimal High { get; set; }

    /// <summary>
    /// Lowest price reached during the trading day.
    /// </summary>
    public decimal Low { get; set; }

    /// <summary>
    /// Closing price for the trading day.
    /// </summary>
    public decimal Close { get; set; }

    /// <summary>
    /// Split and dividend adjusted closing price.
    /// </summary>
    public decimal? AdjustedClose { get; set; }

    /// <summary>
    /// Trading volume for the day.
    /// </summary>
    public long? Volume { get; set; }

    /// <summary>
    /// Processing status: 'pending', 'processed', 'error', 'skipped'
    /// </summary>
    public string Status { get; set; } = "pending";

    /// <summary>
    /// Error message if processing failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// When this record was inserted into staging.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// When this record was processed (moved to production or marked as error).
    /// </summary>
    public DateTime? ProcessedAt { get; set; }
}
