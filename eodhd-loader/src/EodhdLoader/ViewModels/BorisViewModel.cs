using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EodhdLoader.Services;

namespace EodhdLoader.ViewModels;

/// <summary>
/// ViewModel for Boris the Spider - the intelligent price data loader.
/// </summary>
public partial class BorisViewModel : ViewModelBase
{
    private readonly BorisService _boris;
    private readonly ConfigurationService _config;
    private readonly StockAnalyzerApiClient _apiClient;
    private readonly PriceCoverageAnalyzer _coverageAnalyzer;
    private readonly HolidayForwardFillService _holidayFillService;
    private readonly ProdSyncService _prodSyncService;

    [ObservableProperty]
    private TargetEnvironment _selectedEnvironment = TargetEnvironment.Local;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartBorisCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopBorisCommand))]
    private bool _isRunning;

    [ObservableProperty]
    private int _dailyBudget = 3000;

    [ObservableProperty]
    private int _callsUsedToday;

    [ObservableProperty]
    private int _daysProcessed;

    [ObservableProperty]
    private int _totalDaysQueued;

    [ObservableProperty]
    private DateTime _currentDate;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string _connectionStatus = "Not Connected";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartBorisCommand))]
    private bool _isConnected;

    [ObservableProperty]
    private string _apiUrl = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AnalyzeCoverageCommand))]
    [NotifyCanExecuteChangedFor(nameof(FillHolidaysCommand))]
    private bool _isAnalyzing;

    [ObservableProperty]
    private string _coverageReport = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FillHolidaysCommand))]
    private bool _isFillingHolidays;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SyncFromProdCommand))]
    private bool _isSyncing;

    [ObservableProperty]
    private string _prodStatus = "";

    [ObservableProperty]
    private string _localStatus = "";

    public ObservableCollection<string> Log { get; } = new();

    public BorisViewModel(
        BorisService boris,
        ConfigurationService config,
        StockAnalyzerApiClient apiClient,
        PriceCoverageAnalyzer coverageAnalyzer,
        HolidayForwardFillService holidayFillService,
        ProdSyncService prodSyncService)
    {
        _boris = boris;
        _config = config;
        _apiClient = apiClient;
        _coverageAnalyzer = coverageAnalyzer;
        _holidayFillService = holidayFillService;
        _prodSyncService = prodSyncService;

        // Subscribe to Boris events
        _boris.LogMessage += (_, message) =>
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                Log.Insert(0, message);
                if (Log.Count > 500) Log.RemoveAt(Log.Count - 1);
            });
        };

        UpdateApiUrl();
    }

    partial void OnSelectedEnvironmentChanged(TargetEnvironment value)
    {
        _apiClient.CurrentEnvironment = value;
        UpdateApiUrl();
        IsConnected = false;
        ConnectionStatus = "Not Connected";
    }

    partial void OnDailyBudgetChanged(int value)
    {
        _boris.SetDailyBudget(value);
    }

    private void UpdateApiUrl()
    {
        ApiUrl = _config.GetApiUrl(SelectedEnvironment);
    }

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        IsBusy = true;
        ConnectionStatus = "Testing...";

        try
        {
            var connected = await _apiClient.TestConnectionAsync();
            IsConnected = connected;
            ConnectionStatus = connected ? "Connected ✓" : "Connection Failed ✗";

            if (connected)
            {
                AddLog($"✓ Connected to {ApiUrl}");
            }
            else
            {
                AddLog($"✗ Could not connect to {ApiUrl}");
            }
        }
        catch (Exception ex)
        {
            IsConnected = false;
            ConnectionStatus = $"Error: {ex.Message}";
            AddLog($"✗ Connection error: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanStartBoris))]
    private async Task StartBorisAsync()
    {
        if (IsRunning) return;

        // Validate API URL is configured
        if (string.IsNullOrWhiteSpace(ApiUrl))
        {
            AddLog($"❌ API URL not configured for {SelectedEnvironment}");
            MessageBox.Show(
                $"API URL is not configured for {SelectedEnvironment}.\n\n" +
                "Please set the PROD_API_URL environment variable.",
                "Configuration Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        // Check if targeting production by URL (more reliable than enum comparison)
        var isProduction = ApiUrl.Contains("psfordtaurus.com", StringComparison.OrdinalIgnoreCase) ||
                           ApiUrl.Contains("azure", StringComparison.OrdinalIgnoreCase);

        AddLog($"Environment: {SelectedEnvironment} (value={(int)SelectedEnvironment}), isProduction={isProduction}");

        // Production confirmation - check both enum AND URL to be safe
        if (isProduction)
        {
            var result = MessageBox.Show(
                $"⚠️ PRODUCTION MODE\n\n" +
                $"Boris will load data to PRODUCTION database.\n" +
                $"Target: {ApiUrl}\n" +
                $"Daily budget: {DailyBudget} API calls\n\n" +
                $"Continue?",
                "Confirm Production Run",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
            {
                AddLog("Production run cancelled by user");
                return;
            }
        }

        IsRunning = true;
        DaysProcessed = 0;
        TotalDaysQueued = 0;
        CallsUsedToday = _boris.CallsUsedToday;

        AddLog($"🕷️ Starting Boris in {SelectedEnvironment} mode...");

        var progressHandler = new Progress<BorisProgress>(p =>
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                CurrentDate = p.CurrentDate;
                DaysProcessed = p.DaysProcessed;
                TotalDaysQueued = p.TotalDaysQueued;
                CallsUsedToday = p.CallsUsedToday;
                Progress = p.PercentComplete;
            });
        });

        try
        {
            await _boris.StartAsync(SelectedEnvironment, progressHandler);
        }
        finally
        {
            IsRunning = false;
        }
    }

    private bool CanStartBoris() => !IsRunning && IsConnected;

    [RelayCommand(CanExecute = nameof(CanStopBoris))]
    private void StopBoris()
    {
        AddLog("🛑 Stopping Boris...");
        _boris.Stop();
    }

    private bool CanStopBoris() => IsRunning;

    [RelayCommand]
    private void ClearLog()
    {
        Log.Clear();
        AddLog("Log cleared");
    }

    [RelayCommand(CanExecute = nameof(CanAnalyzeCoverage))]
    private async Task AnalyzeCoverageAsync()
    {
        if (IsAnalyzing) return;

        IsAnalyzing = true;
        CoverageReport = "";
        AddLog("🔍 Analyzing price coverage...");

        try
        {
            var progressHandler = new Progress<string>(msg => AddLog(msg));
            var report = await _coverageAnalyzer.AnalyzeCoverageAsync(SelectedEnvironment, progressHandler);

            CoverageReport = report.GetSummary();

            if (report.Success)
            {
                AddLog($"✓ Analysis complete: {report.DatesWithData}/{report.ExpectedTradingDays} days ({report.OverallCoveragePercent:F1}% coverage)");
                AddLog($"  Missing: {report.TotalMissingDays} trading days");

                if (report.Tier1_Last30Days?.MissingDays > 0)
                    AddLog($"  ⚠️ Critical: {report.Tier1_Last30Days.MissingDays} missing in last 30 days");
            }
            else
            {
                AddLog($"✗ Analysis failed: {report.Error}");
            }
        }
        catch (Exception ex)
        {
            AddLog($"✗ Analysis error: {ex.Message}");
        }
        finally
        {
            IsAnalyzing = false;
        }
    }

    private bool CanAnalyzeCoverage() => !IsAnalyzing && !IsRunning && !IsFillingHolidays;

    [RelayCommand(CanExecute = nameof(CanFillHolidays))]
    private async Task FillHolidaysAsync()
    {
        if (IsFillingHolidays) return;

        // Confirm action
        var confirmResult = MessageBox.Show(
            $"This will forward-fill price data for US market holidays in {SelectedEnvironment}.\n\n" +
            "For each holiday, the prior trading day's Close price will be copied as OHLC with Volume=0.\n\n" +
            "Continue?",
            "Fill Holiday Prices",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirmResult != MessageBoxResult.Yes) return;

        IsFillingHolidays = true;
        AddLog("📅 Analyzing holidays...");

        try
        {
            var progressHandler = new Progress<string>(msg => AddLog(msg));

            // First analyze
            var analysis = await _holidayFillService.AnalyzeHolidaysAsync(SelectedEnvironment, progressHandler);

            if (!analysis.Success)
            {
                AddLog($"✗ Analysis failed: {analysis.Error}");
                return;
            }

            var toFill = analysis.HolidaysWithPriorData;
            if (toFill == 0)
            {
                AddLog("✓ No holidays need forward-fill");
                return;
            }

            AddLog($"Found {analysis.MissingHolidays.Count} holidays missing data ({toFill} with prior data available)");

            // Confirm fill
            var fillResult = MessageBox.Show(
                $"Found {toFill} holidays that can be forward-filled.\n\n" +
                $"This will insert approximately {toFill * 500:N0} records (estimates vary by securities).\n\n" +
                "Proceed with forward-fill?",
                "Confirm Forward-Fill",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (fillResult != MessageBoxResult.Yes)
            {
                AddLog("Forward-fill cancelled by user");
                return;
            }

            // Execute fill
            AddLog("📅 Forward-filling holidays...");
            var result = await _holidayFillService.ForwardFillHolidaysAsync(SelectedEnvironment, progressHandler);

            if (result.Success)
            {
                AddLog($"✓ {result.Message}");
            }
            else
            {
                AddLog($"✗ Forward-fill failed: {result.Error}");
            }
        }
        catch (Exception ex)
        {
            AddLog($"✗ Error: {ex.Message}");
        }
        finally
        {
            IsFillingHolidays = false;
        }
    }

    private bool CanFillHolidays() => !IsFillingHolidays && !IsRunning && !IsAnalyzing;

    [RelayCommand(CanExecute = nameof(CanSyncFromProd))]
    private async Task SyncFromProdAsync()
    {
        if (IsSyncing) return;

        // Must be in Local environment to sync FROM prod TO local
        if (SelectedEnvironment != TargetEnvironment.Local)
        {
            MessageBox.Show(
                "Sync from Production can only be used when targeting Local database.\n\n" +
                "This feature pulls data FROM production and inserts it INTO the local database.",
                "Environment Mismatch",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        IsSyncing = true;
        AddLog("🔄 Checking sync status...");

        try
        {
            // Get status from both environments
            var prodStatus = await _prodSyncService.GetProductionStatusAsync();
            var localStatus = await _prodSyncService.GetLocalStatusAsync();

            if (!prodStatus.Success)
            {
                AddLog($"✗ Could not connect to production: {prodStatus.Error}");
                return;
            }

            if (!prodStatus.HasData)
            {
                AddLog("✗ No data available on production");
                return;
            }

            ProdStatus = $"Prod: {prodStatus.TotalPriceRecords:N0} prices, {prodStatus.DistinctSecurities:N0} securities ({prodStatus.StartDate} to {prodStatus.EndDate})";
            LocalStatus = localStatus.HasData
                ? $"Local: {localStatus.TotalPriceRecords:N0} prices, {localStatus.DistinctSecurities:N0} securities ({localStatus.StartDate} to {localStatus.EndDate})"
                : "Local: No data";

            AddLog(ProdStatus);
            AddLog(LocalStatus);

            // Calculate what would be synced
            var recordDiff = prodStatus.TotalPriceRecords - (localStatus.HasData ? localStatus.TotalPriceRecords : 0);

            // Confirm sync
            var confirmResult = MessageBox.Show(
                $"Sync data FROM Production TO Local database?\n\n" +
                $"Production:\n" +
                $"  • {prodStatus.TotalPriceRecords:N0} price records\n" +
                $"  • {prodStatus.DistinctSecurities:N0} securities\n" +
                $"  • {prodStatus.StartDate} to {prodStatus.EndDate}\n\n" +
                $"Local:\n" +
                $"  • {(localStatus.HasData ? $"{localStatus.TotalPriceRecords:N0}" : "0")} price records\n\n" +
                $"Estimated new records: ~{Math.Max(0, recordDiff):N0}\n\n" +
                "This may take several minutes for large datasets.",
                "Confirm Sync from Production",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirmResult != MessageBoxResult.Yes)
            {
                AddLog("Sync cancelled by user");
                return;
            }

            // Execute full sync
            AddLog("🔄 Starting sync from production...");
            var progressHandler = new Progress<string>(msg => AddLog(msg));

            var result = await _prodSyncService.FullSyncAsync(
                startDate: null, // Sync all available data
                endDate: null,
                progress: progressHandler);

            if (result.Success)
            {
                AddLog($"✓ {result.Message}");

                // Refresh local status
                var newLocalStatus = await _prodSyncService.GetLocalStatusAsync();
                if (newLocalStatus.Success && newLocalStatus.HasData)
                {
                    LocalStatus = $"Local: {newLocalStatus.TotalPriceRecords:N0} prices, {newLocalStatus.DistinctSecurities:N0} securities ({newLocalStatus.StartDate} to {newLocalStatus.EndDate})";
                }
            }
            else
            {
                AddLog($"✗ Sync failed: {result.Error}");
            }
        }
        catch (Exception ex)
        {
            AddLog($"✗ Error: {ex.Message}");
        }
        finally
        {
            IsSyncing = false;
        }
    }

    private bool CanSyncFromProd() => !IsSyncing && !IsRunning && !IsAnalyzing && !IsFillingHolidays;

    private void AddLog(string message)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            Log.Insert(0, $"[{DateTime.Now:HH:mm:ss}] {message}");
            if (Log.Count > 500) Log.RemoveAt(Log.Count - 1);
        });
    }
}
