using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EodhdLoader.Services;

namespace EodhdLoader.ViewModels;

/// <summary>
/// ViewModel for Crawler Mode - a production data monitoring dashboard.
/// Shows live stats about how much historical price data has been loaded,
/// with animated counters, progress bars by decade, and activity feed.
/// </summary>
public partial class CrawlerViewModel : ViewModelBase
{
    private readonly StockAnalyzerApiClient _apiClient;
    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _animationTimer;
    private CancellationTokenSource? _cancellationTokenSource;

    // Animation state
    private int _targetTotalRecords;
    private int _targetDistinctSecurities;
    private int _targetDistinctDates;
    private int _animatedTotalRecords;
    private int _animatedDistinctSecurities;
    private int _animatedDistinctDates;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ToggleMonitoringCommand))]
    private bool _isMonitoring;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ToggleMonitoringCommand))]
    private bool _isConnected;

    [ObservableProperty]
    private string _connectionStatus = "Not Connected";

    [ObservableProperty]
    private string _statusText = "Click Test Connection, then Start to begin monitoring.";

    // Animated counters (these animate toward targets)
    [ObservableProperty]
    private string _totalRecordsDisplay = "—";

    [ObservableProperty]
    private string _distinctSecuritiesDisplay = "—";

    [ObservableProperty]
    private string _distinctDatesDisplay = "—";

    // Static stats
    [ObservableProperty]
    private string _dateRange = "—";

    [ObservableProperty]
    private int _yearsOfData;

    [ObservableProperty]
    private int _avgRecordsPerDay;

    // Decade progress
    [ObservableProperty]
    private ObservableCollection<DecadeProgress> _decades = [];

    // Activity feed
    [ObservableProperty]
    private ObservableCollection<ActivityItem> _activityFeed = [];

    [ObservableProperty]
    private int _refreshIntervalSeconds = 30;

    [ObservableProperty]
    private string _lastRefresh = "Never";

    [ObservableProperty]
    private TargetEnvironment _selectedEnvironment = TargetEnvironment.Local;

    public CrawlerViewModel(StockAnalyzerApiClient apiClient)
    {
        _apiClient = apiClient;

        // Set up auto-refresh timer
        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(30)
        };
        _refreshTimer.Tick += async (s, e) => await RefreshStatsAsync();

        // Set up animation timer (smooth counter updates)
        _animationTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };
        _animationTimer.Tick += AnimateCounters;
    }

    private void AnimateCounters(object? sender, EventArgs e)
    {
        var changed = false;

        // Animate total records
        if (_animatedTotalRecords != _targetTotalRecords)
        {
            var diff = _targetTotalRecords - _animatedTotalRecords;
            var step = Math.Max(1, Math.Abs(diff) / 20);
            _animatedTotalRecords += diff > 0 ? step : -step;
            if (Math.Abs(_targetTotalRecords - _animatedTotalRecords) < step)
                _animatedTotalRecords = _targetTotalRecords;
            TotalRecordsDisplay = _animatedTotalRecords.ToString("N0");
            changed = true;
        }

        // Animate securities
        if (_animatedDistinctSecurities != _targetDistinctSecurities)
        {
            var diff = _targetDistinctSecurities - _animatedDistinctSecurities;
            var step = Math.Max(1, Math.Abs(diff) / 20);
            _animatedDistinctSecurities += diff > 0 ? step : -step;
            if (Math.Abs(_targetDistinctSecurities - _animatedDistinctSecurities) < step)
                _animatedDistinctSecurities = _targetDistinctSecurities;
            DistinctSecuritiesDisplay = _animatedDistinctSecurities.ToString("N0");
            changed = true;
        }

        // Animate dates
        if (_animatedDistinctDates != _targetDistinctDates)
        {
            var diff = _targetDistinctDates - _animatedDistinctDates;
            var step = Math.Max(1, Math.Abs(diff) / 20);
            _animatedDistinctDates += diff > 0 ? step : -step;
            if (Math.Abs(_targetDistinctDates - _animatedDistinctDates) < step)
                _animatedDistinctDates = _targetDistinctDates;
            DistinctDatesDisplay = _animatedDistinctDates.ToString("N0");
            changed = true;
        }

        // Stop animation when done
        if (!changed)
        {
            _animationTimer.Stop();
        }
    }

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        IsBusy = true;
        ConnectionStatus = "Testing...";
        var envName = SelectedEnvironment == TargetEnvironment.Production ? "Production" : "Local";
        StatusText = $"Testing connection to {envName} API...";

        try
        {
            _apiClient.CurrentEnvironment = SelectedEnvironment;
            var connected = await _apiClient.TestConnectionAsync();

            if (connected)
            {
                IsConnected = true;
                ConnectionStatus = $"Connected ({envName})";
                StatusText = $"Connected to {envName}. Click Start to begin monitoring.";
            }
            else
            {
                IsConnected = false;
                ConnectionStatus = "Failed";
                StatusText = $"Could not connect to {envName} API.";
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

    private bool CanToggleMonitoring() => IsConnected || IsMonitoring;

    [RelayCommand(CanExecute = nameof(CanToggleMonitoring))]
    private async Task ToggleMonitoringAsync()
    {
        if (IsMonitoring)
        {
            // Stop
            _refreshTimer.Stop();
            _animationTimer.Stop();
            _cancellationTokenSource?.Cancel();
            IsMonitoring = false;
            StatusText = "Monitoring stopped.";
        }
        else
        {
            // Start
            IsMonitoring = true;
            _cancellationTokenSource = new CancellationTokenSource();
            StatusText = "Starting monitor...";

            // Initial fetch
            await RefreshStatsAsync();

            // Start auto-refresh
            _refreshTimer.Interval = TimeSpan.FromSeconds(RefreshIntervalSeconds);
            _refreshTimer.Start();
        }
    }

    [RelayCommand]
    private async Task RefreshNowAsync()
    {
        await RefreshStatsAsync();
    }

    private async Task RefreshStatsAsync()
    {
        if (_cancellationTokenSource?.Token.IsCancellationRequested == true)
            return;

        try
        {
            StatusText = "Fetching stats...";
            _apiClient.CurrentEnvironment = SelectedEnvironment;

            var result = await _apiClient.GetPriceMonitorStatsAsync(_cancellationTokenSource?.Token ?? default);

            if (!result.Success)
            {
                StatusText = $"Error: {result.Error}";
                return;
            }

            if (!result.HasData)
            {
                StatusText = "No data in production database yet.";
                return;
            }

            // Update targets (animation will smooth the transition)
            _targetTotalRecords = result.TotalRecords;
            _targetDistinctSecurities = result.DistinctSecurities;
            _targetDistinctDates = result.DistinctDates;
            _animationTimer.Start();

            // Update static stats
            DateRange = $"{result.StartDate} to {result.EndDate}";
            YearsOfData = result.YearsOfData;
            AvgRecordsPerDay = result.AvgRecordsPerDay;

            // Update decade progress
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                Decades.Clear();
                var maxRecords = result.CoverageByDecade.Count > 0
                    ? result.CoverageByDecade.Max(d => d.Records)
                    : 1;

                foreach (var decade in result.CoverageByDecade)
                {
                    Decades.Add(new DecadeProgress
                    {
                        Label = decade.Decade,
                        Records = decade.Records,
                        TradingDays = decade.TradingDays,
                        Progress = maxRecords > 0 ? (decade.Records * 100.0) / maxRecords : 0
                    });
                }

                // Update activity feed
                ActivityFeed.Clear();
                foreach (var activity in result.RecentActivity)
                {
                    ActivityFeed.Add(new ActivityItem
                    {
                        Date = activity.Date,
                        LoadedAt = activity.LoadedAt
                    });
                }
            });

            LastRefresh = DateTime.Now.ToString("HH:mm:ss");
            StatusText = $"Last updated: {LastRefresh}. Auto-refresh every {RefreshIntervalSeconds}s.";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Refresh cancelled.";
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
    }
}

public class DecadeProgress
{
    public string Label { get; set; } = "";
    public int Records { get; set; }
    public int TradingDays { get; set; }
    public double Progress { get; set; }
}

public class ActivityItem
{
    public string Date { get; set; } = "";
    public string LoadedAt { get; set; } = "";
}
