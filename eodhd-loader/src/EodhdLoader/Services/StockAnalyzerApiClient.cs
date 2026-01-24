using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

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
            tickers,
            fromDate = fromDate.ToString("yyyy-MM-dd"),
            toDate = toDate?.ToString("yyyy-MM-dd")
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
    /// Uses extended timeout (10 minutes) since this can process hundreds of holidays.
    /// </summary>
    public async Task<HolidayForwardFillApiResult> ForwardFillHolidaysAsync(CancellationToken ct = default)
    {
        if (_httpClient == null)
            return new HolidayForwardFillApiResult { Success = false, Error = "HttpClient not configured" };

        try
        {
            // Create a separate HttpClient with extended timeout for this long-running operation
            // The default HttpClient.Timeout (100s) is too short for filling hundreds of holidays
            using var longTimeoutClient = _httpClientFactory.CreateClient();
            longTimeoutClient.BaseAddress = _httpClient.BaseAddress;
            longTimeoutClient.Timeout = TimeSpan.FromMinutes(10);

            var response = await longTimeoutClient.PostAsync("/api/admin/prices/holidays/forward-fill", null, ct);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<HolidayForwardFillApiResult>(cancellationToken: ct);
            return result ?? new HolidayForwardFillApiResult { Success = false, Error = "Empty response" };
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            return new HolidayForwardFillApiResult { Success = false, Error = "Operation timed out after 10 minutes" };
        }
        catch (Exception ex)
        {
            return new HolidayForwardFillApiResult { Success = false, Error = ex.Message };
        }
    }

    // ============================================================================
    // Data Sync Methods (for pulling production data to local)
    // ============================================================================

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
    public int TickersProcessed { get; set; }
    public int PricesLoaded { get; set; }
    public int Errors { get; set; }
    public List<string> FailedTickers { get; set; } = [];
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
