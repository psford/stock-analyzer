using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EodhdLoader.Services;

/// <summary>
/// HTTP client for calling Stock Analyzer admin API endpoints.
/// </summary>
public class StockAnalyzerApiClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ConfigurationService _config;
    private HttpClient? _httpClient;
    private string _currentBaseUrl = "";
    private TargetEnvironment _currentEnvironment = TargetEnvironment.Local;

    public StockAnalyzerApiClient(IHttpClientFactory httpClientFactory, ConfigurationService config)
    {
        _httpClientFactory = httpClientFactory;
        _config = config;
        UpdateHttpClient();
    }

    public TargetEnvironment CurrentEnvironment
    {
        get => _currentEnvironment;
        set
        {
            if (_currentEnvironment != value)
            {
                _currentEnvironment = value;
                UpdateHttpClient();
            }
        }
    }

    private void UpdateHttpClient()
    {
        var baseUrl = _config.GetApiUrl(_currentEnvironment);

        // Only create new client if URL actually changed
        if (string.IsNullOrEmpty(baseUrl))
        {
            _httpClient = null;
            _currentBaseUrl = "";
            return;
        }

        if (_currentBaseUrl != baseUrl)
        {
            _httpClient = _httpClientFactory.CreateClient();
            _httpClient.BaseAddress = new Uri(baseUrl);
            _currentBaseUrl = baseUrl;
        }
    }

    /// <summary>
    /// Calls POST /api/admin/prices/load-tickers to backfill historical data for specific tickers.
    /// </summary>
    public async Task<ApiResponse<LoadTickersResult>> LoadTickersAsync(
        List<string> tickers,
        DateTime fromDate,
        DateTime? toDate = null,
        CancellationToken ct = default)
    {
        if (_httpClient == null)
            return new ApiResponse<LoadTickersResult> { Success = false, Error = "HttpClient not configured" };

        var request = new
        {
            Tickers = tickers.ToArray(),
            StartDate = fromDate.ToString("yyyy-MM-dd"),
            EndDate = (toDate ?? fromDate).ToString("yyyy-MM-dd")
        };

        var response = await _httpClient.PostAsJsonAsync("/api/admin/prices/load-tickers", request, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<LoadTickersResult>(cancellationToken: ct);
        return new ApiResponse<LoadTickersResult>
        {
            Success = true,
            Data = result
        };
    }

    /// <summary>
    /// Calls POST /api/admin/prices/refresh-date to refresh prices for a specific date.
    /// </summary>
    public async Task<ApiResponse<RefreshDateResult>> RefreshDateAsync(
        DateTime date,
        CancellationToken ct = default)
    {
        if (_httpClient == null)
            return new ApiResponse<RefreshDateResult> { Success = false, Error = "HttpClient not configured" };

        var request = new { date = date.ToString("yyyy-MM-dd") };

        var response = await _httpClient.PostAsJsonAsync("/api/admin/prices/refresh-date", request, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<RefreshDateResult>(cancellationToken: ct);
        return new ApiResponse<RefreshDateResult>
        {
            Success = true,
            Data = result
        };
    }

    /// <summary>
    /// Calls POST /api/admin/prices/sync-securities to sync SecurityMaster.
    /// </summary>
    public async Task<ApiResponse<SecuritySyncApiResult>> SyncSecuritiesAsync(CancellationToken ct = default)
    {
        if (_httpClient == null)
            return new ApiResponse<SecuritySyncApiResult> { Success = false, Error = "HttpClient not configured" };

        var response = await _httpClient.PostAsync("/api/admin/prices/sync-securities", null, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<SecuritySyncApiResult>(cancellationToken: ct);
        return new ApiResponse<SecuritySyncApiResult>
        {
            Success = true,
            Data = result
        };
    }

    /// <summary>
    /// Tests connectivity to the API.
    /// </summary>
    public async Task<bool> TestConnectionAsync(CancellationToken ct = default)
    {
        if (_httpClient == null)
            return false;

        try
        {
            var response = await _httpClient.GetAsync("/health", ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Gets the distinct dates that have price data in the specified range.
    /// Used by Boris to determine which dates are missing.
    /// </summary>
    public async Task<CoverageDatesResult> GetCoverageDatesAsync(
        DateTime startDate,
        DateTime endDate,
        CancellationToken ct = default)
    {
        if (_httpClient == null)
            return new CoverageDatesResult();

        var url = $"/api/admin/prices/coverage-dates?startDate={startDate:yyyy-MM-dd}&endDate={endDate:yyyy-MM-dd}";
        var response = await _httpClient.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<CoverageDatesResult>(cancellationToken: ct);
        return result ?? new CoverageDatesResult();
    }

    /// <summary>
    /// Calls GET /api/admin/prices/holidays/analyze to analyze holidays missing price data.
    /// </summary>
    public async Task<HolidayAnalysisApiResult> AnalyzeHolidaysAsync(CancellationToken ct = default)
    {
        if (_httpClient == null)
            return new HolidayAnalysisApiResult { Success = false, Error = "HttpClient not configured" };

        try
        {
            var response = await _httpClient.GetAsync("/api/admin/prices/holidays/analyze", ct);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<HolidayAnalysisApiResult>(cancellationToken: ct);
            return result ?? new HolidayAnalysisApiResult { Success = false, Error = "Empty response" };
        }
        catch (Exception ex)
        {
            return new HolidayAnalysisApiResult { Success = false, Error = ex.Message };
        }
    }

    /// <summary>
    /// Calls POST /api/admin/prices/holidays/forward-fill to forward-fill holiday price data.
    /// Supports batching via limit parameter to avoid Cloudflare timeouts.
    /// </summary>
    /// <param name="limit">Max days to process per batch. Null = all (use with caution on large datasets).</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<HolidayForwardFillApiResult> ForwardFillHolidaysAsync(int? limit = null, CancellationToken ct = default)
    {
        if (_httpClient == null)
            return new HolidayForwardFillApiResult { Success = false, Error = "HttpClient not configured" };

        try
        {
            // Use shorter timeout since we're batching - each batch should complete quickly
            using var batchClient = _httpClientFactory.CreateClient();
            batchClient.BaseAddress = _httpClient.BaseAddress;
            batchClient.Timeout = TimeSpan.FromMinutes(2);

            var url = "/api/admin/prices/holidays/forward-fill";
            if (limit.HasValue)
                url += $"?limit={limit.Value}";

            var response = await batchClient.PostAsync(url, null, ct);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<HolidayForwardFillApiResult>(cancellationToken: ct);
            return result ?? new HolidayForwardFillApiResult { Success = false, Error = "Empty response" };
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            return new HolidayForwardFillApiResult { Success = false, Error = "Operation timed out after 2 minutes" };
        }
        catch (Exception ex)
        {
            return new HolidayForwardFillApiResult { Success = false, Error = ex.Message };
        }
    }

    // ============================================================================
    // ============================================================================
    // Crawler Methods (gap detection and loading)
    // ============================================================================

    /// <summary>
    /// Gets securities with gaps in their price history (Security Master driven).
    /// Returns securities ordered by tracked status, then priority, then most missing days.
    /// </summary>
    /// <param name="market">Market filter (default: US)</param>
    /// <param name="limit">Maximum number of securities to return</param>
    /// <param name="includeUntracked">If true, includes untracked securities (after tracked ones)</param>
    /// <param name="ct">Cancellation token</param>
    public async Task<PriceGapsResult> GetPriceGapsAsync(string? market = null, int? limit = null, bool includeUntracked = false, CancellationToken ct = default)
    {
        if (_httpClient == null)
            return new PriceGapsResult { Success = false, Error = "HttpClient not configured" };

        try
        {
            var url = "/api/admin/prices/gaps";
            var queryParams = new List<string>();
            if (!string.IsNullOrEmpty(market))
                queryParams.Add($"market={market}");
            if (limit.HasValue)
                queryParams.Add($"limit={limit.Value}");
            if (includeUntracked)
                queryParams.Add("includeUntracked=true");
            if (queryParams.Count > 0)
                url += "?" + string.Join("&", queryParams);

            var response = await _httpClient.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var result = await response.Content.ReadFromJsonAsync<PriceGapsResult>(options, ct);

            return result ?? new PriceGapsResult { Success = false, Error = "Empty response" };
        }
        catch (Exception ex)
        {
            return new PriceGapsResult { Success = false, Error = ex.Message };
        }
    }

    /// <summary>
    /// Gets the specific missing dates for a security.
    /// </summary>
    public async Task<SecurityGapsResult> GetSecurityGapsAsync(int securityAlias, int? limit = null, CancellationToken ct = default)
    {
        if (_httpClient == null)
            return new SecurityGapsResult { Success = false, Error = "HttpClient not configured" };

        try
        {
            var url = $"/api/admin/prices/gaps/{securityAlias}";
            if (limit.HasValue)
                url += $"?limit={limit.Value}";

            var response = await _httpClient.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var result = await response.Content.ReadFromJsonAsync<SecurityGapsResult>(options, ct);
            return result ?? new SecurityGapsResult { Success = false, Error = "Empty response" };
        }
        catch (Exception ex)
        {
            return new SecurityGapsResult { Success = false, Error = ex.Message };
        }
    }

    // ============================================================================
    // Data Sync Methods (for pulling production data to local)
    // ============================================================================

    /// <summary>
    /// Gets rich monitoring stats for the crawler display.
    /// </summary>
    public async Task<PriceMonitorResult> GetPriceMonitorStatsAsync(CancellationToken ct = default)
    {
        if (_httpClient == null)
            return new PriceMonitorResult { Success = false, Error = "HttpClient not configured" };

        try
        {
            var response = await _httpClient.GetAsync("/api/admin/data/prices/monitor", ct);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<PriceMonitorResult>(cancellationToken: ct);
            return result ?? new PriceMonitorResult { Success = false, Error = "Empty response" };
        }
        catch (Exception ex)
        {
            return new PriceMonitorResult { Success = false, Error = ex.Message };
        }
    }

    /// <summary>
    /// Gets summary of price data available on the remote database.
    /// </summary>
    public async Task<PriceSummaryResult> GetPriceSummaryAsync(CancellationToken ct = default)
    {
        if (_httpClient == null)
            return new PriceSummaryResult { Success = false, Error = "HttpClient not configured" };

        try
        {
            var response = await _httpClient.GetAsync("/api/admin/data/prices/summary", ct);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<PriceSummaryResult>(cancellationToken: ct);
            return result ?? new PriceSummaryResult { Success = false, Error = "Empty response" };
        }
        catch (Exception ex)
        {
            return new PriceSummaryResult { Success = false, Error = ex.Message };
        }
    }

    /// <summary>
    /// Exports all active securities from the remote database.
    /// </summary>
    public async Task<SecuritiesExportResult> ExportSecuritiesAsync(CancellationToken ct = default)
    {
        if (_httpClient == null)
            return new SecuritiesExportResult { Success = false, Error = "HttpClient not configured" };

        try
        {
            var response = await _httpClient.GetAsync("/api/admin/data/securities", ct);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<SecuritiesExportResult>(cancellationToken: ct);
            return result ?? new SecuritiesExportResult { Success = false, Error = "Empty response" };
        }
        catch (Exception ex)
        {
            return new SecuritiesExportResult { Success = false, Error = ex.Message };
        }
    }

    /// <summary>
    /// Exports prices from the remote database with pagination.
    /// </summary>
    public async Task<PricesExportResult> ExportPricesAsync(
        DateTime? startDate = null,
        DateTime? endDate = null,
        int page = 1,
        int pageSize = 10000,
        CancellationToken ct = default)
    {
        if (_httpClient == null)
            return new PricesExportResult { Success = false, Error = "HttpClient not configured" };

        try
        {
            var url = $"/api/admin/data/prices?page={page}&pageSize={pageSize}";
            if (startDate.HasValue)
                url += $"&startDate={startDate.Value:yyyy-MM-dd}";
            if (endDate.HasValue)
                url += $"&endDate={endDate.Value:yyyy-MM-dd}";

            var response = await _httpClient.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<PricesExportResult>(cancellationToken: ct);
            return result ?? new PricesExportResult { Success = false, Error = "Empty response" };
        }
        catch (Exception ex)
        {
            return new PricesExportResult { Success = false, Error = ex.Message };
        }
    }
}

public class ApiResponse<T>
{
    public bool Success { get; set; }
    public T? Data { get; set; }
    public string? Error { get; set; }
}

public class LoadTickersResult
{
    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("tickersRequested")]
    public int TickersRequested { get; set; }

    [JsonPropertyName("tickersProcessed")]
    public int TickersProcessed { get; set; }

    [JsonPropertyName("recordsInserted")]
    public int RecordsInserted { get; set; }

    [JsonPropertyName("errors")]
    public List<string> Errors { get; set; } = [];
}

public class RefreshDateResult
{
    public DateTime Date { get; set; }
    public int SecuritiesProcessed { get; set; }
    public int PricesLoaded { get; set; }
}

public class SecuritySyncApiResult
{
    public int SecuritiesAdded { get; set; }
    public int SecuritiesUpdated { get; set; }
}

public class CoverageDatesResult
{
    public string StartDate { get; set; } = "";
    public string EndDate { get; set; } = "";
    public List<string> DatesWithData { get; set; } = [];
    public int Count { get; set; }
}

public class HolidayAnalysisApiResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string DataStartDate { get; set; } = "";
    public string DataEndDate { get; set; } = "";
    public int TotalDatesWithData { get; set; }
    public int MissingHolidayCount { get; set; }
    public int HolidaysWithPriorData { get; set; }
    public int HolidaysWithoutPriorData { get; set; }
    public List<MissingHolidayApiInfo> MissingHolidays { get; set; } = [];
}

public class MissingHolidayApiInfo
{
    public string Name { get; set; } = "";
    public string Date { get; set; } = "";
    public string PriorTradingDay { get; set; } = "";
    public bool HasPriorData { get; set; }
}

public class HolidayForwardFillApiResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? Message { get; set; }
    public int HolidaysProcessed { get; set; }
    public int TotalRecordsInserted { get; set; }
    public int RemainingDays { get; set; }
    public List<HolidayFilledApiInfo> HolidaysFilled { get; set; } = [];
}

public class HolidayFilledApiInfo
{
    public string Name { get; set; } = "";
    public string Date { get; set; } = "";
    public int RecordsInserted { get; set; }
}

// ============================================================================
// Data Sync DTOs
// ============================================================================

public class PriceSummaryResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public bool HasData { get; set; }
    public string? Message { get; set; }
    public string StartDate { get; set; } = "";
    public string EndDate { get; set; } = "";
    public int TotalRecords { get; set; }
    public int DistinctSecurities { get; set; }
}

public class SecuritiesExportResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public int Count { get; set; }
    public List<SecurityExportInfo> Securities { get; set; } = [];
}

