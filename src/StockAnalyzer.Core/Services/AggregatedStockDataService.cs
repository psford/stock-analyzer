using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StockAnalyzer.Core.Data.Entities;
using StockAnalyzer.Core.Helpers;
using StockAnalyzer.Core.Models;

namespace StockAnalyzer.Core.Services;

/// <summary>
/// Orchestrates multiple stock data providers with cascading fallback,
/// caching, and rate limit awareness.
///
/// Provider priority:
/// 1. TwelveData (8/min, 800/day) - real-time quotes
/// 2. FMP (250/day) - fundamentals, may have limited symbol coverage
/// 3. Yahoo Finance (fallback) - full coverage but scraping-based
///
/// Search priority:
/// 1. Local SQL database (sub-10ms) - cached symbols from Finnhub
/// 2. API providers (fallback) - only if local DB empty/unavailable
/// </summary>
public class AggregatedStockDataService
{
    private readonly IEnumerable<IStockDataProvider> _providers;
    private readonly IMemoryCache _cache;
    private readonly ILogger<AggregatedStockDataService>? _logger;
    private readonly IServiceScopeFactory? _serviceScopeFactory;

    // Cache durations
    private static readonly TimeSpan QuoteCacheDuration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan HistoryCacheDuration = TimeSpan.FromHours(1);
    private static readonly TimeSpan SearchCacheDuration = TimeSpan.FromHours(24);

    // Track tickers that are already queued for background backfill (to avoid duplicate requests)
    private static readonly ConcurrentDictionary<string, DateTime> _pendingBackfills = new();

    public AggregatedStockDataService(
        IEnumerable<IStockDataProvider> providers,
        IMemoryCache cache,
        ILogger<AggregatedStockDataService>? logger = null,
        IServiceScopeFactory? serviceScopeFactory = null)
    {
        // Order by priority (ascending - lower number = higher priority)
        _providers = providers.OrderBy(p => p.Priority).ToList();
        _cache = cache;
        _logger = logger;
        _serviceScopeFactory = serviceScopeFactory;

        _logger?.LogInformation(
            "AggregatedStockDataService initialized with providers: {Providers}, LocalDB: {LocalDb}",
            string.Join(" → ", _providers.Select(p => $"{p.ProviderName}(p{p.Priority})")),
            serviceScopeFactory != null ? "enabled" : "disabled");
    }

    /// <summary>
    /// Get stock quote and company information with cascading fallback.
    /// </summary>
    public async Task<StockInfo?> GetStockInfoAsync(string symbol)
    {
        var cacheKey = $"quote:{symbol.ToUpper()}";

        if (_cache.TryGetValue(cacheKey, out StockInfo? cached))
        {
            _logger?.LogDebug("Cache hit for {Symbol}", LogSanitizer.Sanitize(symbol));
            return cached;
        }

        foreach (var provider in _providers.Where(p => p.IsAvailable))
        {
            _logger?.LogDebug("Trying {Provider} for {Symbol}", provider.ProviderName, LogSanitizer.Sanitize(symbol));

            var result = await provider.GetStockInfoAsync(symbol);
            if (result != null)
            {
                _logger?.LogInformation("Got {Symbol} from {Provider}", LogSanitizer.Sanitize(symbol), provider.ProviderName);
                _cache.Set(cacheKey, result, QuoteCacheDuration);
                return result;
            }
        }

        _logger?.LogWarning("All providers failed for {Symbol}", LogSanitizer.Sanitize(symbol));
        return null;
    }

