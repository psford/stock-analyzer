using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
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
/// </summary>
public class AggregatedStockDataService
{
    private readonly IEnumerable<IStockDataProvider> _providers;
    private readonly IMemoryCache _cache;
    private readonly ILogger<AggregatedStockDataService>? _logger;

    // Cache durations
    private static readonly TimeSpan QuoteCacheDuration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan HistoryCacheDuration = TimeSpan.FromHours(1);
    private static readonly TimeSpan SearchCacheDuration = TimeSpan.FromHours(24);

    public AggregatedStockDataService(
        IEnumerable<IStockDataProvider> providers,
        IMemoryCache cache,
        ILogger<AggregatedStockDataService>? logger = null)
    {
        // Order by priority (ascending - lower number = higher priority)
        _providers = providers.OrderBy(p => p.Priority).ToList();
        _cache = cache;
        _logger = logger;

        _logger?.LogInformation(
            "AggregatedStockDataService initialized with providers: {Providers}",
            string.Join(" → ", _providers.Select(p => $"{p.ProviderName}(p{p.Priority})")));
    }

    /// <summary>
    /// Get stock quote and company information with cascading fallback.
    /// </summary>
    public async Task<StockInfo?> GetStockInfoAsync(string symbol)
    {
        var cacheKey = $"quote:{symbol.ToUpper()}";

        if (_cache.TryGetValue(cacheKey, out StockInfo? cached))
        {
            _logger?.LogDebug("Cache hit for {Symbol}", symbol);
            return cached;
        }

        foreach (var provider in _providers.Where(p => p.IsAvailable))
        {
            _logger?.LogDebug("Trying {Provider} for {Symbol}", provider.ProviderName, symbol);

            var result = await provider.GetStockInfoAsync(symbol);
            if (result != null)
            {
                _logger?.LogInformation("Got {Symbol} from {Provider}", symbol, provider.ProviderName);
                _cache.Set(cacheKey, result, QuoteCacheDuration);
                return result;
            }
        }

        _logger?.LogWarning("All providers failed for {Symbol}", symbol);
        return null;
    }

    /// <summary>
    /// Get historical OHLCV data with cascading fallback.
    /// </summary>
    public async Task<HistoricalDataResult?> GetHistoricalDataAsync(string symbol, string period = "1y")
    {
        var cacheKey = $"history:{symbol.ToUpper()}:{period}";

        if (_cache.TryGetValue(cacheKey, out HistoricalDataResult? cached))
        {
            _logger?.LogDebug("Cache hit for {Symbol} history", symbol);
            return cached;
        }

        foreach (var provider in _providers.Where(p => p.IsAvailable))
        {
            _logger?.LogDebug("Trying {Provider} for {Symbol} history", provider.ProviderName, symbol);

            var result = await provider.GetHistoricalDataAsync(symbol, period);
            if (result != null && result.Data.Count > 0)
            {
                _logger?.LogInformation(
                    "Got history for {Symbol} from {Provider} ({Count} points)",
                    symbol, provider.ProviderName, result.Data.Count);
                _cache.Set(cacheKey, result, HistoryCacheDuration);
                return result;
            }
        }

        _logger?.LogWarning("All providers failed for {Symbol} history", symbol);
        return null;
    }

    /// <summary>
    /// Search for symbols by name or ticker with cascading fallback.
    /// </summary>
    public async Task<List<SearchResult>> SearchAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length < 2)
            return new List<SearchResult>();

        var cacheKey = $"search:{query.ToLower()}";

        if (_cache.TryGetValue(cacheKey, out List<SearchResult>? cached))
        {
            _logger?.LogDebug("Cache hit for search '{Query}'", query);
            return cached!;
        }

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

        _logger?.LogDebug("Cache invalidated for {Symbol}", symbol);
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
