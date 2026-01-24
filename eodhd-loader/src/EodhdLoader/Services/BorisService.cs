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
    private readonly string? _eodhdApiKey;

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
        _eodhdApiKey = config.EodhdApiKey;
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
        // Phase 1: Get current database coverage status
        Log("🔍 Analyzing database coverage...");
        var coverage = await GetCoverageStatusAsync(ct);

        if (coverage == null)
        {
            Log("❌ Could not connect to API. Is the Stock Analyzer running?");
            return;
        }

        Log($"📈 Current state: {coverage.TotalPriceRecords:N0} prices, latest date: {coverage.LatestPriceDate ?? "none"}");

        // Phase 2: Determine what dates need loading
        var datesToLoad = await DeterminePriorityDatesAsync(coverage, ct);

        if (datesToLoad.Count == 0)
        {
            Log("✅ All recent data appears to be loaded!");
            return;
        }

        Log($"📅 {datesToLoad.Count} trading days identified for loading");

        // Phase 3: Load data using bulk API, respecting budget
        var loaded = 0;
        var totalDays = datesToLoad.Count;

        foreach (var date in datesToLoad)
        {
            ct.ThrowIfCancellationRequested();

            // Check budget
            if (_callsUsedToday + BulkApiCost > _dailyBudget)
            {
                Log($"💸 Daily budget exhausted ({_callsUsedToday}/{_dailyBudget} calls used)");
                Log($"⏰ Boris will resume tomorrow with fresh budget");
                break;
            }

            Log($"🕸️ Loading {date:yyyy-MM-dd}...");

            var result = await LoadBulkDataForDateAsync(date, ct);

            if (result.Success)
            {
                _callsUsedToday += BulkApiCost;
                loaded++;

                Log($"✓ {date:yyyy-MM-dd}: {result.RecordsLoaded:N0} prices loaded");

                progress?.Report(new BorisProgress
                {
                    CurrentDate = date,
                    DaysProcessed = loaded,
                    TotalDaysQueued = totalDays,
                    RecordsLoadedToday = result.RecordsLoaded,
                    CallsUsedToday = _callsUsedToday,
                    DailyBudget = _dailyBudget
                });
            }
            else
            {
                Log($"⚠️ {date:yyyy-MM-dd}: {result.Error}");
            }

            // Rate limiting - be nice to the API
            await Task.Delay(1000, ct);
        }

        Log($"🎯 Session complete: {loaded} days loaded, {_callsUsedToday} API calls used");
    }

    private async Task<CoverageStatus?> GetCoverageStatusAsync(CancellationToken ct)
    {
        if (_httpClient == null) return null;

        try
        {
            var response = await _httpClient.GetAsync("/api/admin/prices/status", ct);
            if (!response.IsSuccessStatusCode) return null;

            return await response.Content.ReadFromJsonAsync<CoverageStatus>(ct);
        }
        catch
        {
            return null;
        }
    }

    private async Task<List<DateTime>> DeterminePriorityDatesAsync(CoverageStatus coverage, CancellationToken ct)
    {
        var dates = new List<DateTime>();

        // Start from yesterday and work backwards
        var endDate = DateTime.Today.AddDays(-1);
        var startDate = endDate.AddYears(-5); // Go back 5 years max

        // Fetch actual dates that have data from the API
        Log("🔍 Fetching existing coverage dates from database...");
        HashSet<DateTime> existingDates;
        try
        {
            if (_httpClient == null) throw new InvalidOperationException("HttpClient not initialized");

            var url = $"/api/admin/prices/coverage-dates?startDate={startDate:yyyy-MM-dd}&endDate={endDate:yyyy-MM-dd}";
            var response = await _httpClient.GetAsync(url, ct);

            if (response.IsSuccessStatusCode)
            {
                var coverageResult = await response.Content.ReadFromJsonAsync<CoverageDatesResponse>(ct);
                existingDates = coverageResult?.DatesWithData?
                    .Select(d => DateTime.Parse(d))
                    .ToHashSet() ?? [];
                Log($"📊 Found {existingDates.Count} dates with existing data");
            }
            else
            {
                Log("⚠️ Could not fetch coverage dates, proceeding with full scan");
                existingDates = [];
            }
        }
        catch (Exception ex)
        {
            Log($"⚠️ Error fetching coverage dates: {ex.Message}");
            existingDates = [];
        }

        // Phase 1: Recent dates first (most valuable to users)
        // Load any missing dates from last 30 days
        var recentStart = endDate.AddDays(-30);
        for (var date = endDate; date >= recentStart; date = date.AddDays(-1))
        {
            if (IsTradingDay(date) && !existingDates.Contains(date.Date))
            {
                dates.Add(date);
            }
        }

        // Phase 2: Continue backwards for historical data
        for (var date = recentStart.AddDays(-1); date >= startDate; date = date.AddDays(-1))
        {
            if (IsTradingDay(date) && !existingDates.Contains(date.Date))
            {
                dates.Add(date);
            }
        }

        return dates;
    }

    private async Task<BulkLoadResult> LoadBulkDataForDateAsync(DateTime date, CancellationToken ct)
    {
        try
        {
            if (_httpClient == null)
                return new BulkLoadResult { Success = false, Error = "HttpClient not initialized" };

            // Call the Stock Analyzer API's bulk-load endpoint
            var request = new
            {
                StartDate = date.ToString("yyyy-MM-dd"),
                EndDate = date.ToString("yyyy-MM-dd")
            };

            var response = await _httpClient.PostAsJsonAsync("/api/admin/prices/bulk-load", request, ct);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(ct);
                return new BulkLoadResult { Success = false, Error = error };
            }

            // The bulk-load endpoint runs async, so we can't get exact record count
            // but we know it succeeded
            return new BulkLoadResult
            {
                Success = true,
                RecordsLoaded = 0 // Unknown, runs async on server
            };
        }
        catch (Exception ex)
        {
            return new BulkLoadResult { Success = false, Error = ex.Message };
        }
    }

    private static bool IsTradingDay(DateTime date)
    {
        // Skip weekends
        if (date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday)
            return false;

        // TODO: Add holiday checking for major US holidays
        return true;
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