    /// <summary>
    /// Get historical OHLCV data.
    /// Priority: 1) Memory cache, 2) Database (pre-loaded prices), 3) API providers (with background backfill)
    /// </summary>
    public async Task<HistoricalDataResult?> GetHistoricalDataAsync(string symbol, string period = "1y")
    {
        var upperSymbol = symbol.ToUpper();
        var cacheKey = $"history:{upperSymbol}:{period}";

        // 1. Check memory cache first
        if (_cache.TryGetValue(cacheKey, out HistoricalDataResult? cached))
        {
            _logger?.LogDebug("Cache hit for {Symbol} history", LogSanitizer.Sanitize(symbol));
            return cached;
        }

        // 2. Check database for pre-loaded prices
        if (_serviceScopeFactory != null)
        {
            var dbResult = await TryGetFromDatabaseAsync(upperSymbol, period);
            if (dbResult != null)
            {
                _logger?.LogInformation(
                    "Got history for {Symbol} from database ({Count} points)",
                    LogSanitizer.Sanitize(symbol), dbResult.Data.Count);
                _cache.Set(cacheKey, dbResult, HistoryCacheDuration);
                return dbResult;
            }
        }

        // 3. Fall back to API providers
        foreach (var provider in _providers.Where(p => p.IsAvailable))
        {
            _logger?.LogDebug("Trying {Provider} for {Symbol} history", provider.ProviderName, LogSanitizer.Sanitize(symbol));

            var result = await provider.GetHistoricalDataAsync(symbol, period);
            if (result != null && result.Data.Count > 0)
            {
                _logger?.LogInformation(
                    "Got history for {Symbol} from {Provider} ({Count} points)",
                    LogSanitizer.Sanitize(symbol), provider.ProviderName, result.Data.Count);
                _cache.Set(cacheKey, result, HistoryCacheDuration);

                // Queue background backfill to database for future requests
                QueueBackgroundBackfill(upperSymbol);

                return result;
            }
        }

        _logger?.LogWarning("All providers failed for {Symbol} history", LogSanitizer.Sanitize(symbol));
        return null;
    }

