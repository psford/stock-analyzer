using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StockAnalyzer.Core.Data.Entities;
using StockAnalyzer.Core.Services;

namespace StockAnalyzer.Core.Data;

/// <summary>
/// SQL Server implementation of IPriceRepository.
/// Manages historical price data in the data.Prices table.
/// Optimized for efficient querying of large datasets (~1.26M+ rows).
/// </summary>
public class SqlPriceRepository : IPriceRepository
{
    private readonly StockAnalyzerDbContext _context;
    private readonly ILogger<SqlPriceRepository> _logger;

    public SqlPriceRepository(StockAnalyzerDbContext context, ILogger<SqlPriceRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<List<PriceEntity>> GetPricesAsync(int securityAlias, DateTime startDate, DateTime endDate)
    {
        return await _context.Prices
            .AsNoTracking()
            .Where(p => p.SecurityAlias == securityAlias &&
                        p.EffectiveDate >= startDate.Date &&
                        p.EffectiveDate <= endDate.Date)
            .OrderBy(p => p.EffectiveDate)
            .ToListAsync();
    }

    /// <inheritdoc />
    public async Task<List<PriceEntity>> GetAllPricesAsync(int securityAlias)
    {
        return await _context.Prices
            .AsNoTracking()
            .Where(p => p.SecurityAlias == securityAlias)
            .OrderBy(p => p.EffectiveDate)
            .ToListAsync();
    }

    /// <inheritdoc />
    public async Task<List<PriceEntity>> GetPricesForDateAsync(DateTime date)
    {
        return await _context.Prices
            .AsNoTracking()
            .Where(p => p.EffectiveDate == date.Date)
            .ToListAsync();
    }

    /// <inheritdoc />
    public async Task<PriceEntity?> GetLatestPriceAsync(int securityAlias)
    {
        return await _context.Prices
            .AsNoTracking()
            .Where(p => p.SecurityAlias == securityAlias)
            .OrderByDescending(p => p.EffectiveDate)
            .FirstOrDefaultAsync();
    }

    /// <inheritdoc />
    public async Task<Dictionary<int, PriceEntity>> GetLatestPricesAsync(IEnumerable<int> securityAliases)
    {
        var aliasList = securityAliases.ToList();
        if (aliasList.Count == 0)
            return new Dictionary<int, PriceEntity>();

        // Get the latest price for each security using a grouped query
        var latestPrices = await _context.Prices
            .AsNoTracking()
            .Where(p => aliasList.Contains(p.SecurityAlias))
            .GroupBy(p => p.SecurityAlias)
            .Select(g => g.OrderByDescending(p => p.EffectiveDate).First())
            .ToListAsync();

        return latestPrices.ToDictionary(p => p.SecurityAlias);
    }

    /// <inheritdoc />
    public async Task<PriceEntity> CreateAsync(PriceCreateDto dto)
    {
        var entity = new PriceEntity
        {
            SecurityAlias = dto.SecurityAlias,
            EffectiveDate = dto.EffectiveDate.Date,
            Open = dto.Open,
            High = dto.High,
            Low = dto.Low,
            Close = dto.Close,
            Volatility = dto.Volatility,
            Volume = dto.Volume,
            AdjustedClose = dto.AdjustedClose,
            CreatedAt = DateTime.UtcNow
        };

        _context.Prices.Add(entity);
        await _context.SaveChangesAsync();

        return entity;
    }

    /// <inheritdoc />
    public async Task<int> BulkInsertAsync(IEnumerable<PriceCreateDto> prices)
    {
        var priceList = prices.ToList();
        if (priceList.Count == 0) return 0;

        var now = DateTime.UtcNow;
        var count = 0;

        // Get all existing (SecurityAlias, EffectiveDate) pairs to filter out duplicates
        var securityAliases = priceList.Select(p => p.SecurityAlias).Distinct().ToList();
        var dates = priceList.Select(p => p.EffectiveDate.Date).Distinct().ToList();

        var existingKeys = await _context.Prices
            .AsNoTracking()
            .Where(p => securityAliases.Contains(p.SecurityAlias) && dates.Contains(p.EffectiveDate))
            .Select(p => new { p.SecurityAlias, p.EffectiveDate })
            .ToListAsync();

        var existingSet = existingKeys
            .Select(k => (k.SecurityAlias, k.EffectiveDate))
            .ToHashSet();

        // Filter to only new prices
        var newPrices = priceList
            .Where(p => !existingSet.Contains((p.SecurityAlias, p.EffectiveDate.Date)))
            .ToList();

        if (newPrices.Count == 0)
        {
            _logger.LogInformation("Bulk insert: All {Total} prices already exist, nothing to insert", priceList.Count);
            return 0;
        }

        _logger.LogInformation("Bulk insert: {New} new prices out of {Total} total (skipping {Existing} existing)",
            newPrices.Count, priceList.Count, priceList.Count - newPrices.Count);

        // Process in batches of 1000 for efficiency with large datasets
        foreach (var batch in newPrices.Chunk(1000))
        {
            var entities = batch.Select(dto => new PriceEntity
            {
                SecurityAlias = dto.SecurityAlias,
                EffectiveDate = dto.EffectiveDate.Date,
                Open = dto.Open,
                High = dto.High,
                Low = dto.Low,
                Close = dto.Close,
                Volatility = dto.Volatility,
                Volume = dto.Volume,
                AdjustedClose = dto.AdjustedClose,
                CreatedAt = now
            });

            _context.Prices.AddRange(entities);
            await _context.SaveChangesAsync();
            count += batch.Length;

            // Log progress for large inserts
            if (count % 10000 == 0)
            {
                _logger.LogInformation("Bulk insert progress: {Count} prices inserted", count);
            }
        }

        _logger.LogInformation("Bulk insert complete: {Count} prices inserted", count);
        return count;
    }

    /// <inheritdoc />
    public async Task UpsertAsync(PriceCreateDto dto)
    {
        var effectiveDate = dto.EffectiveDate.Date;
        var existing = await _context.Prices
            .FirstOrDefaultAsync(p => p.SecurityAlias == dto.SecurityAlias &&
                                       p.EffectiveDate == effectiveDate);

        if (existing != null)
        {
            // Update existing
            existing.Open = dto.Open;
            existing.High = dto.High;
            existing.Low = dto.Low;
            existing.Close = dto.Close;
            existing.Volatility = dto.Volatility;
            existing.Volume = dto.Volume;
            existing.AdjustedClose = dto.AdjustedClose;
        }
        else
        {
            // Insert new
            _context.Prices.Add(new PriceEntity
            {
                SecurityAlias = dto.SecurityAlias,
                EffectiveDate = effectiveDate,
                Open = dto.Open,
                High = dto.High,
                Low = dto.Low,
                Close = dto.Close,
                Volatility = dto.Volatility,
                Volume = dto.Volume,
                AdjustedClose = dto.AdjustedClose,
                CreatedAt = DateTime.UtcNow
            });
        }

        await _context.SaveChangesAsync();
    }

    /// <inheritdoc />
    public async Task<int> DeleteOlderThanAsync(int securityAlias, DateTime cutoffDate)
    {
        var count = await _context.Prices
            .Where(p => p.SecurityAlias == securityAlias && p.EffectiveDate < cutoffDate.Date)
            .ExecuteDeleteAsync();

        if (count > 0)
        {
            _logger.LogInformation("Deleted {Count} prices older than {Date} for security {Alias}",
                count, cutoffDate.Date, securityAlias);
        }

        return count;
    }

    /// <inheritdoc />
    public async Task<(DateTime? Earliest, DateTime? Latest)> GetDateRangeAsync(int securityAlias)
    {
        var dates = await _context.Prices
            .AsNoTracking()
            .Where(p => p.SecurityAlias == securityAlias)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Earliest = g.Min(p => (DateTime?)p.EffectiveDate),
                Latest = g.Max(p => (DateTime?)p.EffectiveDate)
            })
            .FirstOrDefaultAsync();

