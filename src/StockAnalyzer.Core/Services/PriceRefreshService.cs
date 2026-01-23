using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StockAnalyzer.Core.Data.Entities;

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

                // Skip weekends (no market data)
                if (targetTime.DayOfWeek == DayOfWeek.Saturday ||
                    targetTime.DayOfWeek == DayOfWeek.Sunday)
                {
                    _logger.LogDebug("Skipping price refresh on weekend");
                    continue;
                }

                await RefreshPreviousDayAsync(stoppingToken);
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
    /// </summary>
    private async Task CheckAndBackfillRecentDataAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var priceRepo = scope.ServiceProvider.GetRequiredService<IPriceRepository>();
        var securityRepo = scope.ServiceProvider.GetRequiredService<ISecurityMasterRepository>();

        // Get total price count to check if we have any data
        var totalPrices = await priceRepo.GetTotalCountAsync();
        if (totalPrices == 0)
        {
            _logger.LogInformation("Price database is empty. Use admin endpoint to bulk load historical data.");
            return;
        }

        // Get the most recent date we have prices for
        var activeSecurities = await securityRepo.GetAllActiveAsync();
        if (activeSecurities.Count == 0)
        {
            _logger.LogInformation("No active securities in SecurityMaster. Run security sync first.");
            return;
        }

        // Sample a few securities to find the max date
        var sampleAliases = activeSecurities.Take(10).Select(s => s.SecurityAlias);
        var latestPrices = await priceRepo.GetLatestPricesAsync(sampleAliases);

        if (latestPrices.Count == 0)
        {
            _logger.LogInformation("No prices found for sample securities");
            return;
        }

        var maxDate = latestPrices.Values.Max(p => p.EffectiveDate);
        var yesterday = GetLastTradingDay(DateTime.UtcNow.Date);

        _logger.LogInformation("Most recent price date: {MaxDate}, last trading day: {Yesterday}",
            maxDate.ToString("yyyy-MM-dd"), yesterday.ToString("yyyy-MM-dd"));

        // If we're missing recent days, backfill them
        if (maxDate < yesterday)
        {
            var missingDays = GetTradingDaysBetween(maxDate.AddDays(1), yesterday);
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
    /// Refresh prices for the previous trading day.
    /// Called daily by the background loop.
    /// </summary>
    private async Task RefreshPreviousDayAsync(CancellationToken ct)
    {
        var yesterday = GetLastTradingDay(DateTime.UtcNow.Date);
        await RefreshDateAsync(yesterday, ct);
    }

    /// <summary>
    /// Refresh prices for a specific date.
    /// </summary>
    public async Task RefreshDateAsync(DateTime date, CancellationToken ct)
    {
        _logger.LogInformation("Refreshing prices for {Date}", date.ToString("yyyy-MM-dd"));

        using var scope = _serviceProvider.CreateScope();
        var eodhd = scope.ServiceProvider.GetRequiredService<EodhdService>();
        var priceRepo = scope.ServiceProvider.GetRequiredService<IPriceRepository>();
        var securityRepo = scope.ServiceProvider.GetRequiredService<ISecurityMasterRepository>();

        // Get all active securities
        var securities = await securityRepo.GetAllActiveAsync();
        if (securities.Count == 0)
        {
            _logger.LogWarning("No active securities to refresh");
            return;
        }

        // Build a ticker-to-alias lookup
        var tickerToAlias = securities.ToDictionary(
            s => s.TickerSymbol.ToUpperInvariant(),
            s => s.SecurityAlias);

        // Fetch bulk data from EODHD
        var bulkData = await eodhd.GetBulkEodDataAsync(date, "US", ct);
        if (bulkData.Count == 0)
        {
            _logger.LogWarning("No bulk data returned for {Date}", date.ToString("yyyy-MM-dd"));
            return;
        }

        // Convert to price DTOs, matching only securities we track
        var priceDtos = new List<PriceCreateDto>();
        var matched = 0;
        var unmatched = 0;

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
                    Volume = record.Volume
                });
                matched++;
            }
            else
            {
                unmatched++;
            }
        }

        _logger.LogInformation("Matched {Matched} securities, {Unmatched} not in SecurityMaster",
            matched, unmatched);

        if (priceDtos.Count > 0)
        {
            var inserted = await priceRepo.BulkInsertAsync(priceDtos);
            _logger.LogInformation("Inserted {Count} prices for {Date}",
                inserted, date.ToString("yyyy-MM-dd"));
        }

        _lastRefresh = DateTime.UtcNow;
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
                        ticker, security.SecurityAlias);
                }

                // Fetch historical data from EODHD
                var historicalData = await eodhd.GetHistoricalDataAsync(ticker, startDate, endDate, ct);

                if (historicalData.Count == 0)
                {
                    _logger.LogWarning("No historical data returned for {Ticker}", ticker);
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
                    Volume = record.Volume
                }).ToList();

                // Insert prices
                var inserted = await priceRepo.BulkInsertAsync(priceDtos);

                result.TickersProcessed++;
                result.TotalRecordsInserted += inserted;

                _logger.LogInformation("Loaded {Count} price records for {Ticker}",
                    inserted, ticker);

                // Small delay to be nice to the API
                await Task.Delay(500, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading data for {Ticker}", ticker);
                result.Errors.Add($"{ticker}: {ex.Message}");
            }
        }

        _logger.LogInformation("Ticker load complete: {Processed}/{Total} tickers, {Records} records, {Errors} errors",
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
            Exchange = s.Exchange,
            SecurityType = s.Type
        });

        var upserted = await securityRepo.UpsertManyAsync(dtos);

        _logger.LogInformation("Upserted {Count} securities to SecurityMaster", upserted);

        return upserted;
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