    /// <summary>
    /// Search for symbols by name or ticker.
    /// Uses local database first (sub-10ms), falls back to API providers only if local DB unavailable.
    /// </summary>
    public async Task<List<SearchResult>> SearchAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length < 1)
            return new List<SearchResult>();

        var cacheKey = $"search:{query.ToLower()}";

        if (_cache.TryGetValue(cacheKey, out List<SearchResult>? cached))
        {
            _logger?.LogDebug("Cache hit for search '{Query}'", LogSanitizer.Sanitize(query));
            return cached!;
        }

        // Try local database first (sub-10ms target)
        if (_serviceScopeFactory != null)
        {
            try
            {
                using var scope = _serviceScopeFactory.CreateScope();
                var symbolRepository = scope.ServiceProvider.GetService<ISymbolRepository>();

                if (symbolRepository != null)
                {
                    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                    var localResults = await symbolRepository.SearchAsync(query, limit: 10);
                    stopwatch.Stop();

                    if (localResults.Count > 0)
                    {
                        _logger?.LogDebug(
                            "Local DB search for '{Query}' returned {Count} results in {Elapsed}ms",
                            LogSanitizer.Sanitize(query), localResults.Count, stopwatch.ElapsedMilliseconds);
                        _cache.Set(cacheKey, localResults, SearchCacheDuration);
                        return localResults;
                    }

                    _logger?.LogDebug("Local DB search for '{Query}' returned no results in {Elapsed}ms",
                        LogSanitizer.Sanitize(query), stopwatch.ElapsedMilliseconds);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Local DB search failed for '{Query}', falling back to API providers",
                    LogSanitizer.Sanitize(query));
            }
        }

        // Fall back to API providers only if local DB unavailable or empty
        foreach (var provider in _providers.Where(p => p.IsAvailable))
        {
            var results = await provider.SearchAsync(query);
            if (results.Count > 0)
            {
                _logger?.LogDebug(
                    "Got {Count} search results from {Provider}",
                    results.Count, provider.ProviderName);
                _cache.Set(cacheKey, results, SearchCacheDuration);
                return results;
            }
        }

        return new List<SearchResult>();
    }

    /// <summary>
    /// Get trending stocks with cascading fallback.
    /// </summary>
    public async Task<List<(string Symbol, string Name)>> GetTrendingStocksAsync(int count = 10)
    {
        foreach (var provider in _providers.Where(p => p.IsAvailable))
        {
            var results = await provider.GetTrendingStocksAsync(count);
            if (results.Count > 0)
            {
                _logger?.LogDebug(
                    "Got {Count} trending from {Provider}",
                    results.Count, provider.ProviderName);
                return results;
            }
        }

        return new List<(string, string)>();
    }

    /// <summary>
    /// Get rate limit status across all providers.
    /// </summary>
    public Dictionary<string, ProviderStatus> GetProviderStatus()
    {
        var status = new Dictionary<string, ProviderStatus>();

        foreach (var provider in _providers)
        {
            var providerStatus = new ProviderStatus
            {
                Name = provider.ProviderName,
                Priority = provider.Priority,
                IsAvailable = provider.IsAvailable
            };

            // Try to get rate limit info if provider exposes it
            if (provider is TwelveDataService twelveData)
            {
                var stats = twelveData.RateLimiter.GetStats();
                providerStatus.MinuteUsed = stats.MinuteUsed;
                providerStatus.DayUsed = stats.DayUsed;
                providerStatus.MaxPerMinute = stats.MaxPerMinute;
                providerStatus.MaxPerDay = stats.MaxPerDay;
            }
            else if (provider is FmpService fmp)
            {
                var stats = fmp.RateLimiter.GetStats();
                providerStatus.MinuteUsed = stats.MinuteUsed;
                providerStatus.DayUsed = stats.DayUsed;
                providerStatus.MaxPerMinute = stats.MaxPerMinute;
                providerStatus.MaxPerDay = stats.MaxPerDay;
            }

            status[provider.ProviderName] = providerStatus;
        }

        return status;
    }

    /// <summary>
    /// Invalidate cache for a specific symbol.
    /// </summary>
    public void InvalidateCache(string symbol)
    {
        var upperSymbol = symbol.ToUpper();
        _cache.Remove($"quote:{upperSymbol}");

        // Remove all period variants for history
        foreach (var period in new[] { "1d", "5d", "1mo", "3mo", "6mo", "1y", "2y", "5y", "10y", "ytd" })
        {
            _cache.Remove($"history:{upperSymbol}:{period}");
        }

        _logger?.LogDebug("Cache invalidated for {Symbol}", LogSanitizer.Sanitize(symbol));
    }

    /// <summary>
    /// Try to get historical data from the database (pre-loaded prices).
    /// </summary>
    private async Task<HistoricalDataResult?> TryGetFromDatabaseAsync(string symbol, string period)
    {
        if (_serviceScopeFactory == null) return null;

        try
        {
            using var scope = _serviceScopeFactory.CreateScope();
            var securityRepo = scope.ServiceProvider.GetService<ISecurityMasterRepository>();
            var priceRepo = scope.ServiceProvider.GetService<IPriceRepository>();

            if (securityRepo == null || priceRepo == null) return null;

            // Look up the security by ticker
            var security = await securityRepo.GetByTickerAsync(symbol);
            if (security == null)
            {
                _logger?.LogDebug("Security {Symbol} not found in SecurityMaster", LogSanitizer.Sanitize(symbol));
                return null;
            }

            // Get the date range for the requested period
            var (startDate, endDate) = GetDateRangeForPeriod(period);

            // Fetch prices from database
            var prices = await priceRepo.GetPricesAsync(security.SecurityAlias, startDate, endDate);

            if (prices.Count == 0)
            {
                _logger?.LogDebug("No prices in database for {Symbol} in period {Period}",
                    LogSanitizer.Sanitize(symbol), period);
                return null;
            }

            // Convert to HistoricalDataResult
            var ohlcvData = prices.Select(p => new OhlcvData
            {
                Date = p.EffectiveDate,
                Open = p.Open,
                High = p.High,
                Low = p.Low,
                Close = p.Close,
                Volume = p.Volume ?? 0,
                AdjustedClose = p.AdjustedClose
            }).OrderBy(d => d.Date).ToList();

            return new HistoricalDataResult
            {
                Symbol = symbol,
                Period = period,
                StartDate = ohlcvData.First().Date,
                EndDate = ohlcvData.Last().Date,
                Data = ohlcvData
            };
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Error fetching {Symbol} from database", LogSanitizer.Sanitize(symbol));
            return null;
        }
    }

    /// <summary>
    /// Convert period string to date range.
    /// </summary>
    private static (DateTime StartDate, DateTime EndDate) GetDateRangeForPeriod(string period)
    {
        var endDate = DateTime.Now.Date;
        var startDate = period.ToLower() switch
        {
            "ytd" => new DateTime(endDate.Year, 1, 1),
            "1d" => endDate.AddDays(-1),
            "5d" => endDate.AddDays(-5),
            "1mo" => endDate.AddMonths(-1),
            "3mo" => endDate.AddMonths(-3),
            "6mo" => endDate.AddMonths(-6),
            "1y" => endDate.AddYears(-1),
            "2y" => endDate.AddYears(-2),
            "5y" => endDate.AddYears(-5),
            "10y" => endDate.AddYears(-10),
            _ => endDate.AddYears(-1)
        };

        return (startDate, endDate);
    }

    /// <summary>
    /// Queue a background task to backfill historical data for a ticker.
    /// Adds the security to SecurityMaster if not present and loads historical prices from EODHD.
    /// </summary>
    private void QueueBackgroundBackfill(string symbol)
    {
        if (_serviceScopeFactory == null) return;

        // Check if already queued recently (within last hour) to avoid duplicate requests
        if (_pendingBackfills.TryGetValue(symbol, out var queuedAt) &&
            DateTime.UtcNow - queuedAt < TimeSpan.FromHours(1))
        {
            _logger?.LogDebug("Backfill for {Symbol} already queued at {QueuedAt}", LogSanitizer.Sanitize(symbol), queuedAt);
            return;
        }

        _pendingBackfills[symbol] = DateTime.UtcNow;

        // Fire-and-forget background task
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _serviceScopeFactory.CreateScope();
                var priceRefreshService = scope.ServiceProvider.GetService<PriceRefreshService>();

                if (priceRefreshService == null)
                {
                    _logger?.LogWarning("PriceRefreshService not available for backfill of {Symbol}", LogSanitizer.Sanitize(symbol));
                    return;
                }

                _logger?.LogInformation("Starting background backfill for {Symbol}", LogSanitizer.Sanitize(symbol));

                // Load 10 years of historical data (matching what we did for S&P 500)
                var startDate = DateTime.Now.AddYears(-10);
                var endDate = DateTime.Now;

                var result = await priceRefreshService.LoadHistoricalDataForTickersAsync(
                    new[] { symbol },
                    startDate,
                    endDate);

                if (result.TotalRecordsInserted > 0)
                {
                    _logger?.LogInformation(
                        "Background backfill complete for {Symbol}: {Count} records inserted",
                        LogSanitizer.Sanitize(symbol), result.TotalRecordsInserted);

                    // Invalidate cache so next request uses database
                    InvalidateCache(symbol);
                }
                else if (result.Errors.Count > 0)
                {
                    _logger?.LogWarning(
                        "Background backfill for {Symbol} had errors: {Errors}",
                        LogSanitizer.Sanitize(symbol), string.Join("; ", result.Errors));
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Background backfill failed for {Symbol}", LogSanitizer.Sanitize(symbol));
            }
            finally
            {
                // Clean up after some time to allow retry
                _ = Task.Delay(TimeSpan.FromHours(2)).ContinueWith(
                    _ => _pendingBackfills.TryRemove(symbol, out DateTime _),
                    TaskScheduler.Default);
            }
        });
    }
}

/// <summary>
/// Status information for a stock data provider.
/// </summary>
public class ProviderStatus
{
    public required string Name { get; init; }
    public required int Priority { get; init; }
    public required bool IsAvailable { get; init; }
    public int? MinuteUsed { get; set; }
    public int? DayUsed { get; set; }
    public int? MaxPerMinute { get; set; }
    public int? MaxPerDay { get; set; }
}
