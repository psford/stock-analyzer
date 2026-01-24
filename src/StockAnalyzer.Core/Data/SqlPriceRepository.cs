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

            var existingDateSet = existingDates.Select(d => DateOnly.FromDateTime(d)).ToHashSet();
            result.TotalDatesWithData = existingDateSet.Count;

            // Find holidays in range
            var startDate = DateOnly.FromDateTime(dateRange.Min);
            var endDate = DateOnly.FromDateTime(dateRange.Max);
            var holidays = UsMarketCalendar.GetHolidaysBetween(startDate, endDate).ToList();

            foreach (var holiday in holidays)
            {
                // Only consider holidays that fall on weekdays
                if (!holiday.IsWeekday) continue;

                // Check if we have data for this holiday
                if (existingDateSet.Contains(holiday.Date)) continue;

                // Find prior trading day
                var priorDay = UsMarketCalendar.GetPreviousTradingDay(holiday.Date);
                var hasPriorData = existingDateSet.Contains(priorDay);

                result.MissingHolidays.Add(new MissingHolidayInfo
                {
                    HolidayName = holiday.Name,
                    HolidayDate = holiday.Date.ToDateTime(TimeOnly.MinValue),
                    PriorTradingDay = priorDay.ToDateTime(TimeOnly.MinValue),
                    HasPriorDayData = hasPriorData
                });
            }

            result.Success = true;
            _logger.LogInformation("Holiday analysis complete: {Missing} holidays missing data ({WithPrior} with prior data available)",
                result.MissingHolidays.Count, result.HolidaysWithPriorData);
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
            _logger.LogError(ex, "Holiday analysis failed");
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<HolidayForwardFillResult> ForwardFillHolidaysAsync()
    {
        var result = new HolidayForwardFillResult();

        try
        {
            // First analyze to find missing holidays
            var analysis = await AnalyzeHolidaysAsync();
            if (!analysis.Success)
            {
                result.Error = analysis.Error;
                return result;
            }

            var toFill = analysis.MissingHolidays.Where(h => h.HasPriorDayData).ToList();
            if (toFill.Count == 0)
            {
                result.Success = true;
                result.Message = "No holidays need forward-fill";
                return result;
            }

            _logger.LogInformation("Forward-filling {Count} holidays with prior data available", toFill.Count);

            int totalInserted = 0;

            foreach (var missing in toFill.OrderBy(h => h.HolidayDate))
            {
                _logger.LogInformation("Forward-filling {Holiday} ({Date:yyyy-MM-dd}) from {Prior:yyyy-MM-dd}",
                    missing.HolidayName, missing.HolidayDate, missing.PriorTradingDay);

                // Get all prices from prior trading day
                var priorPrices = await _context.Prices
                    .AsNoTracking()
                    .Where(p => p.EffectiveDate == missing.PriorTradingDay)
                    .ToListAsync();

                if (priorPrices.Count == 0)
                {
                    _logger.LogWarning("No prior day prices found for {Date}", missing.PriorTradingDay);
                    continue;
                }

                // Check which securities already have data for the holiday
                var existingAliases = await _context.Prices
                    .AsNoTracking()
                    .Where(p => p.EffectiveDate == missing.HolidayDate)
                    .Select(p => p.SecurityAlias)
                    .ToListAsync();

                var existingSet = existingAliases.ToHashSet();

                // Create new price records for the holiday
                var newPrices = priorPrices
                    .Where(p => !existingSet.Contains(p.SecurityAlias))
                    .Select(p => new PriceEntity
                    {
                        SecurityAlias = p.SecurityAlias,
                        EffectiveDate = missing.HolidayDate,
                        Open = p.Close,   // Use prior close as OHLC
                        High = p.Close,
                        Low = p.Close,
                        Close = p.Close,
                        Volume = 0,       // No trading on holidays
                        AdjustedClose = p.AdjustedClose,
                        CreatedAt = DateTime.UtcNow
                    })
                    .ToList();

                if (newPrices.Count > 0)
                {
                    _context.Prices.AddRange(newPrices);
                    await _context.SaveChangesAsync();

                    totalInserted += newPrices.Count;
                    result.HolidaysFilled.Add((missing.HolidayName, missing.HolidayDate, newPrices.Count));

                    _logger.LogInformation("  Inserted {Count:N0} records for {Holiday}",
                        newPrices.Count, missing.HolidayName);
                }
            }

            result.Success = true;
            result.HolidaysProcessed = result.HolidaysFilled.Count;
            result.TotalRecordsInserted = totalInserted;
            result.Message = $"Forward-filled {result.HolidaysProcessed} holidays with {totalInserted:N0} total records";

            _logger.LogInformation("Holiday forward-fill complete: {Holidays} holidays, {Records:N0} records",
                result.HolidaysProcessed, totalInserted);
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
            _logger.LogError(ex, "Holiday forward-fill failed");
        }

        return result;
    }
}
