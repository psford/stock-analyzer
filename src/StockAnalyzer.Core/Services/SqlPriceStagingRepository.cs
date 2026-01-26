using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StockAnalyzer.Core.Data;
using StockAnalyzer.Core.Data.Entities;
using StockAnalyzer.Core.Helpers;

namespace StockAnalyzer.Core.Services;

/// <summary>
/// SQL Server implementation of the price staging repository.
/// Uses raw SQL for bulk operations to maximize throughput.
/// </summary>
public class SqlPriceStagingRepository : IPriceStagingRepository
{
    private readonly StockAnalyzerDbContext _context;
    private readonly ILogger<SqlPriceStagingRepository> _logger;

    public SqlPriceStagingRepository(
        StockAnalyzerDbContext context,
        ILogger<SqlPriceStagingRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<int> BulkInsertAsync(IEnumerable<PriceStagingDto> records, Guid batchId)
    {
        var recordList = records.ToList();
        if (recordList.Count == 0) return 0;

        _logger.LogInformation("Inserting {Count} records into staging with batch {BatchId}",
            recordList.Count, batchId);

        // Convert to entities
        var entities = recordList.Select(r => new PriceStagingEntity
        {
            BatchId = batchId,
            Ticker = r.Ticker.ToUpperInvariant(),
            EffectiveDate = r.EffectiveDate.Date,
            Open = r.Open,
            High = r.High,
            Low = r.Low,
            Close = r.Close,
            AdjustedClose = r.AdjustedClose,
            Volume = r.Volume,
            Status = "pending",
            CreatedAt = DateTime.UtcNow
        }).ToList();

        // Use EF Core bulk insert
        await _context.PriceStaging.AddRangeAsync(entities);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Inserted {Count} records into staging", entities.Count);
        return entities.Count;
    }

    public async Task<Dictionary<string, int>> GetStatusCountsAsync()
    {
        var counts = await _context.PriceStaging
            .GroupBy(p => p.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync();

        return counts.ToDictionary(x => x.Status, x => x.Count);
    }

    public async Task<StagingMergeResult> MergeToPricesAsync(int batchSize = 10000, bool createMissingSecurities = false)
    {
        var result = new StagingMergeResult();

        try
        {
            // Get pending records
            var pendingRecords = await _context.PriceStaging
                .Where(p => p.Status == "pending")
                .OrderBy(p => p.CreatedAt)
                .Take(batchSize)
                .ToListAsync();

            if (pendingRecords.Count == 0)
            {
                result.Success = true;
                return result;
            }

            _logger.LogInformation("Processing {Count} pending staging records", pendingRecords.Count);
            result.RecordsProcessed = pendingRecords.Count;

            // Get unique tickers
            var tickers = pendingRecords.Select(r => r.Ticker).Distinct().ToList();

            // Look up security aliases
            var securities = await _context.SecurityMaster
                .Where(s => tickers.Contains(s.TickerSymbol))
                .ToDictionaryAsync(s => s.TickerSymbol, s => s.SecurityAlias);

            // Find tickers without securities
            var missingTickers = tickers.Where(t => !securities.ContainsKey(t)).ToList();

            if (missingTickers.Count > 0)
            {
                if (createMissingSecurities)
                {
                    _logger.LogInformation("Creating {Count} new securities for unknown tickers", missingTickers.Count);

                    foreach (var ticker in missingTickers)
                    {
                        var newSecurity = new SecurityMasterEntity
                        {
                            TickerSymbol = ticker,
                            IssueName = ticker,
                            Exchange = "US",
                            IsActive = true,
                            IsTracked = false,
                            CreatedAt = DateTime.UtcNow,
                            UpdatedAt = DateTime.UtcNow
                        };
                        _context.SecurityMaster.Add(newSecurity);
                    }
                    await _context.SaveChangesAsync();
                    result.SecurityMasterCreated = missingTickers.Count;

                    // Refresh securities dictionary
                    securities = await _context.SecurityMaster
                        .Where(s => tickers.Contains(s.TickerSymbol))
                        .ToDictionaryAsync(s => s.TickerSymbol, s => s.SecurityAlias);
                }
                else
                {
                    _logger.LogWarning("{Count} tickers have no SecurityMaster entry and will be skipped",
                        missingTickers.Count);
                }
            }

            // Get existing prices for deduplication
            var securityAliases = securities.Values.ToList();
            var dates = pendingRecords.Select(r => r.EffectiveDate).Distinct().ToList();

            var existingPrices = await _context.Prices
                .Where(p => securityAliases.Contains(p.SecurityAlias) && dates.Contains(p.EffectiveDate))
                .Select(p => new { p.SecurityAlias, p.EffectiveDate })
                .ToListAsync();

            var existingPriceSet = new HashSet<(int SecurityAlias, DateTime Date)>(
                existingPrices.Select(p => (p.SecurityAlias, p.EffectiveDate)));

            // Process each staging record
            var pricesToInsert = new List<PriceEntity>();
            var processedAt = DateTime.UtcNow;

            foreach (var staging in pendingRecords)
            {
                if (!securities.TryGetValue(staging.Ticker, out var securityAlias))
                {
                    staging.Status = "skipped";
                    staging.ErrorMessage = "No SecurityMaster entry";
                    staging.ProcessedAt = processedAt;
                    result.RecordsSkipped++;
                    continue;
                }

                if (existingPriceSet.Contains((securityAlias, staging.EffectiveDate)))
                {
                    staging.Status = "skipped";
                    staging.ErrorMessage = "Duplicate";
                    staging.ProcessedAt = processedAt;
                    result.RecordsSkipped++;
                    continue;
                }

                // Add to insert list
                pricesToInsert.Add(new PriceEntity
                {
                    SecurityAlias = securityAlias,
                    EffectiveDate = staging.EffectiveDate,
                    Open = staging.Open,
                    High = staging.High,
                    Low = staging.Low,
                    Close = staging.Close,
                    AdjustedClose = staging.AdjustedClose,
                    Volume = staging.Volume,
                    CreatedAt = processedAt
                });

                staging.Status = "processed";
                staging.ProcessedAt = processedAt;

                // Track for deduplication within batch
                existingPriceSet.Add((securityAlias, staging.EffectiveDate));
            }

            // Bulk insert prices
            if (pricesToInsert.Count > 0)
            {
                await _context.Prices.AddRangeAsync(pricesToInsert);
                result.RecordsInserted = pricesToInsert.Count;
            }

            // Save all changes
            await _context.SaveChangesAsync();

            result.Success = true;
            _logger.LogInformation(
                "Merge complete: {Processed} processed, {Inserted} inserted, {Skipped} skipped, {Errors} errors",
                result.RecordsProcessed, result.RecordsInserted, result.RecordsSkipped, result.RecordsErrored);
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Error = ex.Message;
            _logger.LogError(ex, "Error merging staging records to prices");
        }

        return result;
    }

    public async Task<int> CleanupProcessedAsync(DateTime olderThan)
    {
        var deleted = await _context.PriceStaging
            .Where(p => (p.Status == "processed" || p.Status == "skipped") && p.ProcessedAt < olderThan)
            .ExecuteDeleteAsync();

        _logger.LogInformation("Cleaned up {Count} processed staging records older than {Date}",
            deleted, olderThan);

        return deleted;
    }

    public async Task<List<BatchSummary>> GetBatchSummariesAsync(int limit = 20)
    {
        var summaries = await _context.PriceStaging
            .GroupBy(p => p.BatchId)
            .Select(g => new BatchSummary
            {
                BatchId = g.Key,
                TotalRecords = g.Count(),
                PendingRecords = g.Count(p => p.Status == "pending"),
                ProcessedRecords = g.Count(p => p.Status == "processed"),
                ErrorRecords = g.Count(p => p.Status == "error"),
                CreatedAt = g.Min(p => p.CreatedAt),
                ProcessedAt = g.Max(p => p.ProcessedAt)
            })
            .OrderByDescending(b => b.CreatedAt)
            .Take(limit)
            .ToListAsync();

        return summaries;
    }
}
