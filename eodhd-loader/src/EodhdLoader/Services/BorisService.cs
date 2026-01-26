using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace EodhdLoader.Services;

/// <summary>
/// Boris the Spider - Intelligent price data loader that prioritizes
/// loading the most relevant data first based on popularity and recency.
///
/// Strategy:
/// 1. Recent dates first (users want current data)
/// 2. Popular securities first (S&P 500 > Russell 3000 > everything else)
/// 3. Uses bulk API to maximize data per API call
/// 4. Respects daily API budget (default 3000 calls = 30 bulk days)
/// </summary>
public class BorisService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ConfigurationService _config;

    private HttpClient? _httpClient;
    private string _currentBaseUrl = "";
    private CancellationTokenSource? _cts;
    private bool _isRunning;

    // API budget management
    private const int DefaultDailyBudget = 3000; // API calls per day
    private const int BulkApiCost = 100; // Each bulk call costs 100 API credits
    private int _dailyBudget;
    private int _callsUsedToday;
    private DateTime _budgetResetDate;

    // Progress tracking
    public event EventHandler<string>? LogMessage;

    public bool IsRunning => _isRunning;
    public int CallsUsedToday => _callsUsedToday;
    public int DailyBudget => _dailyBudget;
    public int RemainingCalls => Math.Max(0, _dailyBudget - _callsUsedToday);

    public BorisService(IHttpClientFactory httpClientFactory, ConfigurationService config)
    {
        _httpClientFactory = httpClientFactory;
        _config = config;
        _dailyBudget = DefaultDailyBudget;
        _budgetResetDate = DateTime.Today;
    }

    public void SetDailyBudget(int budget)
    {
        _dailyBudget = budget;
    }

    /// <summary>
    /// Start Boris - he will intelligently crawl and load price data.
    /// </summary>
    public async Task StartAsync(TargetEnvironment environment, IProgress<BorisProgress>? progress = null)
    {
        if (_isRunning) return;

        _isRunning = true;
        _cts = new CancellationTokenSource();

        // Reset budget if new day
        if (DateTime.Today > _budgetResetDate)
        {
            _callsUsedToday = 0;
            _budgetResetDate = DateTime.Today;
        }

        var baseUrl = _config.GetApiUrl(environment);

        // Create a new HttpClient if base URL changed (can't modify BaseAddress after first request)
        if (_httpClient == null || _currentBaseUrl != baseUrl)
        {
            _httpClient = _httpClientFactory.CreateClient();
            _httpClient.BaseAddress = new Uri(baseUrl);
            _currentBaseUrl = baseUrl;
        }

        Log($"🕷️ Boris awakens... Target: {environment}");
        Log($"📊 Daily budget: {_dailyBudget} calls ({_dailyBudget / BulkApiCost} bulk days)");
        Log($"💰 Calls remaining today: {RemainingCalls}");

        try
        {
            await RunCrawlLoopAsync(progress, _cts.Token);
        }
        catch (OperationCanceledException)
        {
            Log("🛑 Boris stopped by user");
        }
        catch (Exception ex)
        {
            Log($"❌ Boris encountered an error: {ex.Message}");
        }
        finally
        {
            _isRunning = false;
            Log("💤 Boris goes to sleep...");
        }
    }

    /// <summary>
    /// Stop Boris gracefully.
    /// </summary>
    public void Stop()
    {
        _cts?.Cancel();
    }

    private async Task RunCrawlLoopAsync(IProgress<BorisProgress>? progress, CancellationToken ct)
    {
        // Phase 1: Get securities with price gaps (Security Master driven)
        Log("🔍 Querying tracked securities for price gaps...");
        var gapsResult = await GetSecurityGapsAsync(ct);

        if (gapsResult == null || !gapsResult.Success)
        {
            Log($"❌ Could not query price gaps: {gapsResult?.Error ?? "Unknown error"}");
            return;
        }

        Log($"📈 Found {gapsResult.Summary.TotalSecurities:N0} tracked securities");
        Log($"   {gapsResult.Summary.SecuritiesWithGaps} with gaps, {gapsResult.Summary.TotalMissingDays:N0} total missing days");
        Log($"   Completion: {gapsResult.CompletionPercent:F1}%");

        if (gapsResult.Summary.SecuritiesWithGaps == 0 || gapsResult.Gaps.Count == 0)
        {
            Log("✅ All tracked securities have complete price history!");
            return;
        }

        // Phase 2: Process each security with gaps
        var securitiesProcessed = 0;
        var totalDatesLoaded = 0;
        var totalPricesInserted = 0;
        var totalSecurities = gapsResult.Gaps.Count;

        foreach (var securityGap in gapsResult.Gaps)
        {
            ct.ThrowIfCancellationRequested();

            // Check budget
            if (_callsUsedToday >= _dailyBudget)
            {
                Log($"💸 Daily budget exhausted ({_callsUsedToday}/{_dailyBudget} calls used)");
                Log($"⏰ Boris will resume tomorrow with fresh budget");
                break;
            }

            Log($"🕸️ Processing {securityGap.Ticker} ({securityGap.MissingDays} missing days)...");

            // Get the specific missing dates for this security
            var missingDates = await GetMissingDatesForSecurityAsync(securityGap.SecurityAlias, ct);
            if (missingDates == null || missingDates.Count == 0)
            {
                Log($"⚠️ {securityGap.Ticker}: No missing dates returned (may have been filled)");
                securitiesProcessed++;
                continue;
            }

            // Load data for each missing date (limited by budget)
            var datesForSecurity = 0;
            var pricesForSecurity = 0;

            foreach (var missingDate in missingDates)
            {
                ct.ThrowIfCancellationRequested();

                if (_callsUsedToday >= _dailyBudget)
                    break;

                var result = await LoadDataForTickerDateAsync(securityGap.Ticker, missingDate, ct);

                if (result.Success)
                {
                    _callsUsedToday++;
                    datesForSecurity++;
                    totalDatesLoaded++;
                    pricesForSecurity += result.RecordsLoaded;
                    totalPricesInserted += result.RecordsLoaded;
                }
                else
                {
                    // Don't spam logs for expected failures (weekends, holidays, etc.)
                    if (!result.Error?.Contains("No data") ?? true)
                        Log($"  ⚠️ {missingDate:yyyy-MM-dd}: {result.Error}");
                }

                // Rate limiting - be nice to the API
                await Task.Delay(200, ct);
            }

            securitiesProcessed++;
            if (datesForSecurity > 0)
            {
                Log($"✓ {securityGap.Ticker}: {datesForSecurity} dates loaded, {pricesForSecurity} prices");
            }

            progress?.Report(new BorisProgress
            {
                CurrentDate = missingDates.FirstOrDefault(),
                DaysProcessed = totalDatesLoaded,
                TotalDaysQueued = gapsResult.Summary.TotalMissingDays,
                RecordsLoadedToday = totalPricesInserted,
                CallsUsedToday = _callsUsedToday,
                DailyBudget = _dailyBudget
            });
        }

        Log($"🎯 Session complete: {securitiesProcessed}/{totalSecurities} securities, {totalDatesLoaded} dates loaded, {totalPricesInserted:N0} prices inserted, {_callsUsedToday} API calls used");
    }

    private async Task<PriceGapsResult?> GetSecurityGapsAsync(CancellationToken ct)
    {
        if (_httpClient == null) return null;

        try
        {
            // Get tracked securities with gaps - limit to 100 per session to avoid overwhelming
            var response = await _httpClient.GetAsync("/api/admin/prices/gaps?market=US&limit=100", ct);
            if (!response.IsSuccessStatusCode) return null;

            return await response.Content.ReadFromJsonAsync<PriceGapsResult>(ct);
        }
        catch
        {
            return null;
        }
    }

    private async Task<List<DateTime>?> GetMissingDatesForSecurityAsync(int securityAlias, CancellationToken ct)
    {
        if (_httpClient == null) return null;

        try
        {
            // Limit to 50 dates per security to keep sessions manageable
            var response = await _httpClient.GetAsync($"/api/admin/prices/gaps/{securityAlias}?limit=50", ct);
            if (!response.IsSuccessStatusCode) return null;

            var result = await response.Content.ReadFromJsonAsync<SecurityGapsResult>(ct);
            if (result?.MissingDates == null) return null;

            return result.MissingDates
                .Select(d => DateTime.Parse(d))
                .OrderByDescending(d => d) // Most recent first
                .ToList();
        }
        catch
        {
            return null;
        }
    }

    private async Task<BulkLoadResult> LoadDataForTickerDateAsync(string ticker, DateTime date, CancellationToken ct)
    {
        try
        {
            if (_httpClient == null)
                return new BulkLoadResult { Success = false, Error = "HttpClient not initialized" };

            // Call the Stock Analyzer API to load data - it will fetch from EODHD internally
            var insertRequest = new
            {
                Tickers = new[] { ticker },
                StartDate = date.ToString("yyyy-MM-dd"),
                EndDate = date.ToString("yyyy-MM-dd")
            };

            var insertResponse = await _httpClient.PostAsJsonAsync("/api/admin/prices/load-tickers", insertRequest, ct);
            if (!insertResponse.IsSuccessStatusCode)
            {
                var error = await insertResponse.Content.ReadAsStringAsync(ct);
                return new BulkLoadResult { Success = false, Error = $"API error: {error}" };
            }

            var result = await insertResponse.Content.ReadFromJsonAsync<LoadTickersApiResult>(ct);
            return new BulkLoadResult
            {
                Success = true,
                RecordsLoaded = result?.RecordsInserted ?? 0,
                RecordsFetched = result?.TickersProcessed ?? 0,
                RecordsMatched = 1
            };
        }
        catch (Exception ex)
        {
            return new BulkLoadResult { Success = false, Error = ex.Message };
        }
    }

    private class LoadTickersApiResult
    {
        public string? Message { get; set; }
        public int TickersRequested { get; set; }
        public int TickersProcessed { get; set; }
        public int RecordsInserted { get; set; }
        public List<string>? Errors { get; set; }
    }

    private void Log(string message)
    {
        LogMessage?.Invoke(this, $"[{DateTime.Now:HH:mm:ss}] {message}");
    }
}

