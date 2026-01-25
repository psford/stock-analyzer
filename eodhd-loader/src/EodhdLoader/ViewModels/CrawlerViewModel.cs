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

    // Queue of securities to process
    private Queue<SecurityGapInfo> _securityQueue = new();
    // Current security's missing dates
    private Queue<string> _dateQueue = new();
    private SecurityGapInfo? _currentSecurity;

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

                // Fetch initial gap status
                await RefreshGapsAsync();
                StatusText = $"Connected. {SecuritiesWithGaps} securities with {TotalMissingDays:N0} missing days. Click Start to begin.";
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

    private async Task RefreshGapsAsync()
    {
        var gaps = await _apiClient.GetPriceGapsAsync("US", SecuritiesToFetch, _cts?.Token ?? default);
        if (gaps.Success)
        {
            TotalSecurities = gaps.Summary.TotalSecurities;
            SecuritiesWithGaps = gaps.Summary.SecuritiesWithGaps;
            TotalMissingDays = gaps.Summary.TotalMissingDays;
            CompletionPercent = gaps.CompletionPercent;

            // Queue up the securities with gaps (ordered by most gaps first)
            _securityQueue.Clear();
            foreach (var secGap in gaps.Gaps)
            {
                _securityQueue.Enqueue(secGap);
            }
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

        CurrentAction = "Starting...";
        StatusText = "Crawler starting...";

        // Get initial batch of securities with gaps
        await RefreshGapsAsync();

        if (_securityQueue.Count == 0)
        {
            CurrentAction = "Complete!";
            StatusText = "No gaps found. All Security Master securities have complete price data!";
            IsCrawling = false;
            return;
        }

        // Start the paced timer
        _crawlTimer.Start();
        CurrentAction = "Crawling";
        StatusText = $"Crawling... {_securityQueue.Count} securities queued";
    }

    private bool CanStopCrawl() => IsCrawling;

    [RelayCommand(CanExecute = nameof(CanStopCrawl))]
    private void StopCrawl()
    {
        _crawlTimer.Stop();
        _cts?.Cancel();
        IsCrawling = false;
        CurrentAction = "Stopped";
        StatusText = $"Stopped. Processed {SecuritiesProcessedThisSession} securities, loaded {RecordsLoadedThisSession:N0} records.";
    }

    private async Task ProcessNextAsync()
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
            await RefreshGapsAsync();

            if (_securityQueue.Count == 0)
            {
                // No more gaps - we're done!
                _crawlTimer.Stop();
                IsCrawling = false;
                CurrentAction = "Complete!";
                StatusText = $"All gaps filled! Processed {SecuritiesProcessedThisSession} securities, loaded {RecordsLoadedThisSession:N0} records.";
                AddActivity("✓", "Complete", "All Security Master securities have complete price data");
                return;
            }
        }

        // Get next security
        _currentSecurity = _securityQueue.Dequeue();
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
            StatusText = $"{_currentSecurity.Ticker}: {_dateQueue.Count} dates to load";
        }
        else
        {
            // No gaps for this security (maybe already filled)
            SecuritiesProcessedThisSession++;
            _currentSecurity = null;
        }
    }

    private async Task ProcessNextDateAsync()
    {
        if (_currentSecurity == null || _dateQueue.Count == 0) return;

        var dateStr = _dateQueue.Dequeue();
        CurrentAction = $"Loading {_currentSecurity.Ticker} {dateStr}";

        try
        {
            // Use load-tickers endpoint to load just this ticker for this date
            if (DateTime.TryParse(dateStr, out var date))
            {
                var result = await _apiClient.LoadTickersAsync(
                    [_currentSecurity.Ticker],
                    date,
                    date,
                    _cts?.Token ?? default);

                if (result.Success && result.Data != null)
                {
                    RecordsLoadedThisSession += result.Data.PricesLoaded;

                    if (result.Data.PricesLoaded > 0)
                    {
                        AddActivity("✓", $"{_currentSecurity.Ticker} {dateStr}", $"{result.Data.PricesLoaded} records");
                    }

                    TotalMissingDays = Math.Max(0, TotalMissingDays - 1);

                    // Update completion
                    if (_dateQueue.Count == 0)
                    {
                        // Done with this security
                        SecuritiesProcessedThisSession++;
                        SecuritiesWithGaps = Math.Max(0, SecuritiesWithGaps - 1);
                        CompletionPercent = TotalSecurities > 0
                            ? Math.Round((TotalSecurities - SecuritiesWithGaps) * 100.0 / TotalSecurities, 1)
                            : 100;
                        _currentSecurity = null;
                    }

                    StatusText = $"Loaded {_currentSecurity?.Ticker ?? "?"} {dateStr}. {_dateQueue.Count} dates remaining for this security.";
                }
                else
                {
                    ErrorsThisSession++;
                    AddActivity("✗", $"{_currentSecurity.Ticker} {dateStr}", result.Error ?? "Failed");
                }
            }
        }
        catch (Exception ex)
        {
            ErrorsThisSession++;
            AddActivity("✗", $"{_currentSecurity.Ticker} {dateStr}", ex.Message);
        }
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