public class SecurityExportInfo
{
    public int SecurityAlias { get; set; }
    public string TickerSymbol { get; set; } = "";
    public string IssueName { get; set; } = "";
    public string? Exchange { get; set; }
    public string? SecurityType { get; set; }
    public string? Country { get; set; }
    public string? Currency { get; set; }
    public string? Isin { get; set; }
    public bool IsActive { get; set; }
}

public class PricesExportResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string StartDate { get; set; } = "";
    public string EndDate { get; set; } = "";
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
    public int TotalCount { get; set; }
    public int Count { get; set; }
    public bool HasMore { get; set; }
    public List<PriceExportInfo> Prices { get; set; } = [];
}

public class PriceExportInfo
{
    public int SecurityAlias { get; set; }
    public string EffectiveDate { get; set; } = "";
    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Close { get; set; }
    public long? Volume { get; set; }
    public decimal? AdjustedClose { get; set; }
}

// ============================================================================
// Price Monitor DTOs (for Crawler display)
// ============================================================================

public class PriceGapsResult
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("market")]
    public string Market { get; set; } = "";

    [JsonPropertyName("includeUntracked")]
    public bool IncludeUntracked { get; set; }

    [JsonPropertyName("summary")]
    public PriceGapsSummary Summary { get; set; } = new();

    [JsonPropertyName("completionPercent")]
    public double CompletionPercent { get; set; }

    [JsonPropertyName("gaps")]
    public List<SecurityGapInfo> Gaps { get; set; } = [];
}

