using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using StockAnalyzer.Core.Helpers;

namespace StockAnalyzer.Core.Services;

/// <summary>
/// Fetches company descriptions from Wikipedia's REST API as a fallback
/// when financial data providers return null/short descriptions.
///
/// Strategy:
/// 1. Try direct page summary lookup with company name
/// 2. If 404, search Wikipedia and fetch the top result's summary
/// 3. Cache results for 24 hours (descriptions rarely change)
///
/// Rate limiting: Single-concurrency with a minimum 2-second gap between
/// every HTTP request. We treat Wikipedia as a shared public resource and
/// will never hit it harder than a single user casually browsing.
/// Combined with the CompanyBio DB cache (which means each company is
/// fetched at most once, ever), actual Wikipedia traffic is negligible.
/// </summary>
public class WikipediaService
{
    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _cache;
    private readonly ILogger<WikipediaService>? _logger;

    private const string SummaryBaseUrl = "https://en.wikipedia.org/api/rest_v1/page/summary/";
    private const string SearchBaseUrl = "https://en.wikipedia.org/w/api.php";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(24);

    // Rate limiting: one request at a time, minimum 2 seconds between requests.
    // Wikipedia is a shared public resource — we will never hammer it.
    private static readonly SemaphoreSlim _rateLimitSemaphore = new(1, 1);
    private static readonly TimeSpan _minRequestGap = TimeSpan.FromSeconds(2);
    private static DateTime _lastRequestTimeUtc = DateTime.MinValue;

    /// <summary>
    /// Descriptions shorter than this threshold trigger a Wikipedia lookup.
    /// Most financial API descriptions are either null or a single generic sentence (~100 chars).
    /// </summary>
    public const int MinDescriptionLength = 150;

    public WikipediaService(HttpClient httpClient, IMemoryCache cache, ILogger<WikipediaService>? logger = null)
    {
        _httpClient = httpClient;
        _httpClient.Timeout = TimeSpan.FromSeconds(5);
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("StockAnalyzer/3.1 (https://psfordtaurus.com)");
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// Get a company description from Wikipedia.
    /// Returns null if no suitable article is found.
    /// </summary>
    public async Task<string?> GetCompanyDescriptionAsync(string companyName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(companyName))
            return null;

        var cacheKey = $"wiki:desc:{companyName.ToUpperInvariant()}";
        if (_cache.TryGetValue(cacheKey, out string? cached))
            return cached;

        try
        {
            var description = await FetchDescriptionAsync(companyName, ct);

            // Cache even null results to avoid repeated lookups for companies with no article
            _cache.Set(cacheKey, description, CacheDuration);
            return description;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Wikipedia lookup failed for {Company}", LogSanitizer.Sanitize(companyName));
            return null;
        }
    }

    private async Task<string?> FetchDescriptionAsync(string companyName, CancellationToken ct)
    {
        // Step 1: Try direct summary lookup
        var extract = await TryGetSummaryAsync(companyName, ct);
        if (extract != null)
        {
            _logger?.LogDebug("Wikipedia direct hit for {Company}", LogSanitizer.Sanitize(companyName));
            return extract;
        }

        // Step 2: Search and fetch top result
        var searchTitle = await SearchForArticleAsync(companyName, ct);
        if (searchTitle == null)
        {
            _logger?.LogDebug("No Wikipedia article found for {Company}", LogSanitizer.Sanitize(companyName));
            return null;
        }

        extract = await TryGetSummaryAsync(searchTitle, ct);
        if (extract != null)
            _logger?.LogDebug("Wikipedia search hit for {Company} -> {Title}", LogSanitizer.Sanitize(companyName), LogSanitizer.Sanitize(searchTitle));

        return extract;
    }

    /// <summary>
    /// Rate-limited HTTP GET. Acquires the semaphore, enforces the minimum gap
    /// between requests, then makes the call. Ensures we never exceed ~30 req/min
    /// to Wikipedia even under concurrent user load.
    /// </summary>
    private async Task<HttpResponseMessage> RateLimitedGetAsync(string url, CancellationToken ct)
    {
        await _rateLimitSemaphore.WaitAsync(ct);
        try
        {
            var elapsed = DateTime.UtcNow - _lastRequestTimeUtc;
            if (elapsed < _minRequestGap)
            {
                var delay = _minRequestGap - elapsed;
                _logger?.LogDebug("Wikipedia rate limit: waiting {Delay}ms", delay.TotalMilliseconds);
                await Task.Delay(delay, ct);
            }

            var response = await _httpClient.GetAsync(url, ct);
            _lastRequestTimeUtc = DateTime.UtcNow;
            return response;
        }
        finally
        {
            _rateLimitSemaphore.Release();
        }
    }

    private async Task<string?> TryGetSummaryAsync(string title, CancellationToken ct)
    {
        var url = SummaryBaseUrl + Uri.EscapeDataString(title);

        try
        {
            var response = await RateLimitedGetAsync(url, ct);

            if (response.StatusCode == HttpStatusCode.NotFound)
                return null;

            response.EnsureSuccessStatusCode();

            var summary = await response.Content.ReadFromJsonAsync<WikiSummaryResponse>(cancellationToken: ct);

            // Only return extracts that are actual article content (not disambiguation pages)
            if (summary?.Type == "standard" && !string.IsNullOrWhiteSpace(summary.Extract))
                return summary.Extract;

            return null;
        }
        catch (TaskCanceledException)
        {
            return null; // Timeout
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    private async Task<string?> SearchForArticleAsync(string query, CancellationToken ct)
    {
        var url = $"{SearchBaseUrl}?action=query&list=search&srsearch={Uri.EscapeDataString(query)}&format=json&srlimit=1";

        try
        {
            var response = await RateLimitedGetAsync(url, ct);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<WikiSearchResponse>(cancellationToken: ct);
            var firstResult = result?.Query?.Search?.FirstOrDefault();

            return firstResult?.Title;
        }
        catch
        {
            return null;
        }
    }

    // JSON models for Wikipedia API responses

    private class WikiSummaryResponse
    {
        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("extract")]
        public string? Extract { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }
    }

    private class WikiSearchResponse
    {
        [JsonPropertyName("query")]
        public WikiQueryResult? Query { get; set; }
    }

    private class WikiQueryResult
    {
        [JsonPropertyName("search")]
        public List<WikiSearchResult>? Search { get; set; }
    }

    private class WikiSearchResult
    {
        [JsonPropertyName("title")]
        public string? Title { get; set; }
    }
}
