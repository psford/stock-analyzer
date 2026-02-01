using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
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

        // Process in batches of 1000, dedup per-batch to keep IN clauses small
        foreach (var batch in priceList.Chunk(1000))
        {
            var batchAliases = batch.Select(p => p.SecurityAlias).Distinct().ToList();
            var batchDates = batch.Select(p => p.EffectiveDate.Date).Distinct().ToList();

            var existingKeys = await _context.Prices
                .AsNoTracking()
                .Where(p => batchAliases.Contains(p.SecurityAlias) && batchDates.Contains(p.EffectiveDate))
                .Select(p => new { p.SecurityAlias, p.EffectiveDate })
                .ToListAsync();

            var existingSet = existingKeys
                .Select(k => (k.SecurityAlias, k.EffectiveDate))
                .ToHashSet();

            var newPrices = batch
                .Where(p => !existingSet.Contains((p.SecurityAlias, p.EffectiveDate.Date)))
                .ToList();

            if (newPrices.Count == 0)
                continue;

            var entities = newPrices.Select(dto => new PriceEntity
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
            count += newPrices.Count;

            if (count % 10000 == 0)
            {
                _logger.LogInformation("Bulk insert progress: {Count} prices inserted", count);
            }
        }

        _logger.LogInformation("Bulk insert complete: {Count} new prices inserted out of {Total} total",
            count, priceList.Count);
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
        // Two TOP 1 index seeks instead of GroupBy anti-pattern that prevents index optimization
        var earliest = await _context.Prices
            .AsNoTracking()
            .Where(p => p.SecurityAlias == securityAlias)
            .OrderBy(p => p.EffectiveDate)
            .Select(p => (DateTime?)p.EffectiveDate)
            .FirstOrDefaultAsync();

        var latest = await _context.Prices
            .AsNoTracking()
            .Where(p => p.SecurityAlias == securityAlias)
            .OrderByDescending(p => p.EffectiveDate)
            .Select(p => (DateTime?)p.EffectiveDate)
            .FirstOrDefaultAsync();

        return (earliest, latest);
    }

    /// <inheritdoc />
    public async Task<long> GetTotalCountAsync()
    {
        // Use CoverageSummary pre-aggregated table instead of scanning 7M+ row Prices table
        var total = await _context.CoverageSummary
            .AsNoTracking()
            .SumAsync(s => s.TrackedRecords + s.UntrackedRecords);
        return total;
    }

    /// <inheritdoc />
    public async Task<int> GetCountForSecurityAsync(int securityAlias)
    {
        return await _context.Prices
            .AsNoTracking()
            .CountAsync(p => p.SecurityAlias == securityAlias);
    }

    /// <inheritdoc />
    public async Task<List<DateTime>> GetDistinctDatesAsync(DateTime startDate, DateTime endDate)
    {
        // While-loop skip-scan: ~500 MIN() index seeks for 2 years of trading days
        // instead of scanning 5M+ rows via SELECT DISTINCT.
        // Each iteration uses IX_Prices_EffectiveDate to jump to the next unique date.
        // (Recursive CTEs don't allow TOP, CROSS APPLY, or aggregates in the recursive member.)
        var sql = @"
            DECLARE @dates TABLE (EffectiveDate DATE);
            DECLARE @dt DATE = (SELECT MIN(EffectiveDate) FROM data.Prices WITH (NOLOCK)
                                WHERE EffectiveDate >= @p0 AND EffectiveDate <= @p1);
            WHILE @dt IS NOT NULL
            BEGIN
                INSERT INTO @dates VALUES (@dt);
                SET @dt = (SELECT MIN(EffectiveDate) FROM data.Prices WITH (NOLOCK)
                           WHERE EffectiveDate > @dt AND EffectiveDate <= @p1);
            END;
            SELECT EffectiveDate FROM @dates ORDER BY EffectiveDate;";

        return await _context.Database
            .SqlQueryRaw<DateTime>(sql, startDate.Date, endDate.Date)
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
            // Use CoverageSummary for date range instead of full table scan
            var summaryRows = await _context.CoverageSummary
                .AsNoTracking()
                .ToListAsync();

            if (!summaryRows.Any())
            {
                result.Error = "No price data found in database";
                return result;
            }

            var minYear = summaryRows.Min(r => r.Year);
            var maxYear = summaryRows.Max(r => r.Year);

            // Fast date range via TOP 1 index seeks (IX_Prices_EffectiveDate)
            var minDate = await _context.Prices
                .AsNoTracking()
                .OrderBy(p => p.EffectiveDate)
                .Select(p => p.EffectiveDate)
                .FirstOrDefaultAsync();

            var maxDate = await _context.Prices
                .AsNoTracking()
                .OrderByDescending(p => p.EffectiveDate)
                .Select(p => p.EffectiveDate)
                .FirstOrDefaultAsync();

            result.DataStartDate = minDate;
            result.DataEndDate = maxDate;

            // Get distinct dates using efficient recursive CTE skip-scan (~500 seeks)
            var existingDates = await GetDistinctDatesAsync(minDate, maxDate);
            var existingDateSet = existingDates.ToHashSet();
            result.TotalDatesWithData = existingDateSet.Count;

            // Load ALL calendar entries in range (single query, ~4K rows)
            var calendarEntries = await _context.BusinessCalendar
                .AsNoTracking()
                .Where(bc => bc.SourceId == 1
                    && bc.EffectiveDate >= minDate
                    && bc.EffectiveDate <= maxDate)
                .OrderBy(bc => bc.EffectiveDate)
                .Select(bc => new { bc.EffectiveDate, bc.IsBusinessDay, bc.IsHoliday })
                .ToListAsync();

            // Build sorted list of business days for binary search
            var sortedBDs = calendarEntries
                .Where(c => c.IsBusinessDay)
                .Select(c => c.EffectiveDate)
                .ToList(); // Already sorted by OrderBy above

            var nonBusinessDays = calendarEntries.Where(c => !c.IsBusinessDay).ToList();

            foreach (var nonBD in nonBusinessDays)
            {
                // Binary search for prior business day (O(log n) instead of DB query)
                var idx = sortedBDs.BinarySearch(nonBD.EffectiveDate);
                if (idx < 0) idx = ~idx; // Index of first BD > nonBD.EffectiveDate
                idx--; // Step back to last BD < nonBD.EffectiveDate

                var priorBusinessDay = idx >= 0 ? sortedBDs[idx] : default;
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
            // 1. Get date range via TOP 1 index seeks
            var minDate = await _context.Prices
                .AsNoTracking()
                .OrderBy(p => p.EffectiveDate)
                .Select(p => p.EffectiveDate)
                .FirstOrDefaultAsync();

            var maxDate = await _context.Prices
                .AsNoTracking()
                .OrderByDescending(p => p.EffectiveDate)
                .Select(p => p.EffectiveDate)
                .FirstOrDefaultAsync();

            if (minDate == default)
            {
                result.Error = "No price data found in database";
                return result;
            }

            // 2. Get existing price dates via efficient recursive CTE (~500 seeks)
            var existingDates = await GetDistinctDatesAsync(minDate, maxDate);
            var existingDateSet = existingDates.ToHashSet();

            // 3. Get all calendar entries in range (single query, ~4K rows)
            var calendarEntries = await _context.BusinessCalendar
                .AsNoTracking()
                .Where(bc => bc.SourceId == 1
                    && bc.EffectiveDate >= minDate
                    && bc.EffectiveDate <= maxDate)
                .OrderBy(bc => bc.EffectiveDate)
                .Select(bc => new { bc.EffectiveDate, bc.IsBusinessDay, bc.IsHoliday })
                .ToListAsync();

            var sortedBDs = calendarEntries
                .Where(c => c.IsBusinessDay)
                .Select(c => c.EffectiveDate)
                .ToList();

            // 4. Build fill targets: non-BDs needing data, mapped to prior BD with data
            var fillTargets = new List<(DateTime NonBDDate, DateTime PriorBDDate, bool IsHoliday)>();

            foreach (var entry in calendarEntries.Where(c => !c.IsBusinessDay))
            {
                if (existingDateSet.Contains(entry.EffectiveDate))
                    continue; // Already has price data

                // Binary search for prior business day
                var idx = sortedBDs.BinarySearch(entry.EffectiveDate);
                if (idx < 0) idx = ~idx;
                idx--;

                if (idx >= 0 && existingDateSet.Contains(sortedBDs[idx]))
                    fillTargets.Add((entry.EffectiveDate, sortedBDs[idx], entry.IsHoliday));
            }

            var totalNeedingFill = fillTargets.Count;
            if (limit.HasValue)
                fillTargets = fillTargets.Take(limit.Value).ToList();

            _logger.LogInformation(
                "Forward-fill: {Count} non-BD dates to process (of {Total} needing fill), limit: {Limit}",
                fillTargets.Count, totalNeedingFill, limit?.ToString() ?? "unlimited");

            if (fillTargets.Count == 0)
            {
                result.Success = true;
                result.HolidaysProcessed = 0;
                result.TotalRecordsInserted = 0;
                result.RemainingDays = 0;
                result.Message = "No non-business days need forward-filling";
                return result;
            }

            // 5. Process in batches of 50 using raw SQL MERGE
            int totalInserted = 0;
            int totalUpdated = 0;
            int daysProcessed = 0;

            var connection = _context.Database.GetDbConnection();
            var connectionWasOpen = connection.State == ConnectionState.Open;
            if (!connectionWasOpen)
                await connection.OpenAsync();

            try
            {
                foreach (var batch in fillTargets.Chunk(50))
                {
                    var batchLookup = batch.ToDictionary(b => b.NonBDDate);

                    // Build VALUES clause with date literals (from our calendar table, safe)
                    var valuesClause = string.Join(", ",
                        batch.Select(t =>
                            $"('{t.NonBDDate:yyyy-MM-dd}', '{t.PriorBDDate:yyyy-MM-dd}')"));

                    var sql = @"
                        CREATE TABLE #FillMapping (NonBDDate DATE NOT NULL, PriorBDDate DATE NOT NULL);
                        INSERT INTO #FillMapping (NonBDDate, PriorBDDate) VALUES " + valuesClause + @";

                        DECLARE @results TABLE (ActionType NVARCHAR(10), EffDate DATE);

                        MERGE data.Prices AS target
                        USING (
                            SELECT fm.NonBDDate, p.SecurityAlias, p.[Close], p.AdjustedClose
                            FROM #FillMapping fm
                            INNER JOIN data.Prices p WITH (NOLOCK) ON p.EffectiveDate = fm.PriorBDDate
                        ) AS source
                        ON target.SecurityAlias = source.SecurityAlias
                            AND target.EffectiveDate = source.NonBDDate
                        WHEN MATCHED THEN
                            UPDATE SET [Open] = source.[Close], High = source.[Close],
                                       Low = source.[Close], [Close] = source.[Close],
                                       Volume = 0, AdjustedClose = source.AdjustedClose
                        WHEN NOT MATCHED THEN
                            INSERT (SecurityAlias, EffectiveDate, [Open], High, Low, [Close],
                                    Volume, AdjustedClose, CreatedAt)
                            VALUES (source.SecurityAlias, source.NonBDDate, source.[Close],
                                    source.[Close], source.[Close], source.[Close],
                                    0, source.AdjustedClose, GETUTCDATE())
                        OUTPUT $action, inserted.EffectiveDate INTO @results;

                        SELECT EffDate,
                            SUM(CASE WHEN ActionType = 'INSERT' THEN 1 ELSE 0 END) AS InsertCount,
                            SUM(CASE WHEN ActionType = 'UPDATE' THEN 1 ELSE 0 END) AS UpdateCount
                        FROM @results GROUP BY EffDate ORDER BY EffDate;

                        DROP TABLE #FillMapping;";

                    using var command = connection.CreateCommand();
#pragma warning disable CA2100 // Date literals are from BusinessCalendar table, not user input
                    command.CommandText = sql;
#pragma warning restore CA2100
                    command.CommandTimeout = 120;

                    if (_context.Database.CurrentTransaction != null)
                        command.Transaction = _context.Database.CurrentTransaction.GetDbTransaction();

                    using var reader = await command.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        var effDate = reader.GetDateTime(0);
                        var insertCount = reader.GetInt32(1);
                        var updateCount = reader.GetInt32(2);

                        totalInserted += insertCount;
                        totalUpdated += updateCount;
                        daysProcessed++;

                        var isHoliday = batchLookup.TryGetValue(effDate, out var target) && target.IsHoliday;
                        var dayType = isHoliday ? "Holiday" : "Weekend";
                        result.HolidaysFilled.Add(
                            ($"{dayType} {effDate:yyyy-MM-dd}", effDate, insertCount + updateCount));
                    }

                    if (daysProcessed % 100 == 0)
                    {
                        _logger.LogInformation(
                            "Progress: {Days}/{Total} non-BD dates, {Inserted:N0} inserted, {Updated:N0} updated",
                            daysProcessed, fillTargets.Count, totalInserted, totalUpdated);
                    }
                }
            }
            finally
            {
                if (!connectionWasOpen)
                    await connection.CloseAsync();
            }

            result.Success = true;
            result.HolidaysProcessed = daysProcessed;
            result.TotalRecordsInserted = totalInserted;
            result.RemainingDays = totalNeedingFill - daysProcessed;
            result.Message = $"Forward-filled {daysProcessed} non-business days: " +
                $"{totalInserted:N0} inserted, {totalUpdated:N0} updated. " +
                $"Remaining: {result.RemainingDays}";

            _logger.LogInformation(
                "Forward-fill complete: {Days} days, {Inserted:N0} inserted, {Updated:N0} updated, {Remaining} remaining",
                daysProcessed, totalInserted, totalUpdated, result.RemainingDays);
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
            _logger.LogError(ex, "Forward-fill failed");
        }

        return result;
    }
}