public class PriceGapsSummary
{
    [JsonPropertyName("totalSecurities")]
    public int TotalSecurities { get; set; }

    [JsonPropertyName("totalTrackedSecurities")]
    public int TotalTrackedSecurities { get; set; }

    [JsonPropertyName("totalUntrackedSecurities")]
    public int TotalUntrackedSecurities { get; set; }

    [JsonPropertyName("securitiesWithData")]
    public int SecuritiesWithData { get; set; }

    [JsonPropertyName("securitiesWithGaps")]
    public int SecuritiesWithGaps { get; set; }

    [JsonPropertyName("trackedWithGaps")]
    public int TrackedWithGaps { get; set; }

    [JsonPropertyName("untrackedWithGaps")]
    public int UntrackedWithGaps { get; set; }

    [JsonPropertyName("securitiesComplete")]
    public int SecuritiesComplete { get; set; }

    [JsonPropertyName("totalPriceRecords")]
    public int TotalPriceRecords { get; set; }

    [JsonPropertyName("totalMissingDays")]
    public int TotalMissingDays { get; set; }
}

public class SecurityGapInfo
{
    [JsonPropertyName("securityAlias")]
    public int SecurityAlias { get; set; }

    [JsonPropertyName("ticker")]
    public string Ticker { get; set; } = "";

