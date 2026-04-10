using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using StockAnalyzer.Core.Data.Entities;
using StockAnalyzer.Core.Helpers;
using StockAnalyzer.Core.Models;

namespace StockAnalyzer.Core.Services;

/// <summary>
/// Orchestrates multiple stock data providers with parallel fetch and per-field compositing,
/// caching, and rate limit awareness.
///
/// Stock quote fetching: All available providers are called simultaneously. Fields are composited
/// per provider priority matrix—each field is populated by the highest-priority provider that
/// returns a non-null value. Identity fields (Symbol, ShortName, LongName) come from primary
/// provider only to avoid mixing names.
///
/// Historical and search data still use sequential fallback for compatibility.
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
    /// <summary>
    /// Result of fetching data from the database, including gap information.
    /// </summary>
    private record DatabaseFetchResult(HistoricalDataResult Result, int GapDays, DateTime GapStartDate, int SecurityAlias);

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

    // Coalesce concurrent cache misses for the same key (stampede prevention)
    private static readonly ConcurrentDictionary<string, Task<HistoricalDataResult?>> _inflight = new();

    // Per-symbol cancellation tokens for cache eviction (covers all key patterns including custom date ranges)
    private static readonly ConcurrentDictionary<string, CancellationTokenSource> _symbolCacheTokens = new();

    /// <summary>
    /// Priority matrix mapping field groups to ordered provider names.
    /// Each group's array represents provider priority: first provider with a non-null value wins.
    /// </summary>
    private static readonly Dictionary<string, string[]> FieldPriorityMatrix = new()
    {
        ["Price"] = ["TwelveData", "FMP", "Yahoo"],
        ["Volume"] = ["TwelveData", "FMP", "Yahoo"],
        ["MarketCapPe"] = ["FMP", "Yahoo"],
        ["ForwardValuation"] = ["Yahoo"],
        ["Dividend"] = ["Yahoo"],
        ["FiftyTwoWeek"] = ["TwelveData", "FMP", "Yahoo"],
        ["MovingAverages"] = ["FMP", "Yahoo"],
        ["CompanyInfo"] = ["TwelveData", "FMP", "Yahoo"]
    };

    /// <summary>
    /// Maps field groups to their property names.
    /// Note: IpoDate is in CompanyInfo group despite its model location near MovingAverages (lines 54-56).
    /// </summary>
    private static readonly Dictionary<string, string[]> FieldGroupMembers = new()
    {
        ["Price"] = ["CurrentPrice", "PreviousClose", "Open", "DayHigh", "DayLow"],
        ["Volume"] = ["Volume", "AverageVolume"],
        ["MarketCapPe"] = ["MarketCap", "PeRatio"],
        ["ForwardValuation"] = ["ForwardPeRatio", "PegRatio", "PriceToBook"],
        ["Dividend"] = ["DividendYield", "DividendRate"],
        ["FiftyTwoWeek"] = ["FiftyTwoWeekHigh", "FiftyTwoWeekLow"],
        ["MovingAverages"] = ["FiftyDayAverage", "TwoHundredDayAverage"],
        ["CompanyInfo"] = ["Sector", "Industry", "Website", "Country", "Currency", "Exchange", "MicCode", "ExchangeName", "Description", "FullTimeEmployees", "IpoDate", "Isin", "Cusip", "Sedol"]
    };

    /// <summary>
    /// Cached PropertyInfo objects for all StockInfo fields (avoids per-lookup reflection).
    /// Built once at class initialization.
    /// </summary>
    private static readonly Dictionary<string, System.Reflection.PropertyInfo?> _propertyInfoCache =
        typeof(StockInfo).GetProperties()
            .ToDictionary(p => p.Name, p => (System.Reflection.PropertyInfo?)p);

    /// <summary>
    /// Inverted field-to-group mapping (field name → group name).
    /// Eliminates linear scan in GetCompositeFieldValue when locating a field's group.
    /// </summary>
    private static readonly Dictionary<string, string> _fieldToGroupMap =
        FieldGroupMembers.SelectMany(g => g.Value.Select(field => (field, group: g.Key)))
            .ToDictionary(x => x.field, x => x.group);

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
    /// Composite StockInfo from multiple provider results using field priority matrix.
    /// Identity fields (Symbol, ShortName, LongName) come from primary provider (first non-null by Price priority).
    /// Other fields are composited: per field group, take highest-priority provider's non-null value.
    /// Returns null if all provider results are null.
    /// </summary>
    private static StockInfo? CompositeStockInfo(Dictionary<string, StockInfo?> providerResults)
    {
        // If all providers are null, return null
        if (providerResults.Values.All(v => v == null))
            return null;

        // Find primary provider (first by Price priority that has non-null result)
        StockInfo? primaryResult = null;
        foreach (var providerName in FieldPriorityMatrix["Price"])
        {
            if (providerResults.TryGetValue(providerName, out var result) && result != null)
            {
                primaryResult = result;
                break;
            }
        }

        // If no primary provider found, use any non-null result
        if (primaryResult == null)
        {
            primaryResult = providerResults.Values.First(v => v != null);
        }

        // Helper: Get composite value for a field from highest-priority provider
        // Uses cached PropertyInfo and field-to-group mappings (no reflection per-lookup)
        T? GetCompositeFieldValue<T>(string fieldName)
        {
            // Find which group this field belongs to (O(1) lookup via inverted map)
            if (!_fieldToGroupMap.TryGetValue(fieldName, out var groupName))
                return default;

            // Look up priority list for this group
            if (!FieldPriorityMatrix.TryGetValue(groupName, out var priorityList))
                return default;

            // Get cached PropertyInfo (O(1) instead of reflection)
            if (!_propertyInfoCache.TryGetValue(fieldName, out var prop) || prop == null)
                return default;

            // Try each provider in priority order
            foreach (var providerName in priorityList)
            {
                if (providerResults.TryGetValue(providerName, out var provider) && provider != null)
                {
                    var value = prop.GetValue(provider);
                    if (value != null)
                        return (T)value;
                }
            }

            return default;
        }

        // Build composite using record copy with all fields set from highest-priority providers
        var composite = primaryResult! with
        {
            // Price fields
            CurrentPrice = GetCompositeFieldValue<decimal?>("CurrentPrice") ?? primaryResult!.CurrentPrice,
            PreviousClose = GetCompositeFieldValue<decimal?>("PreviousClose") ?? primaryResult!.PreviousClose,
            Open = GetCompositeFieldValue<decimal?>("Open") ?? primaryResult!.Open,
            DayHigh = GetCompositeFieldValue<decimal?>("DayHigh") ?? primaryResult!.DayHigh,
            DayLow = GetCompositeFieldValue<decimal?>("DayLow") ?? primaryResult!.DayLow,

            // Volume fields
            Volume = GetCompositeFieldValue<long?>("Volume") ?? primaryResult!.Volume,
            AverageVolume = GetCompositeFieldValue<long?>("AverageVolume") ?? primaryResult!.AverageVolume,

            // MarketCap & P/E
            MarketCap = GetCompositeFieldValue<decimal?>("MarketCap") ?? primaryResult!.MarketCap,
            PeRatio = GetCompositeFieldValue<decimal?>("PeRatio") ?? primaryResult!.PeRatio,

            // Forward valuation
            ForwardPeRatio = GetCompositeFieldValue<decimal?>("ForwardPeRatio") ?? primaryResult!.ForwardPeRatio,
            PegRatio = GetCompositeFieldValue<decimal?>("PegRatio") ?? primaryResult!.PegRatio,
            PriceToBook = GetCompositeFieldValue<decimal?>("PriceToBook") ?? primaryResult!.PriceToBook,

            // Dividend
            DividendYield = GetCompositeFieldValue<decimal?>("DividendYield") ?? primaryResult!.DividendYield,
            DividendRate = GetCompositeFieldValue<decimal?>("DividendRate") ?? primaryResult!.DividendRate,

            // 52-week
            FiftyTwoWeekHigh = GetCompositeFieldValue<decimal?>("FiftyTwoWeekHigh") ?? primaryResult!.FiftyTwoWeekHigh,
            FiftyTwoWeekLow = GetCompositeFieldValue<decimal?>("FiftyTwoWeekLow") ?? primaryResult!.FiftyTwoWeekLow,

            // Moving averages
            FiftyDayAverage = GetCompositeFieldValue<decimal?>("FiftyDayAverage") ?? primaryResult!.FiftyDayAverage,
            TwoHundredDayAverage = GetCompositeFieldValue<decimal?>("TwoHundredDayAverage") ?? primaryResult!.TwoHundredDayAverage,

            // Company info
            Sector = GetCompositeFieldValue<string?>("Sector") ?? primaryResult!.Sector,
            Industry = GetCompositeFieldValue<string?>("Industry") ?? primaryResult!.Industry,
            Website = GetCompositeFieldValue<string?>("Website") ?? primaryResult!.Website,
            Country = GetCompositeFieldValue<string?>("Country") ?? primaryResult!.Country,
            Currency = GetCompositeFieldValue<string?>("Currency") ?? primaryResult!.Currency,
            Exchange = GetCompositeFieldValue<string?>("Exchange") ?? primaryResult!.Exchange,
            MicCode = GetCompositeFieldValue<string?>("MicCode") ?? primaryResult!.MicCode,
            ExchangeName = GetCompositeFieldValue<string?>("ExchangeName") ?? primaryResult!.ExchangeName,
            Description = GetCompositeFieldValue<string?>("Description") ?? primaryResult!.Description,
            FullTimeEmployees = GetCompositeFieldValue<int?>("FullTimeEmployees") ?? primaryResult!.FullTimeEmployees,
            IpoDate = GetCompositeFieldValue<string?>("IpoDate") ?? primaryResult!.IpoDate,
            Isin = GetCompositeFieldValue<string?>("Isin") ?? primaryResult!.Isin,
            Cusip = GetCompositeFieldValue<string?>("Cusip") ?? primaryResult!.Cusip,
            Sedol = GetCompositeFieldValue<string?>("Sedol") ?? primaryResult!.Sedol
        };

        return composite;
    }

    /// <summary>
    /// Get stock quote and company information with parallel fetch and per-field compositing.
    /// All available providers are called simultaneously. Fields are composited per priority matrix.
    /// Identity fields (Symbol, ShortName, LongName) come from primary provider only.
    /// </summary>
    public async Task<StockInfo?> GetStockInfoAsync(string symbol)
    {
        var cacheKey = $"quote:{symbol.ToUpper()}";

        if (_cache.TryGetValue(cacheKey, out StockInfo? cached))
        {
            _logger?.LogDebug("Cache hit for {Symbol}", LogSanitizer.Sanitize(symbol));
            return cached;
        }

        // Filter available providers
        var availableProviders = _providers.Where(p => p.IsAvailable).ToList();
        if (availableProviders.Count == 0)
        {
            _logger?.LogWarning("No providers available for {Symbol}", LogSanitizer.Sanitize(symbol));
            return null;
        }

        // Parallel fetch: call all providers simultaneously, each with its own error handling
        var tasks = availableProviders.Select(async provider =>
        {
            try
            {
                var result = await provider.GetStockInfoAsync(symbol);
                return (provider.ProviderName, Result: result);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Provider {Provider} failed for {Symbol}",
                    provider.ProviderName, LogSanitizer.Sanitize(symbol));
                return (provider.ProviderName, Result: (StockInfo?)null);
            }
        }).ToList();

        var results = await Task.WhenAll(tasks);
        var providerResults = results.ToDictionary(r => r.ProviderName, r => r.Result);

        // Composite the results
        var composite = CompositeStockInfo(providerResults);

        if (composite != null)
        {
            // Log which providers contributed
            var contributingProviders = providerResults.Where(kv => kv.Value != null).Select(kv => kv.Key).ToList();
            _logger?.LogInformation("Composited {Symbol} from providers: {Providers}",
                LogSanitizer.Sanitize(symbol), string.Join(", ", contributingProviders));

            _cache.Set(cacheKey, composite, SymbolCacheOptions(symbol.ToUpper(), QuoteCacheDuration));
            return composite;
        }

        _logger?.LogWarning("All providers failed for {Symbol}", LogSanitizer.Sanitize(symbol));
        return null;
    }

    /// <summary>
    /// Get historical OHLCV data.
    /// Priority: 1) Memory cache, 2) Coalesced in-flight request, 3) Database, 4) API providers
    /// </summary>
    public async Task<HistoricalDataResult?> GetHistoricalDataAsync(string symbol, string period = "1y")
    {
        var upperSymbol = symbol.ToUpper();
        var cacheKey = $"history:{upperSymbol}:{period}";

        // 1. Check memory cache first (fast path)
        if (_cache.TryGetValue(cacheKey, out HistoricalDataResult? cached))
        {
            _logger?.LogDebug("Cache hit for {Symbol} history", LogSanitizer.Sanitize(symbol));
            return cached;
        }

        // 2. Coalesce concurrent misses — if another request is already fetching this key, share its task
        var task = _inflight.GetOrAdd(cacheKey, _ => FetchHistoricalDataCoreAsync(upperSymbol, period, cacheKey));

        try
        {
            return await task;
        }
        finally
        {
            // Remove from inflight after completion so future requests re-check cache
            _inflight.TryRemove(cacheKey, out _);
        }
    }

    /// <summary>
    /// Get historical OHLCV data for a custom date range.
    /// Used when explicit from/to dates are provided instead of a period string.
    /// </summary>
    public async Task<HistoricalDataResult?> GetHistoricalDataAsync(string symbol, DateTime from, DateTime to)
    {
        var upperSymbol = symbol.ToUpper();
        var cacheKey = $"history:{upperSymbol}:{from:yyyyMMdd}:{to:yyyyMMdd}";

        if (_cache.TryGetValue(cacheKey, out HistoricalDataResult? cached))
            return cached;

        var task = _inflight.GetOrAdd(cacheKey, _ => FetchHistoricalDataCoreAsync(upperSymbol, from, to, cacheKey));

        try
        {
            return await task;
        }
        finally
        {
            _inflight.TryRemove(cacheKey, out _);
        }
    }

    /// <summary>
    /// Core fetch logic for historical data (DB → API providers).
    /// Called via _inflight coalescing so concurrent requests for the same key share one task.
    /// </summary>
    private async Task<HistoricalDataResult?> FetchHistoricalDataCoreAsync(string upperSymbol, string period, string cacheKey)
    {
        var (startDate, endDate) = GetDateRangeForPeriod(period);
        return await FetchHistoricalDataCoreAsync(upperSymbol, startDate, endDate, cacheKey);
    }

    private async Task<HistoricalDataResult?> FetchHistoricalDataCoreAsync(string upperSymbol, DateTime startDate, DateTime endDate, string cacheKey)
    {
        // Check database for pre-loaded prices
        if (_serviceScopeFactory != null)
        {
            var dbFetchResult = await TryGetFromDatabaseAsync(upperSymbol, startDate, endDate);
            if (dbFetchResult != null)
            {
                // Case 1: Fresh data (gap <= 0) — return DB data as-is
                if (dbFetchResult.GapDays <= 0)
                {
                    var result = AdjustForSplits(dbFetchResult.Result);
                    _logger?.LogInformation(
                        "Got fresh history for {Symbol} from database ({Count} points)",
                        LogSanitizer.Sanitize(upperSymbol), result.Data.Count);
                    _cache.Set(cacheKey, result, SymbolCacheOptions(upperSymbol, HistoryCacheDuration));
                    return result;
                }

                // Case 2: Stale data (gap > 0) — attempt gap-fill
                _logger?.LogDebug(
                    "Attempting gap-fill for {Symbol} ({GapDays} trading days)",
                    LogSanitizer.Sanitize(upperSymbol), dbFetchResult.GapDays);

                // Calculate the smallest period string that covers the gap
                var gapCalendarDays = (endDate - dbFetchResult.GapStartDate).TotalDays;
                var gapPeriod = gapCalendarDays switch
                {
                    <= 30 => "1mo",
                    <= 90 => "3mo",
                    <= 180 => "6mo",
                    <= 365 => "1y",
                    _ => "2y"
                };

                // Try gap-fill from API providers
                var gapFillSucceeded = false;
                var gapFillData = new List<OhlcvData>();

                foreach (var provider in _providers.Where(p => p.IsAvailable))
                {
                    _logger?.LogDebug("Trying gap-fill from {Provider} for {Symbol} ({Period})",
                        provider.ProviderName, LogSanitizer.Sanitize(upperSymbol), gapPeriod);

                    var providerResult = await provider.GetHistoricalDataAsync(upperSymbol, gapPeriod);
                    if (providerResult != null && providerResult.Data.Count > 0)
                    {
                        // Filter to dates after gap start date
                        gapFillData = providerResult.Data
                            .Where(d => d.Date > dbFetchResult.GapStartDate)
                            .ToList();

                        if (gapFillData.Count > 0)
                        {
                            gapFillSucceeded = true;
                            _logger?.LogDebug("Gap-fill succeeded from {Provider}: {Count} records",
                                provider.ProviderName, gapFillData.Count);
                            break; // Use first successful provider
                        }
                    }
                }

                if (gapFillSucceeded)
                {
                    // Merge: concatenate, deduplicate by date (prefer API), sort ascending
                    var mergedData = new Dictionary<DateTime, OhlcvData>();

                    // Add DB data first
                    foreach (var row in dbFetchResult.Result.Data)
                    {
                        mergedData[row.Date] = row;
                    }

                    // Overwrite with API gap-fill data (API is fresher)
                    foreach (var row in gapFillData)
                    {
                        mergedData[row.Date] = row;
                    }

                    var sortedMergedData = mergedData.Values.OrderBy(d => d.Date).ToList();

                    // Persist gap-filled prices to DB
                    try
                    {
                        using var persistScope = _serviceScopeFactory.CreateScope();
                        var priceRepo = persistScope.ServiceProvider.GetService<IPriceRepository>();
                        if (priceRepo != null)
                        {
                            var priceDtos = gapFillData.Select(d => new PriceCreateDto
                            {
                                SecurityAlias = dbFetchResult.SecurityAlias,
                                EffectiveDate = d.Date,
                                Open = d.Open,
                                High = d.High,
                                Low = d.Low,
                                Close = d.Close,
                                Volume = d.Volume,
                                AdjustedClose = d.AdjustedClose
                            }).ToList();

                            await priceRepo.BulkInsertAsync(priceDtos);
                            _logger?.LogDebug("Persisted {Count} gap-filled prices to database for {Symbol}",
                                priceDtos.Count, LogSanitizer.Sanitize(upperSymbol));
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "Failed to persist gap-filled prices for {Symbol}",
                            LogSanitizer.Sanitize(upperSymbol));
                    }

                    // Apply split adjustment and cache merged result
                    var mergedResult = dbFetchResult.Result with { Data = sortedMergedData };
                    var adjustedResult = AdjustForSplits(mergedResult);
                    _logger?.LogInformation(
                        "Got history for {Symbol} from database + gap-fill ({Count} points)",
                        LogSanitizer.Sanitize(upperSymbol), adjustedResult.Data.Count);
                    _cache.Set(cacheKey, adjustedResult, SymbolCacheOptions(upperSymbol, HistoryCacheDuration));
                    return adjustedResult;
                }
                else
                {
                    // API gap-fill failed — return DB-only data (partial is better than nothing)
                    _logger?.LogWarning(
                        "API gap-fill failed for {Symbol}, returning database-only data ({Count} points)",
                        LogSanitizer.Sanitize(upperSymbol), dbFetchResult.Result.Data.Count);
                    var result = AdjustForSplits(dbFetchResult.Result);
                    _cache.Set(cacheKey, result, SymbolCacheOptions(upperSymbol, HistoryCacheDuration));
                    return result;
                }
            }
        }

        // Fall back to API providers — synthesize a period that covers the requested range
        var years = (int)Math.Ceiling((endDate - startDate).TotalDays / 365.25);
        var fallbackPeriod = years switch
        {
            <= 0 => "1mo",
            1 => "1y",
            <= 2 => "2y",
            <= 5 => "5y",
            _ => "10y"
        };

        foreach (var provider in _providers.Where(p => p.IsAvailable))
        {
            _logger?.LogDebug("Trying {Provider} for {Symbol} history", provider.ProviderName, LogSanitizer.Sanitize(upperSymbol));

            var result = await provider.GetHistoricalDataAsync(upperSymbol, fallbackPeriod);
            if (result != null && result.Data.Count > 0)
            {
                // Filter to requested date range (provider may return broader data)
                var filteredData = result.Data
                    .Where(d => d.Date >= startDate && d.Date <= endDate)
                    .ToList();
                if (filteredData.Count > 0)
                    result = result with { Data = filteredData };

                result = AdjustForSplits(result);
                _logger?.LogInformation(
                    "Got history for {Symbol} from {Provider} ({Count} points)",
                    LogSanitizer.Sanitize(upperSymbol), provider.ProviderName, result.Data.Count);
                _cache.Set(cacheKey, result, SymbolCacheOptions(upperSymbol, HistoryCacheDuration));

                // Queue background backfill to database for future requests
                QueueBackgroundBackfill(upperSymbol);

                return result;
            }
        }

        _logger?.LogWarning("All providers failed for {Symbol} history", LogSanitizer.Sanitize(upperSymbol));
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
    /// Get cache entry options tied to a per-symbol cancellation token.
    /// When InvalidateCache is called, the token is cancelled and ALL entries
    /// for that symbol are evicted — including custom date range keys.
    /// </summary>
    private MemoryCacheEntryOptions SymbolCacheOptions(string upperSymbol, TimeSpan expiration)
    {
        var cts = _symbolCacheTokens.GetOrAdd(upperSymbol, _ => new CancellationTokenSource());
        return new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(expiration)
            .AddExpirationToken(new CancellationChangeToken(cts.Token));
    }

    /// <summary>
    /// Invalidate cache for a specific symbol.
    /// Cancels the per-symbol token, evicting all cache entries (quotes, history periods, custom date ranges).
    /// </summary>
    public void InvalidateCache(string symbol)
    {
        var upperSymbol = symbol.ToUpper();

        // Cancel the per-symbol token — evicts ALL cache entries registered with it
        if (_symbolCacheTokens.TryRemove(upperSymbol, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }

        _logger?.LogDebug("Cache invalidated for {Symbol}", LogSanitizer.Sanitize(symbol));
    }

    /// <summary>
    /// Try to get historical data from the database (pre-loaded prices).
    /// Returns gap information for targeted gap-fill retrieval from API.
    /// </summary>
    private async Task<DatabaseFetchResult?> TryGetFromDatabaseAsync(string symbol, DateTime startDate, DateTime endDate, string? period = null)
    {
        if (_serviceScopeFactory == null) return null;

        try
        {
            using var scope = _serviceScopeFactory.CreateScope();
            var securityRepo = scope.ServiceProvider.GetService<ISecurityMasterRepository>();
            var priceRepo = scope.ServiceProvider.GetService<IPriceRepository>();

            if (securityRepo == null || priceRepo == null) return null;

            var security = await securityRepo.GetByTickerAsync(symbol);
            if (security == null)
            {
                _logger?.LogDebug("Security {Symbol} not found in SecurityMaster", LogSanitizer.Sanitize(symbol));
                return null;
            }

            var prices = await priceRepo.GetPricesAsync(security.SecurityAlias, startDate, endDate);

            if (prices.Count == 0)
            {
                _logger?.LogDebug("No prices in database for {Symbol} ({Start} to {End})",
                    LogSanitizer.Sanitize(symbol), startDate.ToString("yyyy-MM-dd"), endDate.ToString("yyyy-MM-dd"));
                return null;
            }

            // Check if database data covers enough of the requested range (sparsity check).
            // Reject genuinely empty DB data and fall back to full API cascade.
            var requestedDays = (endDate - startDate).TotalDays;
            var expectedTradingDays = requestedDays * 252.0 / 365.25; // ~252 trading days per year
            if (expectedTradingDays > 30 && prices.Count < expectedTradingDays * 0.20)
            {
                _logger?.LogInformation(
                    "Database has sparse data for {Symbol} ({Count} points for {Expected:F0} expected trading days), falling through to API",
                    LogSanitizer.Sanitize(symbol), prices.Count, expectedTradingDays);
                return null;
            }

            // Detect gap: calculate trading days missing between latest price and requested end date.
            // If gap > 0, attempt gap-fill; if gap <= 0, data is fresh.
            var latestPrice = prices.Max(p => p.EffectiveDate);
            var gapCalendarDays = (endDate - latestPrice).TotalDays;
            var gapDays = (int)(gapCalendarDays * 252.0 / 365.25);
            var gapStartDate = latestPrice.AddDays(1);

            _logger?.LogDebug(
                "Database data for {Symbol} gap detection: latest: {Latest:yyyy-MM-dd}, requested end: {End:yyyy-MM-dd}, gap: {GapDays} trading days",
                LogSanitizer.Sanitize(symbol), latestPrice, endDate, gapDays);

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

            var result = new HistoricalDataResult
            {
                Symbol = symbol,
                Period = period ?? "custom",
                StartDate = ohlcvData.First().Date,
                EndDate = ohlcvData.Last().Date,
                Data = ohlcvData
            };

            return new DatabaseFetchResult(result, gapDays, gapStartDate, security.SecurityAlias);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Error fetching {Symbol} from database", LogSanitizer.Sanitize(symbol));
            return null;
        }
    }

    /// <summary>
    /// Adjust OHLC prices for stock splits using the AdjustedClose ratio.
    /// For each data point where AdjustedClose differs from Close, computes
    /// the split ratio and applies it to Open/High/Low/Close.
    /// This ensures charts, returns, and technical indicators are correct
    /// for stocks that have undergone splits (e.g., NVDA 10:1 in June 2024).
    /// </summary>
    private static HistoricalDataResult AdjustForSplits(HistoricalDataResult result)
    {
        var adjustedData = result.Data.Select(d =>
        {
            if (d.AdjustedClose == null || d.AdjustedClose == 0 || d.Close == 0)
                return d;
            var ratio = d.AdjustedClose.Value / d.Close;
            if (Math.Abs(ratio - 1.0m) < 0.0001m)
                return d; // No meaningful adjustment needed
            return d with
            {
                Open = Math.Round(d.Open * ratio, 4),
                High = Math.Round(d.High * ratio, 4),
                Low = Math.Round(d.Low * ratio, 4),
                Close = d.AdjustedClose.Value
            };
        }).ToList();

        return result with { Data = adjustedData };
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
            "mtd" => new DateTime(endDate.Year, endDate.Month, 1),
            "1d" => endDate.AddDays(-1),
            "5d" => endDate.AddDays(-5),
            "1mo" => endDate.AddMonths(-1),
            "3mo" => endDate.AddMonths(-3),
            "6mo" => endDate.AddMonths(-6),
            "1y" => endDate.AddYears(-1),
            "2y" => endDate.AddYears(-2),
            "5y" => endDate.AddYears(-5),
            "10y" => endDate.AddYears(-10),
            "15y" => endDate.AddYears(-15),
            "20y" => endDate.AddYears(-20),
            "30y" => endDate.AddYears(-30),
            "max" or "all" => new DateTime(1900, 1, 1),
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
                        LogSanitizer.Sanitize(symbol), LogSanitizer.Sanitize(string.Join("; ", result.Errors)));
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
