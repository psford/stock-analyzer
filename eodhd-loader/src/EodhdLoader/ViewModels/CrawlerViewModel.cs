using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EodhdLoader.Services;

namespace EodhdLoader.ViewModels;

/// <summary>
/// ViewModel for the Crawler - an autonomous gap-filling agent.
/// Finds missing trading days and loads price data from EODHD.
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
    private const int BatchSize = 10; // Dates to fetch per gaps query

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

    // Progress tracking
    [ObservableProperty]
    private int _totalMissingDays;

    [ObservableProperty]
    private int _daysWithData;

    [ObservableProperty]
    private int _daysLoadedThisSession;

    [ObservableProperty]
    private double _completionPercent;

    [ObservableProperty]
    private string _currentAction = "Idle";

    [ObservableProperty]
    private string _currentDate = "—";

    [ObservableProperty]
    private int _recordsLoadedThisSession;

    [ObservableProperty]
    private int _errorsThisSession;

    // Activity log
    [ObservableProperty]
    private ObservableCollection<CrawlActivity> _activityLog = [];

    // Queue of dates to process
    private Queue<string> _dateQueue = new();

    public CrawlerViewModel(StockAnalyzerApiClient apiClient)
    {
        _apiClient = apiClient;

        // Timer for paced API calls
        _crawlTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(CallIntervalMs)
        };
        _crawlTimer.Tick += async (s, e) => await ProcessNextDateAsync();
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
                StatusText = $"Connected. {TotalMissingDays:N0} missing days found. Click Start to begin.";
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
        var gaps = await _apiClient.GetPriceGapsAsync("US", BatchSize, _cts?.Token ?? default);
        if (gaps.Success)
        {
            TotalMissingDays = gaps.TotalMissingDays;
            DaysWithData = gaps.DaysWithData;
            CompletionPercent = gaps.CompletionPercent;

            // Queue up the missing dates
            _dateQueue.Clear();
            foreach (var date in gaps.MissingDates)
            {
                _dateQueue.Enqueue(date);
            }
        }
    }

    private bool CanStartCrawl() => IsConnected && !IsCrawling;

    [RelayCommand(CanExecute = nameof(CanStartCrawl))]
    private async Task StartCrawlAsync()
    {
        IsCrawling = true;
        _cts = new CancellationTokenSource();
        DaysLoadedThisSession = 0;
        RecordsLoadedThisSession = 0;
        ErrorsThisSession = 0;

        CurrentAction = "Starting...";
        StatusText = "Crawler starting...";

        // Get initial batch of missing dates
        await RefreshGapsAsync();

        if (_dateQueue.Count == 0)
        {
            CurrentAction = "Complete!";
            StatusText = "No missing dates found. Coverage is complete!";
            IsCrawling = false;
            return;
        }

        // Start the paced timer
        _crawlTimer.Start();
        CurrentAction = "Crawling";
        StatusText = $"Crawling... {_dateQueue.Count} dates queued";
    }

    private bool CanStopCrawl() => IsCrawling;

    [RelayCommand(CanExecute = nameof(CanStopCrawl))]
    private void StopCrawl()
    {
        _crawlTimer.Stop();
        _cts?.Cancel();
        IsCrawling = false;
        CurrentAction = "Stopped";
        StatusText = $"Stopped. Loaded {DaysLoadedThisSession} days this session.";
    }

    private async Task ProcessNextDateAsync()
    {
        if (_cts?.Token.IsCancellationRequested == true)
        {
            _crawlTimer.Stop();
            return;
        }

        // If queue is empty, refresh from API
        if (_dateQueue.Count == 0)
        {
            CurrentAction = "Refreshing gaps...";
            await RefreshGapsAsync();

            if (_dateQueue.Count == 0)
            {
                // No more gaps - we're done!
                _crawlTimer.Stop();
                IsCrawling = false;
                CurrentAction = "Complete!";
                StatusText = $"All gaps filled! Loaded {DaysLoadedThisSession} days.";
                AddActivity("✓", "Complete", "All trading days have price data");
                return;
            }
        }

        // Get next date to process
        var dateStr = _dateQueue.Dequeue();
        CurrentDate = dateStr;
        CurrentAction = $"Loading {dateStr}";

        try
        {
            // Parse date and call refresh API
            if (DateTime.TryParse(dateStr, out var date))
            {
                var result = await _apiClient.RefreshDateAsync(date, _cts?.Token ?? default);

                if (result.Success && result.Data != null)
                {
                    DaysLoadedThisSession++;
                    RecordsLoadedThisSession += result.Data.PricesLoaded;
                    DaysWithData++;
                    TotalMissingDays = Math.Max(0, TotalMissingDays - 1);
                    CompletionPercent = (DaysWithData + TotalMissingDays) > 0
                        ? Math.Round(DaysWithData * 100.0 / (DaysWithData + TotalMissingDays), 1)
                        : 100;

                    AddActivity("✓", dateStr, $"{result.Data.PricesLoaded} records");
                    StatusText = $"Loaded {dateStr}: {result.Data.PricesLoaded} records. {_dateQueue.Count} queued.";
                }
                else
                {
                    ErrorsThisSession++;
                    AddActivity("✗", dateStr, result.Error ?? "Failed");
                    StatusText = $"Error on {dateStr}: {result.Error}";
                }
            }
        }
        catch (Exception ex)
        {
            ErrorsThisSession++;
            AddActivity("✗", dateStr, ex.Message);
            StatusText = $"Error: {ex.Message}";
        }
    }

    private void AddActivity(string icon, string date, string details)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            ActivityLog.Insert(0, new CrawlActivity
            {
                Icon = icon,
                Date = date,
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
