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

    public CrawlerViewModel(StockAnalyzerApiClient apiClient)
    {
        _apiClient = apiClient;

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

        var result = await _apiClient.PromoteUntrackedAsync(100, _cts?.Token ?? default);

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
        CurrentPhase = "Crawling";
        return await RefreshGapsAsync();
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

        CurrentPhase = "Crawling";
        CurrentAction = "Starting...";
        StatusText = "Crawler starting...";

        // Get initial batch of tracked securities with gaps
        var ok = await RefreshGapsAsync();

        if (!ok)
        {
            CurrentAction = "Error";
            IsCrawling = false;
            return;
        }

        if (_securityQueue.Count == 0)
        {
            CurrentAction = "Complete!";
            StatusText = "No gaps found. All tracked securities have complete price data.";
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
                    // All tracked gaps filled — stop. User must manually promote
                    // untracked securities when ready.
                    _crawlTimer.Stop();
                    IsCrawling = false;
                    CurrentAction = "Complete!";
                    CurrentPhase = "Complete";
                    StatusText = $"All tracked gaps filled! Processed {SecuritiesProcessedThisSession} securities, loaded {RecordsLoadedThisSession:N0} records.";
                    AddActivity("✓", "Complete", "All tracked securities have complete price data");
                    return;
                }
            }

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

                // All tracked gaps are either filled or in the skip set. Stop.
                _crawlTimer.Stop();
                IsCrawling = false;
                CurrentAction = "Complete!";
                CurrentPhase = "Complete";
                StatusText = $"All tracked gaps filled! Processed {SecuritiesProcessedThisSession} securities, loaded {RecordsLoadedThisSession:N0} records.";
                AddActivity("✓", "Complete", "All tracked securities have complete price data");
                return;
            }

            _currentSecurity = nextSecurity;
            CurrentTicker = _currentSecurity.Ticker;

            CurrentAction = $"Analyzing {_currentSecurity.Ticker}...";

            // Fetch the specific missing dates for this security
            var secGaps = await _apiClient.GetSecurityGapsAsync(_currentSecurity.SecurityAlias, 100, _cts?.Token ?? default);
            if (secGaps.Success && secGaps.MissingDates.Count > 0)
            {
                _dateQueue.Clear();
                foreach (var date in secGaps.MissingDates)
                {
                    _dateQueue.Enqueue(date);
                }

                AddActivity("📊", _currentSecurity.Ticker, $"{secGaps.MissingCount} missing dates ({secGaps.FirstDate} - {secGaps.LastDate})");
                StatusText = $"Crawling: {_currentSecurity.Ticker} - {_dateQueue.Count} dates to load";
                _recordsForCurrentSecurity = 0;
                // Only reset no-data counter when switching to a different security
                if (_lastNoDataAlias != _currentSecurity.SecurityAlias)
                {
                    _consecutiveNoDataCount = 0;
                    _lastNoDataAlias = _currentSecurity.SecurityAlias;
                }
            }
            else
            {
                // No gaps for this security (maybe already filled)
                SecuritiesProcessedThisSession++;
                _currentSecurity = null;
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

                    if (result.Data.RecordsInserted > 0)
                    {
                        AddActivity("✓", $"{security.Ticker} {dateStr}", $"{result.Data.RecordsInserted} records");
                        // Reset no-data counter since we got data
                        _consecutiveNoDataCount = 0;

                        // Live-update heatmap cell (pulse timer re-renders at 30fps)
                        UpdateHeatmapCellLocally(date.Year, security.ImportanceScore,
                            result.Data.RecordsInserted, security.IsTracked);
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
        // If cell doesn't exist, the periodic full refresh will pick it up

        // Periodic full refresh from API to catch new cells and re-normalize
        _recordsSinceHeatmapRefresh += (int)recordsInserted;
        if (_recordsSinceHeatmapRefresh >= HeatmapFullRefreshInterval)
        {
            _recordsSinceHeatmapRefresh = 0;
            _ = RefreshHeatmapFromApiAsync();
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
        _currentSecurity = null;
        _consecutiveNoDataCount = 0;
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
                TotalSecuritiesDisplay = stats.Universe.TotalSecurities.ToString("N0");
                TrackedDisplay = stats.Universe.Tracked.ToString("N0");
                UntrackedDisplay = stats.Universe.Untracked.ToString("N0");
                UnavailableDisplay = stats.Universe.Unavailable.ToString("N0");
            }

            if (stats.Prices != null)
            {
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
