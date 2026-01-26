using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace EodhdLoader.Services;

/// <summary>
/// Service for testing bulk-by-date gap filling approach.
/// Uses EODHD bulk API endpoint which returns ALL tickers for a single date.
/// This is more efficient than per-ticker calls for gap filling.
///
/// Strategy:
/// 1. Get list of dates with missing data
/// 2. For each date, call bulk API (1 call = all tickers for that date)
/// 3. Match returned tickers to tracked securities
/// 4. Insert only the data we need
/// </summary>
public class BulkFillService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ConfigurationService _config;

    private HttpClient? _httpClient;
    private string _currentBaseUrl = "";
    private CancellationTokenSource? _cts;
    private bool _isRunning;

    // Stats tracking
    private int _datesProcessed;
    private int _totalDatesToProcess;
    private int _recordsInserted;
    private int _apiCallsMade;

    public event EventHandler<string>? LogMessage;

    public bool IsRunning => _isRunning;
    public int DatesProcessed => _datesProcessed;
    public int TotalDatesToProcess => _totalDatesToProcess;
    public int RecordsInserted => _recordsInserted;
    public int ApiCallsMade => _apiCallsMade;

    public BulkFillService(IHttpClientFactory httpClientFactory, ConfigurationService config)
    {
        _httpClientFactory = httpClientFactory;
        _config = config;
    }

    /// <summary>
    /// Get list of dates that have gaps across any tracked securities.
    /// </summary>
    public async Task<BulkFillAnalysis> AnalyzeGapsAsync(TargetEnvironment environment, CancellationToken ct = default)
    {
        var baseUrl = _config.GetApiUrl(environment);
        using var client = _httpClientFactory.CreateClient();
        client.BaseAddress = new Uri(baseUrl);

        try
        {
            // Get coverage dates to understand what we have
            var coverageResponse = await client.GetAsync("/api/admin/prices/coverage-dates", ct);
            if (!coverageResponse.IsSuccessStatusCode)
            {
                return new BulkFillAnalysis { Success = false, Error = "Could not get coverage dates" };
            }

            var coverage = await coverageResponse.Content.ReadFromJsonAsync<CoverageDatesResponse>(ct);
            if (coverage == null)
            {
                return new BulkFillAnalysis { Success = false, Error = "Empty coverage response" };
            }

            // Get price summary to understand total scope
            var summaryResponse = await client.GetAsync("/api/admin/data/prices/summary", ct);
            var summary = summaryResponse.IsSuccessStatusCode
                ? await summaryResponse.Content.ReadFromJsonAsync<PriceSummaryResponse>(ct)
                : null;

            // Get gaps info
            var gapsResponse = await client.GetAsync("/api/admin/prices/gaps?market=US&limit=1000", ct);
            PriceGapsResult? gaps = null;
            if (gapsResponse.IsSuccessStatusCode)
            {
                gaps = await gapsResponse.Content.ReadFromJsonAsync<PriceGapsResult>(ct);
            }

            // Calculate missing dates based on trading calendar
            var existingDates = new HashSet<DateTime>(
                coverage.DatesWithData?.Select(d => DateTime.Parse(d)) ?? Enumerable.Empty<DateTime>());

            var startDate = DateTime.Parse(coverage.StartDate ?? "2016-01-01");
            var endDate = DateTime.Parse(coverage.EndDate ?? DateTime.Today.AddDays(-1).ToString("yyyy-MM-dd"));

            // Generate expected trading days (weekdays, excluding major holidays)
            var expectedDates = GetTradingDays(startDate, endDate);
            var missingDates = expectedDates.Except(existingDates).OrderByDescending(d => d).ToList();

            return new BulkFillAnalysis
            {
                Success = true,
                StartDate = startDate,
                EndDate = endDate,
                ExistingDates = existingDates.Count,
                ExpectedDates = expectedDates.Count,
                MissingDates = missingDates,
                TotalSecurities = summary?.DistinctSecurities ?? 0,
                TotalPriceRecords = summary?.TotalRecords ?? 0,
                SecuritiesWithGaps = gaps?.Summary?.SecuritiesWithGaps ?? 0,
                TotalMissingDays = gaps?.Summary?.TotalMissingDays ?? 0
            };
        }
        catch (Exception ex)
        {
            return new BulkFillAnalysis { Success = false, Error = ex.Message };
        }
    }

    /// <summary>
    /// Fill gaps using bulk-by-date approach.
    /// For each missing date, calls EODHD bulk API and inserts matching data.
    /// </summary>
    public async Task<BulkFillResult> FillGapsAsync(
        TargetEnvironment environment,
        List<DateTime> datesToFill,
        int maxDates = 30,
        IProgress<BulkFillProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (_isRunning) return new BulkFillResult { Success = false, Error = "Already running" };

        _isRunning = true;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _datesProcessed = 0;
        _recordsInserted = 0;
        _apiCallsMade = 0;

        var result = new BulkFillResult();
        var errors = new List<string>();

        try
        {
            var baseUrl = _config.GetApiUrl(environment);
            _httpClient = _httpClientFactory.CreateClient();
            _httpClient.BaseAddress = new Uri(baseUrl);
            _httpClient.Timeout = TimeSpan.FromMinutes(5); // Bulk operations can take time
            _currentBaseUrl = baseUrl;

            // Limit dates to process
            var datesToProcess = datesToFill.Take(maxDates).OrderByDescending(d => d).ToList();
            _totalDatesToProcess = datesToProcess.Count;

            Log($"Starting bulk fill for {datesToProcess.Count} dates (most recent first)");

            foreach (var date in datesToProcess)
            {
                if (_cts.Token.IsCancellationRequested) break;

                try
                {
                    Log($"Processing {date:yyyy-MM-dd}...");

                    // Call the sync-eodhd endpoint which uses bulk API
                    var syncRequest = new
                    {
                        Date = date.ToString("yyyy-MM-dd")
                    };

                    var response = await _httpClient.PostAsJsonAsync("/api/admin/prices/sync-eodhd", syncRequest, _cts.Token);
                    _apiCallsMade++;

                    if (response.IsSuccessStatusCode)
                    {
                        var syncResult = await response.Content.ReadFromJsonAsync<SyncEodhdResponse>(_cts.Token);
                        var inserted = syncResult?.RecordsInserted ?? 0;
                        _recordsInserted += inserted;
                        _datesProcessed++;

                        Log($"  ✓ {date:yyyy-MM-dd}: {inserted:N0} records inserted");
                    }
                    else
                    {
                        var error = await response.Content.ReadAsStringAsync(_cts.Token);
                        errors.Add($"{date:yyyy-MM-dd}: {error}");
                        Log($"  ✗ {date:yyyy-MM-dd}: {error}");
                    }

                    // Report progress
                    progress?.Report(new BulkFillProgress
                    {
                        CurrentDate = date,
                        DatesProcessed = _datesProcessed,
                        TotalDates = _totalDatesToProcess,
                        RecordsInserted = _recordsInserted,
                        ApiCallsMade = _apiCallsMade
                    });

                    // Small delay between bulk calls - be nice to the API
                    await Task.Delay(500, _cts.Token);
                }
                catch (TaskCanceledException)
                {
                    Log("Cancelled");
                    break;
                }
                catch (Exception ex)
                {
                    errors.Add($"{date:yyyy-MM-dd}: {ex.Message}");
                    Log($"  ✗ {date:yyyy-MM-dd}: {ex.Message}");
                }
            }

            result.Success = true;
            result.DatesProcessed = _datesProcessed;
            result.RecordsInserted = _recordsInserted;
            result.ApiCallsMade = _apiCallsMade;
            result.Errors = errors;
            result.WasCancelled = _cts.Token.IsCancellationRequested;

            Log($"Bulk fill complete: {_datesProcessed} dates, {_recordsInserted:N0} records, {_apiCallsMade} API calls");
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Error = ex.Message;
            Log($"Error: {ex.Message}");
        }
        finally
        {
            _isRunning = false;
        }

        return result;
    }

    public void Stop()
    {
        _cts?.Cancel();
    }

    private List<DateTime> GetTradingDays(DateTime start, DateTime end)
    {
        var days = new List<DateTime>();
        var current = start;

        // Major US market holidays (simplified - actual implementation would use a proper calendar)
        var majorHolidays = new HashSet<(int Month, int Day)>
        {
            (1, 1),   // New Year's Day
            (7, 4),   // Independence Day
            (12, 25), // Christmas
        };

        while (current <= end)
        {
            // Skip weekends
            if (current.DayOfWeek != DayOfWeek.Saturday && current.DayOfWeek != DayOfWeek.Sunday)
            {
                // Skip major holidays (simplified check)
                if (!majorHolidays.Contains((current.Month, current.Day)))
                {
                    days.Add(current);
                }
            }
            current = current.AddDays(1);
        }

        return days;
    }

    private void Log(string message)
    {
        LogMessage?.Invoke(this, $"[{DateTime.Now:HH:mm:ss}] {message}");
    }
}

