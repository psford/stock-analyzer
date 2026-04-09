using System.Collections.Concurrent;
using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StockAnalyzer.Core.Data;
using StockAnalyzer.Core.Data.Entities;
using StockAnalyzer.Core.Helpers;

namespace StockAnalyzer.Core.Services;

/// <summary>
/// Background service that maintains the historical price database.
/// - On startup: Checks for missing recent data and backfills
/// - Daily: Fetches previous day's prices at 2:30 AM UTC
/// - Manual: Supports bulk loading via admin endpoints
/// </summary>
public class PriceRefreshService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<PriceRefreshService> _logger;
    private readonly int _targetHourUtc;
    private readonly int _targetMinuteUtc;
    private readonly int _startupDelaySeconds;

    private DateTime _lastRefresh = DateTime.MinValue;

    public PriceRefreshService(
        IServiceProvider serviceProvider,
        ILogger<PriceRefreshService> logger,
        IConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;

        // Run at 2:30 AM UTC (after market close, after symbol refresh at 2:00 AM)
        _targetHourUtc = configuration.GetValue("PriceDatabase:RefreshHourUtc", 2);
        _targetMinuteUtc = configuration.GetValue("PriceDatabase:RefreshMinuteUtc", 30);

        // Delay startup to let app stabilize and other services initialize
        _startupDelaySeconds = configuration.GetValue("PriceDatabase:StartupDelaySeconds", 45);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PriceRefreshService starting");

        // Wait for app to stabilize
        await Task.Delay(TimeSpan.FromSeconds(_startupDelaySeconds), stoppingToken);

        // Check if EODHD is configured
        using (var scope = _serviceProvider.CreateScope())
        {
            var eodhd = scope.ServiceProvider.GetService<EodhdService>();
            if (eodhd == null || !eodhd.IsAvailable)
            {
                _logger.LogWarning("EODHD API key not configured, PriceRefreshService disabled");
                return;
            }
        }

        // Check for missing recent data on startup
        await CheckAndBackfillRecentDataAsync(stoppingToken);

        // Main refresh loop
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTime.UtcNow;
                var targetTime = new DateTime(now.Year, now.Month, now.Day,
                    _targetHourUtc, _targetMinuteUtc, 0, DateTimeKind.Utc);

                // If we've already passed target time today, schedule for tomorrow
                if (now > targetTime)
                {
                    targetTime = targetTime.AddDays(1);
                }

                var delay = targetTime - now;
                _logger.LogDebug("Next price refresh scheduled for {TargetTime} (in {Delay})",
                    targetTime, delay);

                await Task.Delay(delay, stoppingToken);

                // Run every day — lookback + forward-fill handles weekends/holidays
                await RunDailyRefreshCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in price refresh loop, retrying in 1 hour");
                await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
            }
        }
    }

    /// <summary>
    /// Check the most recent price date and backfill any missing recent days.
    /// Called on startup to ensure we're up to date.
    /// Uses BusinessCalendar to determine business days (accounts for holidays).
    /// </summary>
    private async Task CheckAndBackfillRecentDataAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var priceRepo = scope.ServiceProvider.GetRequiredService<IPriceRepository>();
        var securityRepo = scope.ServiceProvider.GetRequiredService<ISecurityMasterRepository>();
        var dbContext = scope.ServiceProvider.GetRequiredService<StockAnalyzerDbContext>();

        // Get total price count to check if we have any data
        var totalPrices = await priceRepo.GetTotalCountAsync();
        if (totalPrices == 0)
        {
            _logger.LogInformation("Price database is empty. Use admin endpoint to bulk load historical data.");
            return;
        }

        // Get the most recent date we have prices for (projected query, 2 columns only)
        var tickerAliasMap = await securityRepo.GetActiveTickerAliasMapAsync();
        if (tickerAliasMap.Count == 0)
        {
            _logger.LogInformation("No active securities in SecurityMaster. Run security sync first.");
            return;
        }

        // Sample a few securities to find the max date
        var sampleAliases = tickerAliasMap.Values.Take(10);
        var latestPrices = await priceRepo.GetLatestPricesAsync(sampleAliases);

        if (latestPrices.Count == 0)
        {
            _logger.LogInformation("No prices found for sample securities");
            return;
        }

        var maxDate = latestPrices.Values.Max(p => p.EffectiveDate);

        // Get most recent business day from calendar (accounts for holidays)
        var yesterday = await dbContext.BusinessCalendar
            .AsNoTracking()
            .Where(bc => bc.SourceId == 1
                && bc.IsBusinessDay
                && bc.EffectiveDate < DateTime.UtcNow.Date)
            .OrderByDescending(bc => bc.EffectiveDate)
            .Select(bc => bc.EffectiveDate)
            .FirstOrDefaultAsync(ct);

        if (yesterday == default)
        {
            _logger.LogWarning("No business days found in BusinessCalendar");
            return;
        }

        _logger.LogInformation("Most recent price date: {MaxDate}, last trading day: {Yesterday}",
            maxDate.ToString("yyyy-MM-dd"), yesterday.ToString("yyyy-MM-dd"));

        // If we're missing recent days, backfill them
        if (maxDate < yesterday)
        {
            // Use BusinessCalendar instead of hardcoded weekday logic
            var missingDays = await dbContext.BusinessCalendar
                .AsNoTracking()
                .Where(bc => bc.SourceId == 1
                    && bc.IsBusinessDay
                    && bc.EffectiveDate > maxDate
                    && bc.EffectiveDate <= yesterday)
                .Select(bc => bc.EffectiveDate)
                .OrderBy(d => d)
                .ToListAsync(ct);

            if (missingDays.Count > 0)
            {
                _logger.LogInformation("Found {Count} missing trading days to backfill", missingDays.Count);

                foreach (var date in missingDays)
                {
                    if (ct.IsCancellationRequested) break;
                    await RefreshDateAsync(date, ct);
                }
            }
        }
        else
        {
            _logger.LogInformation("Price database is up to date");
        }
    }

    /// <summary>
    /// Run the full daily refresh cycle: lookback for missed business days, fetch, forward-fill.
    /// Runs every day including weekends — uses BusinessCalendar to determine which dates need data.
    /// </summary>
    private async Task RunDailyRefreshCycleAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var priceRepo = scope.ServiceProvider.GetRequiredService<IPriceRepository>();
        var dbContext = scope.ServiceProvider.GetRequiredService<StockAnalyzerDbContext>();

        var today = DateTime.UtcNow.Date;
        var lookbackStart = today.AddDays(-14);

        // 1. Get business days in lookback window from BusinessCalendar (SourceId=1 = US market)
        var businessDays = await dbContext.BusinessCalendar
            .AsNoTracking()
            .Where(bc => bc.SourceId == 1
                && bc.IsBusinessDay
                && bc.EffectiveDate >= lookbackStart
                && bc.EffectiveDate <= today)
            .Select(bc => bc.EffectiveDate)
            .OrderBy(d => d)
            .ToListAsync(ct);

        // 2. Check which business days already have price data
        // GetDistinctDatesAsync returns Task<List<DateTime>> — compatible with BusinessCalendar DateTime values
        var datesWithPrices = await priceRepo.GetDistinctDatesAsync(lookbackStart, today);
        var datesWithPricesSet = datesWithPrices.ToHashSet();

        var missingDays = businessDays.Where(d => !datesWithPricesSet.Contains(d)).ToList();

        _logger.LogInformation(
            "Daily refresh cycle: {Total} business days in 14-day lookback, {Missing} missing prices",
            businessDays.Count, missingDays.Count);

        // 3. Fetch missing days via EODHD bulk API
        var totalInserted = 0;
        foreach (var date in missingDays)
        {
            if (ct.IsCancellationRequested) break;
            var result = await RefreshDateAsync(date, ct);
            totalInserted += result.RecordsInserted;

            if (result.RecordsFetched == 0 && date >= today.AddDays(-3))
            {
                _logger.LogWarning(
                    "No EODHD data for recent business day {Date}, will retry next cycle",
                    date.ToString("yyyy-MM-dd"));
            }
        }

        // 4. Forward-fill non-business days (weekends + holidays) up to today
        // This is intentionally called every day including weekends — ForwardFillHolidaysAsync is
        // idempotent (skips dates that already have data) and the date cap prevents future fills.
        // Running daily ensures any newly-inserted business day data propagates forward-fills promptly.
        var fillResult = await priceRepo.ForwardFillHolidaysAsync(maxFillDate: today);

        _logger.LogInformation(
            "Daily refresh cycle complete: {MissingDays} days backfilled, {Inserted} records inserted, {Filled} forward-fill records",
            missingDays.Count, totalInserted, fillResult.TotalRecordsInserted);
    }

    /// <summary>
    /// Refresh prices for the previous trading day.
    /// Called daily by the background loop.
    /// OBSOLETE: Use RunDailyRefreshCycleAsync instead.
    /// </summary>
    [Obsolete("Use RunDailyRefreshCycleAsync instead")]
    private async Task RefreshPreviousDayAsync(CancellationToken ct)
    {
        var yesterday = GetLastTradingDay(DateTime.UtcNow.Date);
        await RefreshDateAsync(yesterday, ct);
    }

    /// <summary>
    /// Refresh prices for a specific date.
    /// </summary>
    /// <returns>Result with counts of records processed and inserted</returns>
    public async Task<RefreshDateResult> RefreshDateAsync(DateTime date, CancellationToken ct)
    {
        _logger.LogInformation("Refreshing prices for {Date}", date.ToString("yyyy-MM-dd"));

        using var scope = _serviceProvider.CreateScope();
        var eodhd = scope.ServiceProvider.GetRequiredService<EodhdService>();
        var priceRepo = scope.ServiceProvider.GetRequiredService<IPriceRepository>();
        var securityRepo = scope.ServiceProvider.GetRequiredService<ISecurityMasterRepository>();

        var result = new RefreshDateResult { Date = date };

        // Get ticker→alias map (projected query, 2 columns only — not 55K full entities)
        var tickerToAlias = await securityRepo.GetActiveTickerAliasMapAsync();
        if (tickerToAlias.Count == 0)
        {
            _logger.LogWarning("No active securities to refresh");
            return result;
        }

        // Fetch bulk data from EODHD
        var bulkData = await eodhd.GetBulkEodDataAsync(date, "US", ct);
        result.RecordsFetched = bulkData.Count;

        if (bulkData.Count == 0)
        {
            _logger.LogWarning(
                "No bulk data returned for {Date}. EODHD may be unavailable or date may be a holiday. " +
                "Service will retry on next cycle.",
                date.ToString("yyyy-MM-dd"));
            result.RecordsFetched = 0;
            return result;
        }

        // Convert to price DTOs, matching only securities we track
        var priceDtos = new List<PriceCreateDto>();

        foreach (var record in bulkData)
        {
            var ticker = record.Ticker.ToUpperInvariant();
            if (tickerToAlias.TryGetValue(ticker, out var alias))
            {
                priceDtos.Add(new PriceCreateDto
                {
                    SecurityAlias = alias,
                    EffectiveDate = record.ParsedDate,
                    Open = record.Open,
                    High = record.High,
                    Low = record.Low,
                    Close = record.Close,
                    AdjustedClose = record.AdjustedClose,
                    Volume = (long)record.Volume
                });
                result.RecordsMatched++;
            }
            else
            {
                result.RecordsUnmatched++;
            }
        }

        _logger.LogInformation("Matched {Matched} securities, {Unmatched} not in SecurityMaster",
            result.RecordsMatched, result.RecordsUnmatched);

        if (priceDtos.Count > 0)
        {
            result.RecordsInserted = await priceRepo.BulkInsertAsync(priceDtos);
            _logger.LogInformation("Inserted {Count} prices for {Date}",
                result.RecordsInserted, date.ToString("yyyy-MM-dd"));
        }

        _logger.LogInformation(
            "Refresh summary for {Date}: Fetched={Fetched}, Matched={Matched} ({MatchRate:P0}), Inserted={Inserted}",
            date.ToString("yyyy-MM-dd"),
            result.RecordsFetched,
            result.RecordsMatched,
            result.RecordsFetched > 0 ? (double)result.RecordsMatched / result.RecordsFetched : 0,
            result.RecordsInserted);

        _lastRefresh = DateTime.UtcNow;
        return result;
    }

    /// <summary>
    /// Result of refreshing prices for a specific date.
    /// </summary>
    public class RefreshDateResult
    {
        public DateTime Date { get; set; }
        public int RecordsFetched { get; set; }
        public int RecordsMatched { get; set; }
        public int RecordsUnmatched { get; set; }
        public int RecordsInserted { get; set; }
    }

    /// <summary>
    /// Execute the gap audit using raw SQL to identify all missing business days
    /// for tracked securities.
    /// </summary>
    /// <param name="dbContext">Database context with open connection</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>List of (SecurityAlias, TickerSymbol, MissingDate) tuples</returns>
    private async Task<List<(int SecurityAlias, string TickerSymbol, DateTime MissingDate)>> RunGapAuditAsync(
        StockAnalyzerDbContext dbContext, CancellationToken ct)
    {
        var result = new List<(int SecurityAlias, string TickerSymbol, DateTime MissingDate)>();

        var sql = @"
            WITH TrackedSecurities AS (
                SELECT sm.SecurityAlias, sm.TickerSymbol
                FROM data.SecurityMaster sm
                WHERE sm.IsTracked = 1 AND sm.IsActive = 1 AND sm.IsEodhdUnavailable = 0
            ),
            PriceRanges AS (
                SELECT p.SecurityAlias, MIN(p.EffectiveDate) AS FirstDate, MAX(p.EffectiveDate) AS LastDate
                FROM data.Prices p
                INNER JOIN TrackedSecurities ts ON ts.SecurityAlias = p.SecurityAlias
                GROUP BY p.SecurityAlias
            ),
            ExpectedDates AS (
                SELECT ts.SecurityAlias, ts.TickerSymbol, bc.EffectiveDate
                FROM TrackedSecurities ts
                INNER JOIN PriceRanges pr ON pr.SecurityAlias = ts.SecurityAlias
                INNER JOIN data.BusinessCalendar bc ON bc.SourceId = 1 AND bc.IsBusinessDay = 1
                    AND bc.EffectiveDate BETWEEN pr.FirstDate AND pr.LastDate
            )
            SELECT ed.SecurityAlias, ed.TickerSymbol, ed.EffectiveDate AS MissingDate
            FROM ExpectedDates ed
            LEFT JOIN data.Prices p ON p.SecurityAlias = ed.SecurityAlias AND p.EffectiveDate = ed.EffectiveDate
            WHERE p.Id IS NULL
            ORDER BY ed.TickerSymbol, ed.EffectiveDate";

        try
        {
            var connection = dbContext.Database.GetDbConnection();
            var connectionWasOpen = connection.State == ConnectionState.Open;

            if (!connectionWasOpen)
                await connection.OpenAsync(ct);

            try
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = sql;
                cmd.CommandTimeout = 300;

                using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    var securityAlias = reader.GetInt32(0);
                    var tickerSymbol = reader.GetString(1);
                    var missingDate = reader.GetDateTime(2);

                    result.Add((securityAlias, tickerSymbol, missingDate));
                }
            }
            finally
            {
                if (!connectionWasOpen)
                    connection.Close();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing gap audit");
            throw;
        }

        return result;
    }

    /// <summary>
    /// Backfill all identified gaps in price history using EODHD per-ticker API.
    /// 1. Runs gap audit to identify missing business days
    /// 2. Fetches data from EODHD for each security with gaps
    /// 3. Inserts fetched data
    /// 4. Flags securities where EODHD has no data
    /// 5. Re-runs audit to verify completion
    /// </summary>
    /// <param name="maxConcurrency">Max concurrent API requests (1-10, default 3)</param>
    /// <param name="progress">Optional progress callback</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Result with counts and any errors</returns>
    public async Task<GapBackfillResult> BackfillGapsAsync(
        int maxConcurrency = 3,
        IProgress<TickerBackfillProgress>? progress = null,
        CancellationToken ct = default)
    {
        using var scope = _serviceProvider.CreateScope();
        var eodhd = scope.ServiceProvider.GetRequiredService<EodhdService>();
        var priceRepo = scope.ServiceProvider.GetRequiredService<IPriceRepository>();
        var dbContext = scope.ServiceProvider.GetRequiredService<StockAnalyzerDbContext>();

        var result = new GapBackfillResult();

        // 1. Run gap audit
        var gaps = await RunGapAuditAsync(dbContext, ct);
        result.TotalGapsFound = gaps.Count;

        _logger.LogInformation("Gap audit found {Count} missing price-days", gaps.Count);

        if (gaps.Count == 0)
        {
            result.Success = true;
            result.Message = "No gaps found";
            return result;
        }

        // 2. Group by security, compute per-ticker date ranges
        var securityMap = await dbContext.SecurityMaster
            .AsNoTracking()
            .Where(s => s.IsTracked && s.IsActive && !s.IsEodhdUnavailable)
            .ToDictionaryAsync(s => s.SecurityAlias, s => s, ct);

        var gapsByTicker = gaps
            .GroupBy(g => new { g.SecurityAlias, g.TickerSymbol })
            .Where(g => securityMap.ContainsKey(g.Key.SecurityAlias))
            .OrderByDescending(g => securityMap.GetValueOrDefault(g.Key.SecurityAlias)?.ImportanceScore ?? 0)
            .Select(g => new
            {
                g.Key.TickerSymbol,
                g.Key.SecurityAlias,
                StartDate = g.Min(x => x.MissingDate),
                EndDate = g.Max(x => x.MissingDate),
                GapCount = g.Count()
            })
            .ToList();

        _logger.LogInformation("Backfilling {Count} securities with gaps", gapsByTicker.Count);

        // 3. Fetch per-ticker with throttling
        var tickersWithNoData = new ConcurrentBag<(int SecurityAlias, string TickerSymbol)>();
        var totalInserted = 0;
        var tickersProcessed = 0;

        using var semaphore = new SemaphoreSlim(Math.Clamp(maxConcurrency, 1, 10));

        var tasks = gapsByTicker.Select(async ticker =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                // Rate limit: 100ms spacing
                await Task.Delay(100, ct);

                var data = await eodhd.GetHistoricalDataAsync(
                    ticker.TickerSymbol, ticker.StartDate, ticker.EndDate, ct);

                if (data.Count == 0)
                {
                    tickersWithNoData.Add((ticker.SecurityAlias, ticker.TickerSymbol));
                    return;
                }

                var priceDtos = data.Select(r => new PriceCreateDto
                {
                    SecurityAlias = ticker.SecurityAlias,
                    EffectiveDate = r.ParsedDate,
                    Open = r.Open,
                    High = r.High,
                    Low = r.Low,
                    Close = r.Close,
                    AdjustedClose = r.AdjustedClose,
                    Volume = (long)r.Volume
                }).ToList();

                var inserted = await priceRepo.BulkInsertAsync(priceDtos);
                Interlocked.Add(ref totalInserted, inserted);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error backfilling {Ticker}", ticker.TickerSymbol);
                result.Errors.Add($"{ticker.TickerSymbol}: {ex.Message}");
            }
            finally
            {
                semaphore.Release();
                var processed = Interlocked.Increment(ref tickersProcessed);
                progress?.Report(new TickerBackfillProgress
                {
                    CurrentTicker = ticker.TickerSymbol,
                    TickersProcessed = processed,
                    TotalTickers = gapsByTicker.Count,
                    RecordsInserted = totalInserted,
                    PercentComplete = (int)((double)processed / gapsByTicker.Count * 100)
                });

                if (processed % 50 == 0)
                {
                    _logger.LogInformation(
                        "Backfill progress: {Processed}/{Total} securities, {Inserted} records inserted",
                        processed, gapsByTicker.Count, totalInserted);
                }
            }
        });

        await Task.WhenAll(tasks);

        // 4. Flag securities where EODHD returned no data
        foreach (var (securityAlias, tickerSymbol) in tickersWithNoData)
        {
            var security = await dbContext.SecurityMaster
                .FirstOrDefaultAsync(s => s.SecurityAlias == securityAlias, ct);
            if (security != null)
            {
                security.IsEodhdUnavailable = true;
                _logger.LogInformation("Marked {Ticker} (alias {Alias}) as EODHD unavailable",
                    tickerSymbol, securityAlias);
            }
        }
        await dbContext.SaveChangesAsync(ct);
        result.SecuritiesFlagged = tickersWithNoData.Count;

        // 5. Re-run gap audit to verify
        var remainingGaps = await RunGapAuditAsync(dbContext, ct);
        result.RemainingGaps = remainingGaps.Count;

        result.TickersProcessed = tickersProcessed;
        result.TotalRecordsInserted = totalInserted;
        result.TickersWithNoData = tickersWithNoData.Count;
        result.Success = true;
        result.Message = $"Backfill complete: {totalInserted} records inserted, " +
            $"{tickersWithNoData.Count} securities flagged unavailable, " +
            $"{remainingGaps.Count} gaps remaining";

        _logger.LogInformation(
            "Backfill complete: {Inserted} records, {Flagged} flagged unavailable, {Remaining} gaps remaining",
            totalInserted, tickersWithNoData.Count, remainingGaps.Count);

        return result;
    }

    /// <summary>
    /// Bulk load historical data for a date range.
    /// Called via admin endpoint for initial data loading.
    /// </summary>
    public async Task<BulkLoadResult> BulkLoadHistoricalDataAsync(
        DateTime startDate,
        DateTime endDate,
        IProgress<BulkLoadProgress>? progress = null,
        CancellationToken ct = default)
    {
        var result = new BulkLoadResult();
        var tradingDays = GetTradingDaysBetween(startDate, endDate);

        _logger.LogInformation("Starting bulk load for {Count} trading days from {Start} to {End}",
            tradingDays.Count, startDate.ToString("yyyy-MM-dd"), endDate.ToString("yyyy-MM-dd"));

        result.TotalDays = tradingDays.Count;

        for (int i = 0; i < tradingDays.Count; i++)
        {
            if (ct.IsCancellationRequested)
            {
                result.WasCancelled = true;
                break;
            }

            var date = tradingDays[i];

            try
            {
                await RefreshDateAsync(date, ct);
                result.DaysProcessed++;

                progress?.Report(new BulkLoadProgress
                {
                    CurrentDate = date,
                    DaysProcessed = i + 1,
                    TotalDays = tradingDays.Count,
                    PercentComplete = (int)((i + 1) * 100.0 / tradingDays.Count)
                });

                // Respect rate limits - EODHD bulk API is 100 calls per request
                // With a generous delay between days to avoid hitting limits
                if (i < tradingDays.Count - 1)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), ct);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading data for {Date}", date.ToString("yyyy-MM-dd"));
                result.Errors.Add($"{date:yyyy-MM-dd}: {ex.Message}");
            }
        }

        _logger.LogInformation("Bulk load complete: {Processed}/{Total} days, {Errors} errors",
            result.DaysProcessed, result.TotalDays, result.Errors.Count);

        return result;
    }

    /// <summary>
    /// Load historical data for specific tickers.
    /// Uses the per-ticker historical API endpoint.
    /// </summary>
    /// <param name="tickers">List of ticker symbols to load</param>
    /// <param name="startDate">Start date for historical data</param>
    /// <param name="endDate">End date for historical data</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Result with counts and any errors</returns>
    public async Task<TickerLoadResult> LoadHistoricalDataForTickersAsync(
        IEnumerable<string> tickers,
        DateTime startDate,
        DateTime endDate,
        CancellationToken ct = default)
    {
        var tickerList = tickers.ToList();
        var result = new TickerLoadResult { TotalTickers = tickerList.Count };

        _logger.LogInformation("Loading historical data for {Count} tickers from {Start} to {End}",
            tickerList.Count, startDate.ToString("yyyy-MM-dd"), endDate.ToString("yyyy-MM-dd"));

        using var scope = _serviceProvider.CreateScope();
        var eodhd = scope.ServiceProvider.GetRequiredService<EodhdService>();
        var priceRepo = scope.ServiceProvider.GetRequiredService<IPriceRepository>();
        var securityRepo = scope.ServiceProvider.GetRequiredService<ISecurityMasterRepository>();

        foreach (var ticker in tickerList)
        {
            if (ct.IsCancellationRequested)
            {
                result.WasCancelled = true;
                break;
            }

            try
            {
                // Get or create security in SecurityMaster
                var security = await securityRepo.GetByTickerAsync(ticker);
                if (security == null)
                {
                    // Create a new security entry
                    security = await securityRepo.CreateAsync(new SecurityMasterCreateDto
                    {
                        TickerSymbol = ticker.ToUpperInvariant(),
                        IssueName = ticker.ToUpperInvariant() // Will be updated later if needed
                    });
                    _logger.LogInformation("Created new security for {Ticker} with alias {Alias}",
                        LogSanitizer.Sanitize(ticker), security.SecurityAlias);
                }

                // Fetch historical data from EODHD
                var historicalData = await eodhd.GetHistoricalDataAsync(ticker, startDate, endDate, ct);

                if (historicalData.Count == 0)
                {
                    _logger.LogWarning("No historical data returned for {Ticker}", LogSanitizer.Sanitize(ticker));
                    result.Errors.Add($"{ticker}: No data returned");
                    continue;
                }

                // Convert to price DTOs
                var priceDtos = historicalData.Select(record => new PriceCreateDto
                {
                    SecurityAlias = security.SecurityAlias,
                    EffectiveDate = record.ParsedDate,
                    Open = record.Open,
                    High = record.High,
                    Low = record.Low,
                    Close = record.Close,
                    AdjustedClose = record.AdjustedClose,
                    Volume = (long)record.Volume
                }).ToList();

                // Insert prices
                var inserted = await priceRepo.BulkInsertAsync(priceDtos);

                result.TickersProcessed++;
                result.TotalRecordsInserted += inserted;

                _logger.LogInformation("Loaded {Count} price records for {Ticker}",
                    inserted, LogSanitizer.Sanitize(ticker));

                // Small delay to be nice to the API
                await Task.Delay(500, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading data for {Ticker}", LogSanitizer.Sanitize(ticker));
                result.Errors.Add($"{ticker}: {ex.Message}");
            }
        }

        _logger.LogInformation("Ticker load complete: {Processed}/{Total} tickers, {Records} records, {Errors} errors",
            result.TickersProcessed, result.TotalTickers, result.TotalRecordsInserted, result.Errors.Count);

        return result;
    }

    /// <summary>
    /// Optimized parallel backfill for multiple tickers.
    /// Uses parallelism with rate limiting to maximize throughput while respecting EODHD limits.
    /// EODHD allows 1,000 requests/minute - we use 10 concurrent requests with 100ms spacing.
    /// </summary>
    /// <param name="tickers">List of ticker symbols to load</param>
    /// <param name="startDate">Start date for historical data</param>
    /// <param name="endDate">End date for historical data</param>
    /// <param name="maxConcurrency">Max concurrent API requests (default: 10)</param>
    /// <param name="progress">Optional progress callback</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Result with counts and any errors</returns>
    public async Task<TickerLoadResult> BackfillTickersParallelAsync(
        IEnumerable<string> tickers,
        DateTime startDate,
        DateTime endDate,
        int maxConcurrency = 3,
        IProgress<TickerBackfillProgress>? progress = null,
        CancellationToken ct = default)
    {
        // Clamp concurrency to protect Azure SQL Basic (5 DTU / 60 workers)
        maxConcurrency = Math.Clamp(maxConcurrency, 1, 10);

        var tickerList = tickers.ToList();
        var result = new TickerLoadResult { TotalTickers = tickerList.Count };

        _logger.LogInformation(
            "Starting parallel backfill for {Count} tickers from {Start} to {End} with concurrency {Concurrency}",
            tickerList.Count, startDate.ToString("yyyy-MM-dd"), endDate.ToString("yyyy-MM-dd"), maxConcurrency);

        // Use semaphore to limit concurrency
        using var semaphore = new SemaphoreSlim(maxConcurrency);
        var processedCount = 0;
        var insertedCount = 0;
        var errorList = new List<string>();
        var lockObj = new object();

        var tasks = tickerList.Select(async ticker =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                if (ct.IsCancellationRequested) return;

                using var scope = _serviceProvider.CreateScope();
                var eodhd = scope.ServiceProvider.GetRequiredService<EodhdService>();
                var priceRepo = scope.ServiceProvider.GetRequiredService<IPriceRepository>();
                var securityRepo = scope.ServiceProvider.GetRequiredService<ISecurityMasterRepository>();

                // Get or create security in SecurityMaster
                var security = await securityRepo.GetByTickerAsync(ticker);
                if (security == null)
                {
                    security = await securityRepo.CreateAsync(new SecurityMasterCreateDto
                    {
                        TickerSymbol = ticker.ToUpperInvariant(),
                        IssueName = ticker.ToUpperInvariant()
                    });
                    _logger.LogDebug("Created new security for {Ticker}", LogSanitizer.Sanitize(ticker));
                }

                // Fetch historical data from EODHD (1 API call = full history)
                var historicalData = await eodhd.GetHistoricalDataAsync(ticker, startDate, endDate, ct);

                if (historicalData.Count == 0)
                {
                    lock (lockObj)
                    {
                        errorList.Add($"{ticker}: No data returned");
                    }
                    return;
                }

                // Convert to price DTOs
                var priceDtos = historicalData.Select(record => new PriceCreateDto
                {
                    SecurityAlias = security.SecurityAlias,
                    EffectiveDate = record.ParsedDate,
                    Open = record.Open,
                    High = record.High,
                    Low = record.Low,
                    Close = record.Close,
                    AdjustedClose = record.AdjustedClose,
                    Volume = (long)record.Volume
                }).ToList();

                // Insert prices (uses upsert, so duplicates are handled)
                var inserted = await priceRepo.BulkInsertAsync(priceDtos);

                lock (lockObj)
                {
                    processedCount++;
                    insertedCount += inserted;
                }

                _logger.LogDebug("Loaded {Count} records for {Ticker} ({Processed}/{Total})",
                    inserted, LogSanitizer.Sanitize(ticker), processedCount, tickerList.Count);

                // Report progress
                progress?.Report(new TickerBackfillProgress
                {
                    CurrentTicker = ticker,
                    TickersProcessed = processedCount,
                    TotalTickers = tickerList.Count,
                    RecordsInserted = insertedCount,
                    PercentComplete = (int)(processedCount * 100.0 / tickerList.Count)
                });

                // Small delay to stay under rate limits (100ms × 10 concurrent = 1000/min max)
                await Task.Delay(100, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error loading data for {Ticker}", LogSanitizer.Sanitize(ticker));
                lock (lockObj)
                {
                    errorList.Add($"{ticker}: {ex.Message}");
                }
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);

        result.TickersProcessed = processedCount;
        result.TotalRecordsInserted = insertedCount;
        result.Errors = errorList;
        result.WasCancelled = ct.IsCancellationRequested;

        _logger.LogInformation(
            "Parallel backfill complete: {Processed}/{Total} tickers, {Records} records, {Errors} errors",
            result.TickersProcessed, result.TotalTickers, result.TotalRecordsInserted, result.Errors.Count);

        return result;
    }

    /// <summary>
    /// Sync SecurityMaster from the existing Symbols table.
    /// Creates SecurityMaster entries for all active symbols.
    /// </summary>
    public async Task<int> SyncSecurityMasterFromSymbolsAsync(CancellationToken ct = default)
    {
        using var scope = _serviceProvider.CreateScope();
        var securityRepo = scope.ServiceProvider.GetRequiredService<ISecurityMasterRepository>();
        var symbolCache = _serviceProvider.GetService<SymbolCache>();

        if (symbolCache == null || !symbolCache.IsLoaded)
        {
            _logger.LogWarning("SymbolCache not available or not loaded, cannot sync");
            return 0;
        }

        // Get all active symbols from the cache
        var symbols = symbolCache.GetAllActive().ToList();

        _logger.LogInformation("Found {Count} active symbols to sync to SecurityMaster", symbols.Count);

        // Convert to SecurityMaster DTOs
        var dtos = symbols.Select(s => new SecurityMasterCreateDto
        {
            TickerSymbol = s.Symbol,
            IssueName = s.Description ?? s.Symbol,
            MicCode = null, // Populated later by backfill-mic-codes endpoint
            SecurityType = s.Type
        });

        var upserted = await securityRepo.UpsertManyAsync(dtos);

        _logger.LogInformation("Upserted {Count} securities to SecurityMaster", upserted);

        return upserted;
    }

    /// <summary>
    /// Sync SecurityMaster from EODHD exchange symbol list.
    /// Fetches all available securities from EODHD and adds them to SecurityMaster.
    /// </summary>
    /// <param name="exchange">Exchange code (default: "US")</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Number of securities upserted</returns>
    public async Task<EodhdSyncResult> SyncSecurityMasterFromEodhdAsync(
        string exchange = "US",
        CancellationToken ct = default)
    {
        var result = new EodhdSyncResult { Exchange = exchange };

        using var scope = _serviceProvider.CreateScope();
        var eodhd = scope.ServiceProvider.GetRequiredService<EodhdService>();
        var securityRepo = scope.ServiceProvider.GetRequiredService<ISecurityMasterRepository>();

        _logger.LogInformation("Fetching symbol list from EODHD for {Exchange}", exchange);

        // Fetch all symbols from EODHD
        var symbols = await eodhd.GetExchangeSymbolsAsync(exchange, ct);

        if (symbols.Count == 0)
        {
            _logger.LogWarning("No symbols returned from EODHD for {Exchange}", exchange);
            result.ErrorMessage = $"No symbols returned for exchange {exchange}";
            return result;
        }

        result.TotalSymbols = symbols.Count;
        _logger.LogInformation("Retrieved {Count} symbols from EODHD for {Exchange}", symbols.Count, exchange);

        // Convert to SecurityMaster DTOs
        var dtos = symbols.Select(s => new SecurityMasterCreateDto
        {
            TickerSymbol = s.Ticker,
            IssueName = s.Name,
            MicCode = null, // Populated later by backfill-mic-codes endpoint
            SecurityType = s.Type,
            Country = s.Country,
            Currency = s.Currency,
            Isin = s.Isin
        }).ToList();

        _logger.LogInformation("Upserting {Count} securities to SecurityMaster", dtos.Count);

        // Batch upsert to SecurityMaster
        result.SecuritiesUpserted = await securityRepo.UpsertManyAsync(dtos);

        _logger.LogInformation("Successfully upserted {Count} securities from EODHD {Exchange}",
            result.SecuritiesUpserted, exchange);

        return result;
    }

    /// <summary>
    /// Get the last trading day before or on the given date.
    /// </summary>
    private static DateTime GetLastTradingDay(DateTime date)
    {
        // Move back to Friday if it's a weekend
        while (date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday)
        {
            date = date.AddDays(-1);
        }

        // Note: This doesn't account for market holidays
        // A production system would use a holiday calendar
        return date;
    }

    /// <summary>
    /// Get all trading days between two dates (inclusive).
    /// </summary>
    private static List<DateTime> GetTradingDaysBetween(DateTime start, DateTime end)
    {
        var days = new List<DateTime>();
        var current = start;

        while (current <= end)
        {
            if (current.DayOfWeek != DayOfWeek.Saturday &&
                current.DayOfWeek != DayOfWeek.Sunday)
            {
                days.Add(current);
            }
            current = current.AddDays(1);
        }

        return days;
    }
}

/// <summary>
/// Result of a bulk load operation.
/// </summary>
public class BulkLoadResult
{
    public int TotalDays { get; set; }
    public int DaysProcessed { get; set; }
    public bool WasCancelled { get; set; }
    public List<string> Errors { get; set; } = new();
}

/// <summary>
/// Result of loading historical data for specific tickers.
/// </summary>
public class TickerLoadResult
{
    public int TotalTickers { get; set; }
    public int TickersProcessed { get; set; }
    public int TotalRecordsInserted { get; set; }
    public bool WasCancelled { get; set; }
    public List<string> Errors { get; set; } = new();
}

/// <summary>
/// Progress report for ticker backfill operations.
/// </summary>
public class TickerBackfillProgress
{
    public string CurrentTicker { get; set; } = "";
    public int TickersProcessed { get; set; }
    public int TotalTickers { get; set; }
    public int RecordsInserted { get; set; }
    public int PercentComplete { get; set; }
}

/// <summary>
/// Progress report for bulk load operations.
/// </summary>
public class BulkLoadProgress
{
    public DateTime CurrentDate { get; set; }
    public int DaysProcessed { get; set; }
    public int TotalDays { get; set; }
    public int PercentComplete { get; set; }
}

/// <summary>
/// Request body for refreshing prices for a specific date.
/// </summary>
public record RefreshDateRequest
{
    public string? Date { get; init; }
}

/// <summary>
/// Request body for bulk loading historical data.
/// </summary>
public record BulkLoadRequest
{
    public string? StartDate { get; init; }
    public string? EndDate { get; init; }
}

/// <summary>
/// Request body for loading historical data for specific tickers.
/// </summary>
public record TickerLoadRequest
{
    public string[]? Tickers { get; init; }
    public string? StartDate { get; init; }
    public string? EndDate { get; init; }
}

/// <summary>
/// Request body for optimized parallel backfill.
/// </summary>
public record BackfillRequest
{
    public string[]? Tickers { get; init; }
    public string? StartDate { get; init; }
    public string? EndDate { get; init; }
    public int? MaxConcurrency { get; init; }
}

/// <summary>
/// Request body for syncing SecurityMaster from EODHD exchange symbols.
/// </summary>
public record EodhdSyncRequest
{
    public string? Exchange { get; init; }
}

/// <summary>
/// Result of syncing SecurityMaster from EODHD exchange symbols.
/// </summary>
public class EodhdSyncResult
{
    public string Exchange { get; set; } = string.Empty;
    public int TotalSymbols { get; set; }
    public int SecuritiesUpserted { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Result of gap backfill operation.
/// </summary>
public class GapBackfillResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public int TotalGapsFound { get; set; }
    public int TickersProcessed { get; set; }
    public int TotalRecordsInserted { get; set; }
    public int TickersWithNoData { get; set; }
    public int SecuritiesFlagged { get; set; }
    public int RemainingGaps { get; set; }
    public List<string> Errors { get; set; } = new();
}