        return (dates?.Earliest, dates?.Latest);
    }

    /// <inheritdoc />
    public async Task<long> GetTotalCountAsync()
    {
        return await _context.Prices.LongCountAsync();
    }

    /// <inheritdoc />
    public async Task<int> GetCountForSecurityAsync(int securityAlias)
    {
        return await _context.Prices.CountAsync(p => p.SecurityAlias == securityAlias);
    }

    /// <inheritdoc />
    public async Task<List<DateTime>> GetDistinctDatesAsync(DateTime startDate, DateTime endDate)
    {
        return await _context.Prices
            .AsNoTracking()
            .Where(p => p.EffectiveDate >= startDate.Date && p.EffectiveDate <= endDate.Date)
            .Select(p => p.EffectiveDate)
            .Distinct()
            .OrderBy(d => d)
            .ToListAsync();
    }

    /// <inheritdoc />
    /// <summary>
    /// Analyzes ALL non-business days (weekends AND holidays) that need price data.
    /// Reports which days need forward-fill from prior business days.
    /// </summary>
    public async Task<HolidayAnalysisResult> AnalyzeHolidaysAsync()
    {
        var result = new HolidayAnalysisResult();

        try
        {
            // Get date range from database
            var dateRange = await _context.Prices
                .AsNoTracking()
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    Min = g.Min(p => p.EffectiveDate),
                    Max = g.Max(p => p.EffectiveDate)
                })
                .FirstOrDefaultAsync();

            if (dateRange == null)
            {
                result.Error = "No price data found in database";
                return result;
            }

            result.DataStartDate = dateRange.Min;
            result.DataEndDate = dateRange.Max;

            // Get all distinct dates with data
            var existingDates = await _context.Prices
                .AsNoTracking()
                .Select(p => p.EffectiveDate)
                .Distinct()
                .ToListAsync();

            var existingDateSet = existingDates.ToHashSet();
            result.TotalDatesWithData = existingDateSet.Count;

            // Query ALL non-business days (weekends + holidays) from BusinessCalendar
            var nonBusinessDays = await _context.BusinessCalendar
                .AsNoTracking()
                .Where(bc => bc.SourceId == 1  // US calendar
                    && bc.EffectiveDate >= dateRange.Min
                    && bc.EffectiveDate <= dateRange.Max
                    && !bc.IsBusinessDay)  // All non-business days
                .OrderBy(bc => bc.EffectiveDate)
                .Select(bc => new { bc.EffectiveDate, bc.IsHoliday })
                .ToListAsync();

            foreach (var nonBD in nonBusinessDays)
            {
                // Find prior business day from calendar table
                var priorBusinessDay = await _context.BusinessCalendar
                    .AsNoTracking()
                    .Where(bc => bc.SourceId == 1
                        && bc.EffectiveDate < nonBD.EffectiveDate
                        && bc.IsBusinessDay)
                    .OrderByDescending(bc => bc.EffectiveDate)
                    .Select(bc => bc.EffectiveDate)
                    .FirstOrDefaultAsync();

                var hasPriorData = priorBusinessDay != default && existingDateSet.Contains(priorBusinessDay);
                var hasExistingData = existingDateSet.Contains(nonBD.EffectiveDate);

                var dayType = nonBD.IsHoliday ? "Holiday" : "Weekend";
                result.MissingHolidays.Add(new MissingHolidayInfo
                {
                    HolidayName = $"{dayType} {nonBD.EffectiveDate:yyyy-MM-dd}",
                    HolidayDate = nonBD.EffectiveDate,
                    PriorTradingDay = priorBusinessDay,
                    HasPriorDayData = hasPriorData,
                    HasExistingData = hasExistingData
                });
            }

            result.Success = true;
            _logger.LogInformation("Non-business day analysis complete: {Total} days ({WithPrior} with prior data, {WithExisting} already have data)",
                result.MissingHolidays.Count, result.HolidaysWithPriorData, result.MissingHolidays.Count(h => h.HasExistingData));
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
            _logger.LogError(ex, "Non-business day analysis failed");
        }

        return result;
    }

    /// <inheritdoc />
    /// <summary>
    /// Forward-fills price data to ALL non-business days (weekends AND holidays).
    /// Creates a continuous 7-day calendar where non-business days have stale data (Volume=0)
    /// copied from the prior business day's close.
    /// </summary>
    /// <param name="limit">Optional limit on number of days to process. Null = all days.</param>
    public async Task<HolidayForwardFillResult> ForwardFillHolidaysAsync(int? limit = null)
    {
        var result = new HolidayForwardFillResult();

        try
        {
            // Get all business days that have price data (using calendar table)
            var businessDaysWithPriceData = await _context.Prices
                .AsNoTracking()
                .Select(p => p.EffectiveDate)
                .Distinct()
                .Join(
                    _context.BusinessCalendar.AsNoTracking().Where(bc => bc.SourceId == 1 && bc.IsBusinessDay),
                    priceDate => priceDate,
                    bc => bc.EffectiveDate,
                    (priceDate, bc) => priceDate)
                .OrderBy(d => d)
                .ToListAsync();

            if (businessDaysWithPriceData.Count == 0)
            {
                result.Error = "No price data found for business days";
                return result;
            }

            var minDate = businessDaysWithPriceData.Min();
            var maxDate = businessDaysWithPriceData.Max();

            // Get all non-business days that need forward-filling (don't have price data yet)
            // We need to count these upfront to track remaining
            var allNonBusinessDays = await _context.BusinessCalendar
                .AsNoTracking()
                .Where(bc => bc.SourceId == 1
                    && bc.EffectiveDate >= minDate
                    && bc.EffectiveDate <= maxDate
                    && !bc.IsBusinessDay)
                .Select(bc => bc.EffectiveDate)
                .ToListAsync();

            // Check which non-business days already have price data
            var datesWithPrices = await _context.Prices
                .AsNoTracking()
                .Where(p => allNonBusinessDays.Contains(p.EffectiveDate))
                .Select(p => p.EffectiveDate)
                .Distinct()
                .ToListAsync();

            var nonBusinessDaysNeedingFill = allNonBusinessDays
                .Except(datesWithPrices)
                .OrderBy(d => d)
                .ToList();

            var totalNonBusinessDaysToProcess = nonBusinessDaysNeedingFill.Count;

            _logger.LogInformation("Forward-filling non-business days from {Start:yyyy-MM-dd} to {End:yyyy-MM-dd}. " +
                "Total needing fill: {Total}, limit: {Limit}",
                minDate, maxDate, totalNonBusinessDaysToProcess, limit?.ToString() ?? "unlimited");

            int totalInserted = 0;
            int totalUpdated = 0;
            int daysProcessed = 0;

            // If limit is specified, only process that many days
            var daysToProcess = limit.HasValue
                ? nonBusinessDaysNeedingFill.Take(limit.Value).ToHashSet()
                : nonBusinessDaysNeedingFill.ToHashSet();

            // Process each business day with data
            for (int i = 0; i < businessDaysWithPriceData.Count; i++)
            {
                // Check if we've hit the limit
                if (limit.HasValue && daysProcessed >= limit.Value)
                    break;

                var businessDay = businessDaysWithPriceData[i];

                // Find the next business day (to know where non-business days end)
                var nextBusinessDay = (i + 1 < businessDaysWithPriceData.Count)
                    ? businessDaysWithPriceData[i + 1]
                    : maxDate.AddDays(1); // For the last business day, just process up to maxDate

                // Get non-business days between this business day and the next that need filling
                var nonBusinessDaysForThisBD = await _context.BusinessCalendar
                    .AsNoTracking()
                    .Where(bc => bc.SourceId == 1
                        && bc.EffectiveDate > businessDay
                        && bc.EffectiveDate < nextBusinessDay
                        && !bc.IsBusinessDay
                        && daysToProcess.Contains(bc.EffectiveDate))
                    .OrderBy(bc => bc.EffectiveDate)
                    .Select(bc => new { bc.EffectiveDate, bc.IsHoliday })
                    .ToListAsync();

                if (nonBusinessDaysForThisBD.Count == 0)
                    continue;

                // Get prices from the business day to copy forward
                var businessDayPrices = await _context.Prices
                    .AsNoTracking()
                    .Where(p => p.EffectiveDate == businessDay)
                    .ToListAsync();

                if (businessDayPrices.Count == 0)
                {
                    _logger.LogWarning("No prices found for business day {Date:yyyy-MM-dd}", businessDay);
                    continue;
                }

                // Process each non-business day after this business day
                foreach (var nonBD in nonBusinessDaysForThisBD)
                {
                    // Check limit again (in case we hit it mid-batch)
                    if (limit.HasValue && daysProcessed >= limit.Value)
                        break;

                    // Get existing records for the non-business day (for UPSERT)
                    var existingPrices = await _context.Prices
                        .Where(p => p.EffectiveDate == nonBD.EffectiveDate)
                        .ToDictionaryAsync(p => p.SecurityAlias);

                    int dayInserted = 0;
                    int dayUpdated = 0;

                    foreach (var prior in businessDayPrices)
                    {
                        if (existingPrices.TryGetValue(prior.SecurityAlias, out var existing))
                        {
                            // UPDATE: Overwrite with stale data from prior BD
                            existing.Open = prior.Close;
                            existing.High = prior.Close;
                            existing.Low = prior.Close;
                            existing.Close = prior.Close;
                            existing.Volume = 0; // Mark as stale/non-business day
                            existing.AdjustedClose = prior.AdjustedClose;
                            dayUpdated++;
                        }
                        else
                        {
                            // INSERT: Create new stale record
                            _context.Prices.Add(new PriceEntity
                            {
                                SecurityAlias = prior.SecurityAlias,
                                EffectiveDate = nonBD.EffectiveDate,
                                Open = prior.Close,
                                High = prior.Close,
                                Low = prior.Close,
                                Close = prior.Close,
                                Volume = 0, // Mark as stale/non-business day
                                AdjustedClose = prior.AdjustedClose,
                                CreatedAt = DateTime.UtcNow
                            });
                            dayInserted++;
                        }
                    }

                    await _context.SaveChangesAsync();

                    totalInserted += dayInserted;
                    totalUpdated += dayUpdated;
                    daysProcessed++;

                    var dayType = nonBD.IsHoliday ? "Holiday" : "Weekend";
                    result.HolidaysFilled.Add(($"{dayType} {nonBD.EffectiveDate:yyyy-MM-dd}", nonBD.EffectiveDate, dayInserted + dayUpdated));

                    if (daysProcessed % 50 == 0)
                    {
                        _logger.LogInformation("Progress: {Days}/{Total} non-business days processed, {Inserted:N0} inserted, {Updated:N0} updated",
                            daysProcessed, limit ?? totalNonBusinessDaysToProcess, totalInserted, totalUpdated);
                    }
                }
            }

            result.Success = true;
            result.HolidaysProcessed = daysProcessed;
            result.TotalRecordsInserted = totalInserted;
            result.RemainingDays = totalNonBusinessDaysToProcess - daysProcessed;
            result.Message = $"Forward-filled {daysProcessed} non-business days: {totalInserted:N0} inserted, {totalUpdated:N0} updated. Remaining: {result.RemainingDays}";

            _logger.LogInformation("Non-business day forward-fill batch complete: {Days} days, {Inserted:N0} inserted, {Updated:N0} updated, {Remaining} remaining",
                daysProcessed, totalInserted, totalUpdated, result.RemainingDays);
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
            _logger.LogError(ex, "Non-business day forward-fill failed");
        }

        return result;
    }
}