public class BorisProgress
{
    public DateTime CurrentDate { get; set; }
    public int DaysProcessed { get; set; }
    public int TotalDaysQueued { get; set; }
    public int RecordsLoadedToday { get; set; }
    public int CallsUsedToday { get; set; }
    public int DailyBudget { get; set; }

    public double PercentComplete => TotalDaysQueued > 0
        ? (double)DaysProcessed / TotalDaysQueued * 100
        : 0;

    public int RemainingCalls => Math.Max(0, DailyBudget - CallsUsedToday);
}

public class BorisProgressEventArgs : EventArgs
{
    public BorisProgress Progress { get; set; } = new();
}

public class CoverageStatus
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("totalPriceRecords")]
    public long TotalPriceRecords { get; set; }

    [JsonPropertyName("activeSecurities")]
    public int ActiveSecurities { get; set; }

    [JsonPropertyName("latestPriceDate")]
    public string? LatestPriceDate { get; set; }

    [JsonPropertyName("eodhdApiConfigured")]
    public bool EodhdApiConfigured { get; set; }
}

public class BulkLoadResult
{
    public bool Success { get; set; }
    public int RecordsLoaded { get; set; }
    public int RecordsFetched { get; set; }
    public int RecordsMatched { get; set; }
    public string? Error { get; set; }
}

public class CoverageDatesResponse
{
    [JsonPropertyName("startDate")]
    public string? StartDate { get; set; }

    [JsonPropertyName("endDate")]
    public string? EndDate { get; set; }

    [JsonPropertyName("datesWithData")]
    public List<string>? DatesWithData { get; set; }

    [JsonPropertyName("count")]
    public int Count { get; set; }
}