public class BulkFillAnalysis
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public int ExistingDates { get; set; }
    public int ExpectedDates { get; set; }
    public List<DateTime> MissingDates { get; set; } = new();
    public int TotalSecurities { get; set; }
    public long TotalPriceRecords { get; set; }
    public int SecuritiesWithGaps { get; set; }
    public int TotalMissingDays { get; set; }

    public double CoveragePercent => ExpectedDates > 0 ? (double)ExistingDates / ExpectedDates * 100 : 0;
}

public class BulkFillResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public int DatesProcessed { get; set; }
    public int RecordsInserted { get; set; }
    public int ApiCallsMade { get; set; }
    public List<string> Errors { get; set; } = new();
    public bool WasCancelled { get; set; }
}

public class BulkFillProgress
{
    public DateTime CurrentDate { get; set; }
    public int DatesProcessed { get; set; }
    public int TotalDates { get; set; }
    public int RecordsInserted { get; set; }
    public int ApiCallsMade { get; set; }

    public double PercentComplete => TotalDates > 0 ? (double)DatesProcessed / TotalDates * 100 : 0;
}

// Note: CoverageDatesResponse is defined in BorisService.cs
// Note: PriceGapsResult is defined in StockAnalyzerApiClient.cs

public class PriceSummaryResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("hasData")]
    public bool HasData { get; set; }

    [JsonPropertyName("startDate")]
    public string? StartDate { get; set; }

    [JsonPropertyName("endDate")]
    public string? EndDate { get; set; }

    [JsonPropertyName("totalRecords")]
    public long TotalRecords { get; set; }

    [JsonPropertyName("distinctSecurities")]
    public int DistinctSecurities { get; set; }
}

public class SyncEodhdResponse
{
    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("date")]
    public string? Date { get; set; }

    [JsonPropertyName("recordsFetched")]
    public int RecordsFetched { get; set; }

    [JsonPropertyName("recordsMatched")]
    public int RecordsMatched { get; set; }

    [JsonPropertyName("recordsInserted")]
    public int RecordsInserted { get; set; }
}
