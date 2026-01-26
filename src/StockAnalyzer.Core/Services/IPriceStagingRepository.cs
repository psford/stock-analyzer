using StockAnalyzer.Core.Data.Entities;

namespace StockAnalyzer.Core.Services;

/// <summary>
/// DTO for inserting price data into staging.
/// </summary>
public class PriceStagingDto
{
    public string Ticker { get; set; } = string.Empty;
    public DateTime EffectiveDate { get; set; }
    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Close { get; set; }
    public decimal? AdjustedClose { get; set; }
    public long? Volume { get; set; }
}

/// <summary>
/// Result from staging merge operation.
/// </summary>
public class StagingMergeResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public int RecordsProcessed { get; set; }
    public int RecordsInserted { get; set; }
    public int RecordsSkipped { get; set; }
    public int RecordsErrored { get; set; }
    public int SecurityMasterCreated { get; set; }
}

/// <summary>
/// Repository for staging table operations.
/// Provides fast bulk insert and merge-to-production functionality.
/// </summary>
public interface IPriceStagingRepository
{
    /// <summary>
    /// Bulk insert records into staging table.
    /// Optimized for speed - no FK constraints, minimal validation.
    /// </summary>
    /// <param name="records">Records to insert</param>
    /// <param name="batchId">Batch identifier for tracking</param>
    /// <returns>Number of records inserted</returns>
    Task<int> BulkInsertAsync(IEnumerable<PriceStagingDto> records, Guid batchId);

    /// <summary>
    /// Get count of pending records by status.
    /// </summary>
    Task<Dictionary<string, int>> GetStatusCountsAsync();

    /// <summary>
    /// Merge pending staging records to production Price table.
    /// - Looks up SecurityAlias for each ticker
    /// - Creates new securities if needed
    /// - Skips duplicates (ticker+date already in production)
    /// - Updates staging records with processed status
    /// </summary>
    /// <param name="batchSize">Max records to process in one call</param>
    /// <param name="createMissingSecurities">If true, create SecurityMaster entries for unknown tickers</param>
    /// <returns>Merge result with counts</returns>
    Task<StagingMergeResult> MergeToPricesAsync(int batchSize = 10000, bool createMissingSecurities = false);

    /// <summary>
    /// Delete processed records older than specified age.
    /// </summary>
    /// <param name="olderThan">Delete records processed before this time</param>
    /// <returns>Number of records deleted</returns>
    Task<int> CleanupProcessedAsync(DateTime olderThan);

    /// <summary>
    /// Get batch summary information.
    /// </summary>
    Task<List<BatchSummary>> GetBatchSummariesAsync(int limit = 20);
}

/// <summary>
/// Summary of a staging batch.
/// </summary>
public class BatchSummary
{
    public Guid BatchId { get; set; }
    public int TotalRecords { get; set; }
    public int PendingRecords { get; set; }
    public int ProcessedRecords { get; set; }
    public int ErrorRecords { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
}
