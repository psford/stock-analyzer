using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EodhdLoader.Services;

namespace EodhdLoader.ViewModels;

/// <summary>
/// ViewModel for the Crawler - an autonomous gap-filling agent.
/// Finds securities with missing price data and loads from EODHD.
/// Paced at ~52 API calls/minute (75,000/day budget).
///
/// Single-loop operation with auto-promotion:
/// 1. Fill tracked securities with gaps
/// 2. When tracked gaps exhausted, promote a batch of untracked → tracked
/// 3. Re-run tracked gap query, continue filling
/// 4. Repeat until no untracked securities remain to promote
/// </summary>
public partial class CrawlerViewModel : ViewModelBase
{
    private readonly StockAnalyzerApiClient _apiClient;
    private readonly IISharesConstituentService _constituentService;
    private readonly DispatcherTimer _crawlTimer;
    private CancellationTokenSource? _cts;

    // Rate limiting: 75,000 calls/day = 52/min = ~1.15 seconds between calls
    // Using 1.2s to be safe
    private const int CallIntervalMs = 1200;
    private const int SecuritiesToFetch = 20; // Securities to query at a time

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCrawlCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCrawlCommand))]
    private bool _isCrawling;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCrawlCommand))]
    private bool _isConnected;

    [ObservableProperty]
    private string _connectionStatus = "Not Connected";

    [ObservableProperty]
    private string _statusText = "Click Test to connect, then Start to begin crawling.";

    [ObservableProperty]
    private TargetEnvironment _selectedEnvironment = TargetEnvironment.Production;

    [ObservableProperty]
    private string _currentPhase = "Idle";

    // Progress tracking - Security Master based
    [ObservableProperty]
    private int _totalSecurities;

    [ObservableProperty]
    private int _securitiesWithGaps;

    [ObservableProperty]
    private int _totalMissingDays;

    [ObservableProperty]
    private int _securitiesProcessedThisSession;

    [ObservableProperty]
    private double _completionPercent;

    [ObservableProperty]
    private string _currentAction = "Idle";

    [ObservableProperty]
    private string _currentTicker = "—";

    [ObservableProperty]
    private int _recordsLoadedThisSession;

    [ObservableProperty]
    private int _errorsThisSession;

    // Activity log
    [ObservableProperty]
    private ObservableCollection<CrawlActivity> _activityLog = [];

    // Dashboard stats (populated on connect)
    [ObservableProperty]
    private string _totalRecordsDisplay = "—";

    [ObservableProperty]
    private string _totalSecuritiesDisplay = "—";

    [ObservableProperty]
    private string _trackedDisplay = "—";

    [ObservableProperty]
    private string _untrackedDisplay = "—";

    [ObservableProperty]
    private string _unavailableDisplay = "—";

    [ObservableProperty]
    private string _dataSpanDisplay = "—";

    [ObservableProperty]
    private string _latestDateDisplay = "—";

    // Session timing and delta tracking
    private DateTime _sessionStartedAt;
    private int _initialGapsCount;
    private int _lastSessionTickerCount;
    private int _lastSessionRecordCount;

    // Base total records from connect — used to compute live TotalRecordsDisplay
    private double _initialTotalRecords;
    // Base universe counts from connect — updated locally during crawling
    private int _trackedCount;
    private int _untrackedCount;
    private int _unavailableCount;

    [ObservableProperty]
    private string _gapsDeltaDisplay = "";

    [ObservableProperty]
    private bool _isGapsIncreasing;

    [ObservableProperty]
    private string _tickerRateDisplay = "";

    [ObservableProperty]
    private string _recordRateDisplay = "";

    [ObservableProperty]
    private string _sessionTimeDisplay = "";

    [ObservableProperty]
    private string _tickersDisplay = "—";

    [ObservableProperty]
    private string _tickersSubtitle = "";

    [ObservableProperty]
    private string _recordsDisplay = "—";

    [ObservableProperty]
    private string _recordsSubtitle = "";

    // Freshness indicator for CoverageSummary-based stats
    [ObservableProperty]
    private string _summaryFreshnessDisplay = "";

    // Heatmap data (bound to CoverageHeatmapControl)
    [ObservableProperty]
    private HeatmapDataResult? _heatmapData;

    [ObservableProperty]
    private bool _isHeatmapLoading;

    // Active cell for pulsing indicator (year, score of currently loading data)
    [ObservableProperty]
    private int _activeHeatmapYear = -1;

    [ObservableProperty]
    private int _activeHeatmapScore = -1;

    // Counter incremented when crawler inserts data for the active cell.
    // HeatmapV2Control observes this to trigger purple→green fade animation.
    [ObservableProperty]
    private long _heatmapCellUpdateCounter;

    // Queue of securities to process
    private Queue<SecurityGapInfo> _securityQueue = new();
    // Current security's missing dates
    private Queue<string> _dateQueue = new();
    private SecurityGapInfo? _currentSecurity;
    // Track consecutive dates with no data returned for detecting unavailable securities
    private int _consecutiveNoDataCount;
    private const int NoDataThreshold = 10; // Mark as unavailable after 10 consecutive dates with no data (accounts for extended closures like 9/11)
    private int _lastNoDataAlias; // Track which security the no-data counter applies to
    // Track records loaded per security to detect zero-result completions
    private int _recordsForCurrentSecurity;
    // Securities that completed all their dates with 0 records loaded this session —
    // skip them on subsequent gap refreshes to prevent infinite loops.
    private readonly HashSet<int> _zeroResultAliases = new();
    // Track records loaded since last full heatmap refresh
    private int _recordsSinceHeatmapRefresh;
    private const int HeatmapFullRefreshInterval = 100; // Full re-fetch every N records
    // Re-entrancy guard: DispatcherTimer fires every 1.2s regardless of whether the previous
    // async handler completed. Without this guard, concurrent ProcessNextAsync calls cause
    // race conditions on _currentSecurity, _dateQueue, and _securityQueue.
    private bool _isProcessing;

    public CrawlerViewModel(StockAnalyzerApiClient apiClient, IISharesConstituentService constituentService)
    {
        _apiClient = apiClient;
        _constituentService = constituentService;

        // Timer for paced API calls
        _crawlTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(CallIntervalMs)
        };
        _crawlTimer.Tick += async (s, e) => await ProcessNextAsync();
    }

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        IsBusy = true;
        ConnectionStatus = "Testing...";
        var envName = SelectedEnvironment == TargetEnvironment.Production ? "Production" : "Local";
        StatusText = $"Testing connection to {envName}...";

        try
        {
            _apiClient.CurrentEnvironment = SelectedEnvironment;
            var connected = await _apiClient.TestConnectionAsync();

            if (connected)
            {
                IsConnected = true;
                ConnectionStatus = $"Connected ({envName})";

                // Load dashboard stats in parallel with gap status
                StatusText = "Loading dashboard data...";
                var dashboardTask = LoadDashboardStatsAsync();

                // Fetch initial gap status (tracked only for initial display)
                await RefreshGapsAsync();

                await dashboardTask;
                StatusText = $"Connected. {SecuritiesWithGaps} tracked securities with gaps. Click Start to begin.";
            }
            else
            {
                IsConnected = false;
                ConnectionStatus = "Failed";
                StatusText = $"Could not connect to {envName}.";
            }
        }
        catch (Exception ex)
        {
            IsConnected = false;
            ConnectionStatus = "Failed";
            StatusText = $"Connection failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Fetches gap data for tracked securities and populates the security queue.
    /// Returns true if the query succeeded, false if it failed or timed out.
    /// Callers MUST check the return value — an empty queue after false means
    /// "query failed", NOT "no gaps exist."
    /// </summary>
    private async Task<bool> RefreshGapsAsync(int? limitOverride = null)
    {
        var gaps = await _apiClient.GetPriceGapsAsync("US", limitOverride ?? SecuritiesToFetch, _cts?.Token ?? default);
        if (gaps.Success)
        {
            TotalSecurities = gaps.Summary.TotalSecurities;
            SecuritiesWithGaps = gaps.Summary.SecuritiesWithGaps;
            TotalMissingDays = gaps.Summary.TotalMissingDays;
            CompletionPercent = gaps.CompletionPercent;
            if (IsCrawling) UpdateSessionMetrics();

            _securityQueue.Clear();
            foreach (var secGap in gaps.Gaps)
            {
                _securityQueue.Enqueue(secGap);
            }
            return true;
        }
        else if (gaps.TimedOut)
        {
            AddActivity("⏱", "Timeout", "Gap query timed out");
            StatusText = "Gap query timed out (Azure SQL Basic limitation)";
            return false;
        }
        else
        {
            var errorMsg = string.IsNullOrEmpty(gaps.Error) ? "Unknown error" : gaps.Error;
            AddActivity("✗", "Error", $"Gap query failed: {errorMsg}");
            StatusText = $"Error fetching gaps: {errorMsg}";
            return false;
        }
    }

    /// <summary>
    /// Promotes a batch of untracked securities to tracked, then refreshes gaps.
    /// Returns true if promotion + refresh succeeded AND found new gaps.
    /// Returns false if nothing to promote or the operation failed.
    /// </summary>
    private async Task<bool> PromoteAndRefreshAsync()
    {
        CurrentPhase = "Promoting...";
        CurrentAction = "Promoting untracked securities...";
        AddActivity("↑", "Promote", "Promoting next batch of untracked securities...");

        var result = await _apiClient.PromoteUntrackedAsync(500, _cts?.Token ?? default);

        if (!result.Success)
        {
            AddActivity("✗", "Error", $"Promote failed: {result.Error}");
            StatusText = $"Promote failed: {result.Error}";
            return false;
        }

        if (result.Promoted == 0)
        {
            // No untracked securities left to promote — truly done
            return false;
        }

        AddActivity("↑", "Promoted", $"{result.Promoted} securities promoted to tracked");

        // Update universe cards locally (promoted: untracked → tracked)
        _trackedCount += result.Promoted;
        _untrackedCount = Math.Max(0, _untrackedCount - result.Promoted);
        TrackedDisplay = _trackedCount.ToString("N0");
        UntrackedDisplay = _untrackedCount.ToString("N0");

        CurrentPhase = "Crawling";
        return await RefreshGapsAsync();
    }

    /// <summary>
    /// Checks for stale constituent data and loads missing month-end snapshots if needed.
    /// AC5.1-AC5.4: Detects stale ETFs, loads them with rate limiting, proceeds even if all fail.
    /// </summary>
    private async Task CheckAndLoadConstituentsAsync()
    {
        CurrentAction = "Checking constituent staleness...";
        StatusText = "Checking constituent data freshness...";
        AddActivity("🔍", "Constituents", "Checking for stale month-end data...");

        try
        {
            var staleEtfs = await _constituentService.GetStaleEtfsAsync(_cts?.Token ?? default);

            if (staleEtfs.Count == 0)
            {
                AddActivity("✅", "Constituents", "All constituent data is current");
                return;
            }

            AddActivity("📊", "Constituents", $"Found {staleEtfs.Count} ETFs with stale data, loading...");
            StatusText = $"Loading constituents for {staleEtfs.Count} stale ETFs...";

            int loaded = 0, failed = 0;
            foreach (var (etfTicker, indexCode) in staleEtfs)
            {
                if (_cts?.Token.IsCancellationRequested == true) break;

                CurrentAction = $"Loading constituents: {etfTicker} ({loaded + failed + 1}/{staleEtfs.Count})";

                try
                {
                    var stats = await _constituentService.IngestEtfAsync(etfTicker, null, _cts?.Token ?? default);
                    loaded++;
                    AddActivity("✅", etfTicker, $"{stats.Inserted} inserted, {stats.SkippedExisting} skipped");
                }
                catch (Exception ex)
                {
                    failed++;
                    AddActivity("⚠️", etfTicker, $"Failed: {ex.Message}");
                }

                // Rate limiting — use shared constant from service (AC6.1)
                if (_cts?.Token.IsCancellationRequested != true)
                    await Task.Delay(ISharesConstituentService.RequestDelayMs, _cts?.Token ?? default);
            }

            var summary = $"Constituent refresh complete: {loaded} loaded, {failed} failed";
            AddActivity("📊", "Constituents", summary);
            StatusText = summary;
        }
        catch (OperationCanceledException)
        {
            AddActivity("⏹️", "Constituents", "Constituent check cancelled");
        }
        catch (Exception ex)
        {
            // Best effort — log and continue to gap filling (AC5.4)
            AddActivity("⚠️", "Constituents", $"Staleness check failed: {ex.Message}");
            StatusText = "Constituent check failed — proceeding to gap filling";
        }
    }

    private bool CanStartCrawl() => IsConnected && !IsCrawling;

    [RelayCommand(CanExecute = nameof(CanStartCrawl))]
    private async Task StartCrawlAsync()
    {
        IsCrawling = true;
        _cts = new CancellationTokenSource();
        SecuritiesProcessedThisSession = 0;
        RecordsLoadedThisSession = 0;
        ErrorsThisSession = 0;
        _zeroResultAliases.Clear();

        // Session metrics initialization
        _sessionStartedAt = DateTime.Now;
        _initialGapsCount = SecuritiesWithGaps;
        GapsDeltaDisplay = "";
        IsGapsIncreasing = false;
        TickerRateDisplay = "";
        RecordRateDisplay = "";
        SessionTimeDisplay = "";
        TickersDisplay = "0";
        TickersSubtitle = "this session";
        RecordsDisplay = "0";
        RecordsSubtitle = "this session";

        CurrentPhase = "Crawling";
        CurrentAction = "Starting...";
        StatusText = "Fetching gaps...";

        // Get initial batch of tracked securities with gaps
        var ok = await RefreshGapsAsync();

        if (!ok)
        {
            CurrentAction = "Error";
            IsCrawling = false;
            return;
        }

        // Check and load stale constituent data before proceeding with gap filling
        await CheckAndLoadConstituentsAsync();

        if (_securityQueue.Count == 0)
        {
            // No tracked gaps — try promoting untracked securities
            var promoted = await PromoteAndRefreshAsync();
            if (promoted && _securityQueue.Count > 0)
            {
                _crawlTimer.Start();
                CurrentAction = "Crawling";
                StatusText = $"Promoted new batch. Crawling: {_securityQueue.Count} securities queued";
                return;
            }

            // Preserve session metrics for idle display
            _lastSessionTickerCount = SecuritiesProcessedThisSession;
            _lastSessionRecordCount = RecordsLoadedThisSession;
            TickersDisplay = _lastSessionTickerCount > 0 ? _lastSessionTickerCount.ToString("N0") : "—";
            TickersSubtitle = _lastSessionTickerCount > 0 ? "last session" : "";
            RecordsDisplay = _lastSessionRecordCount > 0 ? _lastSessionRecordCount.ToString("N0") : "—";
            RecordsSubtitle = _lastSessionRecordCount > 0 ? "last session" : "";
            CurrentAction = "Complete!";
            CurrentPhase = "Complete";
            StatusText = "All securities processed! No gaps remain.";
            AddActivity("✓", "Complete", "All securities processed (or marked unavailable)");
            IsCrawling = false;
            return;
        }

        // Start the paced timer
        _crawlTimer.Start();
        CurrentAction = "Crawling";
        StatusText = $"Crawling: {_securityQueue.Count} securities queued";
    }

    private bool CanStopCrawl() => IsCrawling;

    [RelayCommand(CanExecute = nameof(CanStopCrawl))]
    private void StopCrawl()
    {
        _crawlTimer.Stop();
        _cts?.Cancel();
        IsCrawling = false;
        ActiveHeatmapYear = -1;
        ActiveHeatmapScore = -1;
        CurrentAction = "Stopped";
        StatusText = $"Stopped. Processed {SecuritiesProcessedThisSession} securities, loaded {RecordsLoadedThisSession:N0} records.";

        // Preserve session metrics for idle display
        _lastSessionTickerCount = SecuritiesProcessedThisSession;
        _lastSessionRecordCount = RecordsLoadedThisSession;
        TickersDisplay = _lastSessionTickerCount > 0 ? _lastSessionTickerCount.ToString("N0") : "—";
        TickersSubtitle = _lastSessionTickerCount > 0 ? "last session" : "";
        RecordsDisplay = _lastSessionRecordCount > 0 ? _lastSessionRecordCount.ToString("N0") : "—";
        RecordsSubtitle = _lastSessionRecordCount > 0 ? "last session" : "";

        // Full heatmap refresh now that crawling stopped — local updates were used
        // during crawling to avoid stale API cache overwrites
        _recordsSinceHeatmapRefresh = 0;
        _ = RefreshHeatmapFromApiAsync();

        // Trigger CoverageSummary refresh (fire-and-forget, 2-5 min server-side)
        // This updates the pre-aggregated stats so Price Records and Securities cards are current
        _ = _apiClient.TriggerSummaryRefreshAsync();
    }

    [RelayCommand]
    private async Task BulkMarkCompleteAsync()
    {
        AddActivity("⏳", "Bulk", "Running bulk mark — checking eligible securities...");
        StatusText = "Bulk marking EODHD complete...";

        try
        {
            // Dry run first to show what would be marked
            var preview = await _apiClient.BulkMarkEodhdCompleteAsync(minPriceCount: 50, dryRun: true);
            if (!preview.Success)
            {
                AddActivity("✗", "Bulk", $"Preview failed: {preview.Error}");
                StatusText = $"Bulk mark failed: {preview.Error}";
                return;
            }

            if (preview.Count == 0)
            {
                AddActivity("✓", "Bulk", "No securities eligible for bulk marking");
                StatusText = "No securities eligible for bulk marking";
                return;
            }

            // Execute the actual bulk mark
            var result = await _apiClient.BulkMarkEodhdCompleteAsync(minPriceCount: 50, dryRun: false);
            if (result.Success)
            {
                AddActivity("✓", "Bulk", $"Marked {result.Count} securities as EODHD complete");
                StatusText = $"Bulk marked {result.Count} securities as EODHD complete";
            }
            else
            {
                AddActivity("✗", "Bulk", $"Bulk mark failed: {result.Error}");
                StatusText = $"Bulk mark failed: {result.Error}";
            }
        }
        catch (Exception ex)
        {
            AddActivity("✗", "Bulk", $"Error: {ex.Message}");
            StatusText = $"Bulk mark error: {ex.Message}";
        }
    }

    private async Task ProcessNextAsync()
    {
        // Re-entrancy guard: DispatcherTimer fires every 1.2s regardless of whether the
        // previous async handler completed. Without this, concurrent calls cause race
        // conditions on _currentSecurity/_dateQueue AND can hammer the database with
        // parallel gap queries, causing DTU exhaustion (HTTP 500).
        if (_isProcessing) return;
        _isProcessing = true;

        try
        {
            if (_cts?.Token.IsCancellationRequested == true)
            {
                _crawlTimer.Stop();
                return;
            }

            // If we have dates queued for current security, process next date
            if (_dateQueue.Count > 0 && _currentSecurity != null)
            {
                await ProcessNextDateAsync();
                return;
            }

            // Need to get next security
            if (_securityQueue.Count == 0)
            {
                CurrentAction = "Refreshing gaps...";
                var refreshOk = await RefreshGapsAsync();

                if (!refreshOk)
                {
                    _crawlTimer.Stop();
                    IsCrawling = false;
                    CurrentAction = "Failed";
                    return;
                }

                if (_securityQueue.Count == 0)
                {
                    // All tracked gaps filled — promote untracked securities and continue
                    var promoted = await PromoteAndRefreshAsync();
                    if (promoted && _securityQueue.Count > 0)
                    {
                        StatusText = $"Promoted new batch. {_securityQueue.Count} securities queued. {SecuritiesProcessedThisSession} processed so far.";
                        return;
                    }

                    // Nothing left to promote — truly done
                    _crawlTimer.Stop();
                    // Preserve session metrics for idle display
                    _lastSessionTickerCount = SecuritiesProcessedThisSession;
                    _lastSessionRecordCount = RecordsLoadedThisSession;
                    TickersDisplay = _lastSessionTickerCount > 0 ? _lastSessionTickerCount.ToString("N0") : "—";
                    TickersSubtitle = _lastSessionTickerCount > 0 ? "last session" : "";
                    RecordsDisplay = _lastSessionRecordCount > 0 ? _lastSessionRecordCount.ToString("N0") : "—";
                    RecordsSubtitle = _lastSessionRecordCount > 0 ? "last session" : "";
                    IsCrawling = false;
                    CurrentAction = "Complete!";
                    CurrentPhase = "Complete";
                    StatusText = $"All securities processed! {SecuritiesProcessedThisSession} securities, {RecordsLoadedThisSession:N0} records.";
                    AddActivity("✓", "Complete", "All tracked and untracked securities processed");
                    _recordsSinceHeatmapRefresh = 0;
                    _ = RefreshHeatmapFromApiAsync();
                    return;
                }
            }

            // Loop: "already complete" securities retry immediately without waiting for next tick
            while (true)
            {
                // Get next security — skip any that already completed with 0 records this session
                SecurityGapInfo? nextSecurity = null;
                while (_securityQueue.Count > 0)
                {
                    var candidate = _securityQueue.Dequeue();
                    if (_zeroResultAliases.Contains(candidate.SecurityAlias))
                    {
                        // Already tried this security, got 0 records — skip it
                        continue;
                    }
                    nextSecurity = candidate;
                    break;
                }

                if (nextSecurity == null)
                {
                    // All securities in the current batch were in the skip set.
                    // Before promoting, check if there are actionable tracked gaps beyond
                    // what the SecuritiesToFetch limit returned. Only promote when ALL
                    // tracked securities have 100% coverage or are in the skip set.
                    if (SecuritiesWithGaps > _zeroResultAliases.Count)
                    {
                        // There are tracked securities with gaps we haven't tried yet.
                        // Fetch a larger batch to find them past the skipped ones.
                        var expandedLimit = _zeroResultAliases.Count + SecuritiesToFetch;
                        AddActivity("🔄", "Refresh", $"Skipped {_zeroResultAliases.Count} zero-result securities, searching for remaining {SecuritiesWithGaps - _zeroResultAliases.Count} with gaps...");
                        var largerOk = await RefreshGapsAsync(expandedLimit);
                        if (largerOk && _securityQueue.Count > 0)
                        {
                            // Found more securities to process. Next tick will dequeue them.
                            return;
                        }
                    }

                    // All tracked gaps are either filled or in the skip set.
                    // Try promoting untracked securities before stopping.
                    var promoted = await PromoteAndRefreshAsync();
                    if (promoted && _securityQueue.Count > 0)
                    {
                        _zeroResultAliases.Clear(); // New batch = new securities to try
                        StatusText = $"Promoted new batch. {_securityQueue.Count} securities queued. {SecuritiesProcessedThisSession} processed so far.";
                        return;
                    }

                    // Nothing left to promote — truly done
                    _crawlTimer.Stop();
                    // Preserve session metrics for idle display
                    _lastSessionTickerCount = SecuritiesProcessedThisSession;
                    _lastSessionRecordCount = RecordsLoadedThisSession;
                    TickersDisplay = _lastSessionTickerCount > 0 ? _lastSessionTickerCount.ToString("N0") : "—";
                    TickersSubtitle = _lastSessionTickerCount > 0 ? "last session" : "";
                    RecordsDisplay = _lastSessionRecordCount > 0 ? _lastSessionRecordCount.ToString("N0") : "—";
                    RecordsSubtitle = _lastSessionRecordCount > 0 ? "last session" : "";
                    IsCrawling = false;
                    CurrentAction = "Complete!";
                    CurrentPhase = "Complete";
                    StatusText = $"All securities processed! {SecuritiesProcessedThisSession} securities, {RecordsLoadedThisSession:N0} records.";
                    AddActivity("✓", "Complete", "All tracked and untracked securities processed");
                    _recordsSinceHeatmapRefresh = 0;
                    _ = RefreshHeatmapFromApiAsync();
                    return;
                }

                _currentSecurity = nextSecurity;
                CurrentTicker = _currentSecurity.Ticker;
                CurrentAction = $"Loading full history for {_currentSecurity.Ticker}...";

                // Set heatmap active cell for ripple animation
                ActiveHeatmapScore = _currentSecurity.ImportanceScore;
                ActiveHeatmapYear = DateTime.Today.Year;

                AddActivity("📊", _currentSecurity.Ticker, "Loading full price history...");
                StatusText = $"Loading full history for {_currentSecurity.Ticker}...";

                try
                {
                    // Single EODHD API call: fetch ALL available history (1980-today)
                    // Server-side BulkInsertAsync deduplicates against existing records
                    var result = await _apiClient.LoadTickersAsync(
                        [_currentSecurity.Ticker],
                        new DateTime(1980, 1, 1),
                        DateTime.Today,
                        _cts?.Token ?? default);

                    if (result.Success && result.Data != null)
                    {
                        var inserted = result.Data.RecordsInserted;
                        RecordsLoadedThisSession += inserted;
                        SecuritiesProcessedThisSession++;

                        if (inserted > 0)
                        {
                            AddActivity("✓", _currentSecurity.Ticker, $"{inserted:N0} records loaded — marking complete");
                            HeatmapCellUpdateCounter++;
                            // Full-history load (1980-today) fetches everything EODHD has.
                            // Mark complete immediately so this security drops out of gap queries.
                            _ = _apiClient.MarkEodhdCompleteAsync(_currentSecurity.SecurityAlias, _cts?.Token ?? default);
                        }
                        else
                        {
                            // 0 records: either no EODHD data or all data already existed
                            if (result.Data.Errors?.Count > 0 &&
                                result.Data.Errors.Any(e => e.Contains("No data returned", StringComparison.OrdinalIgnoreCase)))
                            {
                                _zeroResultAliases.Add(_currentSecurity.SecurityAlias);
                                await MarkSecurityUnavailableAsync(_currentSecurity);
                                return; // MarkSecurityUnavailableAsync handles state cleanup
                            }
                            else
                            {
                                _zeroResultAliases.Add(_currentSecurity.SecurityAlias);
                                AddActivity("✓", _currentSecurity.Ticker, "Already complete — marking EODHD complete");
                                // Mark server-side so gap query won't return this security again
                                _ = _apiClient.MarkEodhdCompleteAsync(_currentSecurity.SecurityAlias, _cts?.Token ?? default);
                                _currentSecurity = null;
                                continue; // Immediately try next — don't wait for timer tick
                            }
                        }

                        SecuritiesWithGaps = Math.Max(0, SecuritiesWithGaps - 1);
                        CompletionPercent = TotalSecurities > 0
                            ? Math.Round((TotalSecurities - SecuritiesWithGaps) * 100.0 / TotalSecurities, 1)
                            : 100;
                        UpdateSessionMetrics();
                    }
                    else
                    {
                        ErrorsThisSession++;
                        AddActivity("✗", _currentSecurity.Ticker, result.Error ?? "Load failed");
                    }
                }
                catch (Exception ex)
                {
                    ErrorsThisSession++;
                    AddActivity("✗", _currentSecurity.Ticker, $"Error: {ex.Message}");
                }

                _currentSecurity = null;
                break; // Real work done (or error) — yield back to timer
            }
        }
        catch (Exception ex)
        {
            ErrorsThisSession++;
            AddActivity("✗", "Error", $"Crawler error: {ex.Message}");
            StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            _isProcessing = false;
        }
    }

    private async Task ProcessNextDateAsync()
    {
        // Capture to local variable — _currentSecurity is a shared field that could be
        // mutated by concurrent timer ticks (even with re-entrancy guard, defensive coding).
        var security = _currentSecurity;
        if (security == null || _dateQueue.Count == 0) return;

        var dateStr = _dateQueue.Dequeue();
        CurrentAction = $"Loading {security.Ticker} {dateStr}";

        try
        {
            // Use load-tickers endpoint to load just this ticker for this date
            if (DateTime.TryParse(dateStr, out var date))
            {
                // Update active heatmap cell for pulsing indicator
                ActiveHeatmapYear = date.Year;
                ActiveHeatmapScore = security.ImportanceScore;
                var result = await _apiClient.LoadTickersAsync(
                    [security.Ticker],
                    date,
                    date,
                    _cts?.Token ?? default);

                if (result.Success && result.Data != null)
                {
                    RecordsLoadedThisSession += result.Data.RecordsInserted;
                    _recordsForCurrentSecurity += result.Data.RecordsInserted;

                    // Live-update Price Records card
                    if (_initialTotalRecords > 0)
                        TotalRecordsDisplay = FormatLargeNumber(_initialTotalRecords + RecordsLoadedThisSession);

                    if (result.Data.RecordsInserted > 0)
                    {
                        AddActivity("✓", $"{security.Ticker} {dateStr}", $"{result.Data.RecordsInserted} records");
                        // Reset no-data counter since we got data
                        _consecutiveNoDataCount = 0;

                        // Live-update heatmap cell (pulse timer re-renders at 30fps)
                        UpdateHeatmapCellLocally(date.Year, security.ImportanceScore,
                            result.Data.RecordsInserted, security.IsTracked);
                        HeatmapCellUpdateCounter++;
                    }
                    else
                    {
                        // No data returned - EODHD may not have this ticker
                        _consecutiveNoDataCount++;

                        if (_consecutiveNoDataCount >= NoDataThreshold)
                        {
                            // Mark this security as EODHD unavailable and move on
                            await MarkSecurityUnavailableAsync(security);
                            return;
                        }
                    }

                    TotalMissingDays = Math.Max(0, TotalMissingDays - 1);

                    // Update completion
                    if (_dateQueue.Count == 0)
                    {
                        if (_recordsForCurrentSecurity == 0)
                        {
                            // EODHD returned 0 records for ALL dates. Mark permanently
                            // unavailable so it never appears in gap queries again.
                            _zeroResultAliases.Add(security.SecurityAlias);
                            await MarkSecurityUnavailableAsync(security);
                            // MarkSecurityUnavailableAsync handles all state updates
                        }
                        else
                        {
                            // Done with this security (loaded some records)
                            SecuritiesProcessedThisSession++;
                            SecuritiesWithGaps = Math.Max(0, SecuritiesWithGaps - 1);
                            CompletionPercent = TotalSecurities > 0
                                ? Math.Round((TotalSecurities - SecuritiesWithGaps) * 100.0 / TotalSecurities, 1)
                                : 100;
                            _currentSecurity = null;
                        }
                    }

                    StatusText = $"Loaded {security.Ticker} {dateStr}. {_dateQueue.Count} dates remaining for this security.";
                }
                else
                {
                    ErrorsThisSession++;
                    AddActivity("✗", $"{security.Ticker} {dateStr}", result.Error ?? "Failed");
                }
            }
        }
        catch (Exception ex)
        {
            ErrorsThisSession++;
            AddActivity("✗", $"{security.Ticker} {dateStr}", ex.Message);
        }
    }

    /// <summary>
    /// Updates a heatmap cell in-place for instant visual feedback.
    /// The pulse timer (30fps) re-renders the control, so modified cells appear brighter immediately.
    /// Every HeatmapFullRefreshInterval records, does a full API re-fetch to catch new cells and correct drift.
    /// </summary>
    private void UpdateHeatmapCellLocally(int year, int score, long recordsInserted, bool isTracked)
    {
        var data = HeatmapData;
        if (data?.Cells == null) return;

        // Find existing cell by year + score
        var cell = data.Cells.Find(c => c.Year == year && c.Score == score);
        if (cell != null)
        {
            // Update in-place - the control's _cellLookup references the same object
            if (isTracked)
                cell.TrackedRecords += recordsInserted;
            else
                cell.UntrackedRecords += recordsInserted;
        }
        else
        {
            // Create new cell so newly-covered year/score combos appear immediately
            var newCell = new HeatmapCell
            {
                Year = year,
                Score = score,
                TrackedRecords = isTracked ? recordsInserted : 0,
                UntrackedRecords = isTracked ? 0 : recordsInserted,
                TrackedSecurities = isTracked ? 1 : 0,
                UntrackedSecurities = isTracked ? 0 : 1
            };
            data.Cells.Add(newCell);

            // Update metadata bounds so the control renders the new cell
            if (data.Metadata != null)
            {
                if (year < data.Metadata.MinYear || data.Metadata.MinYear == 0)
                    data.Metadata.MinYear = year;
                if (year > data.Metadata.MaxYear)
                    data.Metadata.MaxYear = year;
                data.Metadata.TotalCells = data.Cells.Count;
            }
        }

        // During active crawling, skip API refreshes — they return 30-minute cached
        // stale data that overwrites our local updates. A full refresh fires on crawl stop.
        if (!IsCrawling)
        {
            _recordsSinceHeatmapRefresh += (int)recordsInserted;
            if (_recordsSinceHeatmapRefresh >= HeatmapFullRefreshInterval)
            {
                _recordsSinceHeatmapRefresh = 0;
                _ = RefreshHeatmapFromApiAsync();
            }
        }
    }

    /// <summary>
    /// Full heatmap re-fetch from API. Catches new cells and re-normalizes max values.
    /// </summary>
    private async Task RefreshHeatmapFromApiAsync()
    {
        try
        {
            var heatmap = await _apiClient.GetHeatmapDataAsync(_cts?.Token ?? default);
            if (heatmap?.Success == true)
            {
                HeatmapData = heatmap;
            }
        }
        catch
        {
            // Non-critical - local updates continue working
        }

        // Refresh server-side stats (price records, securities, tracked counts)
        try
        {
            var stats = await _apiClient.GetDashboardStatsAsync(_cts?.Token ?? default);
            if (stats?.Success == true)
            {
                if (stats.Universe != null)
                {
                    TotalSecuritiesDisplay = stats.Universe.TotalSecurities.ToString("N0");
                    TrackedDisplay = stats.Universe.Tracked.ToString("N0");
                    UntrackedDisplay = stats.Universe.Untracked.ToString("N0");
                    UnavailableDisplay = stats.Universe.Unavailable.ToString("N0");
                }
                if (stats.Prices != null)
                {
                    TotalRecordsDisplay = FormatLargeNumber(stats.Prices.TotalRecords);
                    LatestDateDisplay = stats.Prices.LatestDate ?? "—";
                }

                // Update freshness indicator
                if (!string.IsNullOrEmpty(stats.SummaryLastRefreshed)
                    && DateTime.TryParse(stats.SummaryLastRefreshed, out var refreshedAt))
                {
                    var age = DateTime.UtcNow - refreshedAt;
                    if (age.TotalMinutes < 10)
                        SummaryFreshnessDisplay = "refreshed just now";
                    else if (age.TotalHours < 1)
                        SummaryFreshnessDisplay = $"refreshed {(int)age.TotalMinutes}m ago";
                    else if (age.TotalHours < 24)
                        SummaryFreshnessDisplay = $"refreshed {(int)age.TotalHours}h ago";
                    else
                        SummaryFreshnessDisplay = $"refreshed {(int)age.TotalDays}d ago";
                }
            }
        }
        catch
        {
            // Non-critical
        }
    }

    /// <summary>
    /// Marks a security as EODHD unavailable when we detect no data is returned.
    /// Clears the date queue and moves to the next security.
    /// </summary>
    private async Task MarkSecurityUnavailableAsync(SecurityGapInfo security)
    {
        var trackedLabel = security.IsTracked ? "[T]" : "[U]";
        AddActivity("⚠️", $"{trackedLabel} {security.Ticker}", $"No EODHD data - marking as unavailable");
        StatusText = $"Marking {security.Ticker} as EODHD unavailable...";

        try
        {
            var result = await _apiClient.MarkEodhdUnavailableAsync(security.SecurityAlias, _cts?.Token ?? default);

            if (result.Success)
            {
                AddActivity("🚫", security.Ticker, "Marked as EODHD unavailable - will be skipped");

                // Update universe cards locally (tracked/untracked → unavailable)
                _unavailableCount++;
                if (security.IsTracked)
                    _trackedCount = Math.Max(0, _trackedCount - 1);
                else
                    _untrackedCount = Math.Max(0, _untrackedCount - 1);
                TrackedDisplay = _trackedCount.ToString("N0");
                UntrackedDisplay = _untrackedCount.ToString("N0");
                UnavailableDisplay = _unavailableCount.ToString("N0");
            }
            else
            {
                AddActivity("✗", security.Ticker, $"Failed to mark unavailable: {result.Error}");
                ErrorsThisSession++;
            }
        }
        catch (Exception ex)
        {
            AddActivity("✗", security.Ticker, $"Mark unavailable failed: {ex.Message}");
            ErrorsThisSession++;
        }

        // Clear queued dates and move to next security
        _dateQueue.Clear();
        SecuritiesProcessedThisSession++;
        SecuritiesWithGaps = Math.Max(0, SecuritiesWithGaps - 1);
        CompletionPercent = TotalSecurities > 0
            ? Math.Round((TotalSecurities - SecuritiesWithGaps) * 100.0 / TotalSecurities, 1)
            : 100;
        UpdateSessionMetrics();
        _currentSecurity = null;
        _consecutiveNoDataCount = 0;
    }

    /// <summary>
    /// Updates session metrics: gap delta, processing rates, and display strings.
    /// Called after each ticker is processed or gaps count changes.
    /// </summary>
    private void UpdateSessionMetrics()
    {
        // Gap delta
        var delta = _initialGapsCount - SecuritiesWithGaps;
        IsGapsIncreasing = delta < 0;
        if (delta > 0) GapsDeltaDisplay = $"▼ {delta} this session";
        else if (delta < 0) GapsDeltaDisplay = $"▲ {Math.Abs(delta)} this session";
        else GapsDeltaDisplay = "";

        // Processing rates
        var elapsed = DateTime.Now - _sessionStartedAt;
        if (elapsed.TotalMinutes >= 1)
        {
            var tickerRate = SecuritiesProcessedThisSession / elapsed.TotalHours;
            var recordRate = RecordsLoadedThisSession / elapsed.TotalHours;
            TickerRateDisplay = $"{tickerRate:N0}/hr";
            RecordRateDisplay = $"{recordRate:N0}/hr";
        }

        // Session duration
        if (elapsed.TotalHours >= 1)
            SessionTimeDisplay = $"{(int)elapsed.TotalHours}h {elapsed.Minutes}m";
        else if (elapsed.TotalMinutes >= 1)
            SessionTimeDisplay = $"{(int)elapsed.TotalMinutes}m";
        else
            SessionTimeDisplay = "";

        // Update display strings
        TickersDisplay = SecuritiesProcessedThisSession.ToString("N0");
        TickersSubtitle = TickerRateDisplay != "" ? TickerRateDisplay : "this session";
        RecordsDisplay = RecordsLoadedThisSession.ToString("N0");
        RecordsSubtitle = RecordRateDisplay != "" ? RecordRateDisplay : "this session";

        // Live-update the Price Records card (base count + session inserts)
        if (_initialTotalRecords > 0)
        {
            TotalRecordsDisplay = FormatLargeNumber(_initialTotalRecords + RecordsLoadedThisSession);
        }
    }

    private async Task LoadDashboardStatsAsync()
    {
        try
        {
            var stats = await _apiClient.GetDashboardStatsAsync(_cts?.Token ?? default);
            if (stats == null || !stats.Success) return;

            // Populate metric cards
            if (stats.Universe != null)
            {
                _trackedCount = stats.Universe.Tracked;
                _untrackedCount = stats.Universe.Untracked;
                _unavailableCount = stats.Universe.Unavailable;
                TotalSecuritiesDisplay = stats.Universe.TotalSecurities.ToString("N0");
                TrackedDisplay = _trackedCount.ToString("N0");
                UntrackedDisplay = _untrackedCount.ToString("N0");
                UnavailableDisplay = _unavailableCount.ToString("N0");
            }

            if (stats.Prices != null)
            {
                _initialTotalRecords = stats.Prices.TotalRecords;
                TotalRecordsDisplay = FormatLargeNumber(stats.Prices.TotalRecords);
                LatestDateDisplay = stats.Prices.LatestDate ?? "—";

                if (stats.Prices.OldestDate != null && stats.Prices.LatestDate != null
                    && DateTime.TryParse(stats.Prices.OldestDate, out var oldest)
                    && DateTime.TryParse(stats.Prices.LatestDate, out var latest))
                {
                    var years = (int)((latest - oldest).Days / 365.25);
                    DataSpanDisplay = $"{years}yr ({oldest.Year}-{latest.Year})";
                }
            }

            // CoverageSummary freshness indicator
            if (!string.IsNullOrEmpty(stats.SummaryLastRefreshed)
                && DateTime.TryParse(stats.SummaryLastRefreshed, out var refreshedAt))
            {
                var age = DateTime.UtcNow - refreshedAt;
                if (age.TotalMinutes < 10)
                    SummaryFreshnessDisplay = "refreshed just now";
                else if (age.TotalHours < 1)
                    SummaryFreshnessDisplay = $"refreshed {(int)age.TotalMinutes}m ago";
                else if (age.TotalHours < 24)
                    SummaryFreshnessDisplay = $"refreshed {(int)age.TotalHours}h ago";
                else
                    SummaryFreshnessDisplay = $"refreshed {(int)age.TotalDays}d ago";
            }
            else
            {
                SummaryFreshnessDisplay = "";
            }

            // Load heatmap data (Year x ImportanceScore bivariate coverage)
            IsHeatmapLoading = true;
            try
            {
                var heatmap = await _apiClient.GetHeatmapDataAsync(_cts?.Token ?? default);
                if (heatmap?.Success == true)
                {
                    HeatmapData = heatmap;
                }
            }
            finally
            {
                IsHeatmapLoading = false;
            }
        }
        catch
        {
            // Dashboard stats are non-critical - don't block on failure
        }
    }

    private static string FormatLargeNumber(double value)
    {
        if (value >= 1_000_000) return $"{value / 1_000_000:F1}M";
        if (value >= 1_000) return $"{value / 1_000:F0}K";
        return $"{value:N0}";
    }

    private void AddActivity(string icon, string item, string details)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            ActivityLog.Insert(0, new CrawlActivity
            {
                Icon = icon,
                Date = item,
                Details = details,
                Timestamp = DateTime.Now.ToString("HH:mm:ss")
            });

            // Keep log manageable
            while (ActivityLog.Count > 50)
            {
                ActivityLog.RemoveAt(ActivityLog.Count - 1);
            }
        });
    }
}

public class CrawlActivity
{
    public string Icon { get; set; } = "";
    public string Date { get; set; } = "";
    public string Details { get; set; } = "";
    public string Timestamp { get; set; } = "";
}