    [JsonPropertyName("isTracked")]
    public bool IsTracked { get; set; }

    [JsonPropertyName("firstDate")]
    public string FirstDate { get; set; } = "";

    [JsonPropertyName("lastDate")]
    public string LastDate { get; set; } = "";

    [JsonPropertyName("actualDays")]
    public int ActualDays { get; set; }

    [JsonPropertyName("expectedDays")]
    public int ExpectedDays { get; set; }

    [JsonPropertyName("missingDays")]
    public int MissingDays { get; set; }
}

public class SecurityGapsResult
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("securityAlias")]
    public int SecurityAlias { get; set; }

    [JsonPropertyName("ticker")]
    public string Ticker { get; set; } = "";

    [JsonPropertyName("firstDate")]
    public string? FirstDate { get; set; }

    [JsonPropertyName("lastDate")]
    public string? LastDate { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("missingCount")]
    public int MissingCount { get; set; }

    [JsonPropertyName("missingDates")]
    public List<string> MissingDates { get; set; } = [];
}

public class PriceMonitorResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public bool HasData { get; set; }
    public string? Message { get; set; }
    // Core metrics
    public int TotalRecords { get; set; }
    public int DistinctSecurities { get; set; }
    public int DistinctDates { get; set; }
    // Date range
    public string StartDate { get; set; } = "";
    public string EndDate { get; set; } = "";
    public int YearsOfData { get; set; }
    public int AvgRecordsPerDay { get; set; }
    // Breakdown by decade
    public List<DecadeCoverage> CoverageByDecade { get; set; } = [];
    // Recent activity
    public List<RecentActivityItem> RecentActivity { get; set; } = [];
}

public class DecadeCoverage
{
    public string Decade { get; set; } = "";
    public int Records { get; set; }
    public int Securities { get; set; }
    public int TradingDays { get; set; }
}

public class RecentActivityItem
{
    public string Date { get; set; } = "";
    public string LoadedAt { get; set; } = "";
}
