using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EodhdLoader.Services;

namespace EodhdLoader.ViewModels;

/// <summary>
/// ViewModel for testing bulk-by-date gap filling approach.
/// This is a TEST tab - the bulk fill strategy uses 1 API call per date
/// to get ALL tickers, rather than 1 API call per ticker.
/// </summary>
public partial class BulkFillViewModel : ViewModelBase
{
    private readonly BulkFillService _bulkFillService;
    private readonly ConfigurationService _config;
    private readonly StockAnalyzerApiClient _apiClient;

    private List<DateTime> _missingDates = new();

    [ObservableProperty]
    private TargetEnvironment _selectedEnvironment = TargetEnvironment.Local;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartBulkFillCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopBulkFillCommand))]
    private bool _isRunning;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AnalyzeGapsCommand))]
    [NotifyCanExecuteChangedFor(nameof(StartBulkFillCommand))]
    private bool _isAnalyzing;

    [ObservableProperty]
    private string _connectionStatus = "Not Connected";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartBulkFillCommand))]
    private bool _isConnected;

    [ObservableProperty]
    private string _apiUrl = "";

    [ObservableProperty]
    private int _maxDatesToProcess = 30;

    // Analysis results
    [ObservableProperty]
    private string _analysisReport = "";

    [ObservableProperty]
    private int _existingDates;

    [ObservableProperty]
    private int _expectedDates;

    [ObservableProperty]
    private int _missingDateCount;

    [ObservableProperty]
    private double _coveragePercent;

    // Progress tracking
    [ObservableProperty]
    private int _datesProcessed;

    [ObservableProperty]
    private int _totalDatesToProcess;

    [ObservableProperty]
    private int _recordsInserted;

    [ObservableProperty]
    private int _apiCallsMade;

    [ObservableProperty]
    private DateTime _currentDate;

    [ObservableProperty]
    private double _progress;

    public ObservableCollection<string> Log { get; } = new();

    public BulkFillViewModel(
        BulkFillService bulkFillService,
        ConfigurationService config,
        StockAnalyzerApiClient apiClient)
    {
        _bulkFillService = bulkFillService;
        _config = config;
        _apiClient = apiClient;

        // Subscribe to service events
        _bulkFillService.LogMessage += (_, message) =>
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
        AnalysisReport = "";
        _missingDates.Clear();
        MissingDateCount = 0;
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

    [RelayCommand(CanExecute = nameof(CanAnalyzeGaps))]
    private async Task AnalyzeGapsAsync()
    {
        if (IsAnalyzing) return;

        IsAnalyzing = true;
        AnalysisReport = "";
        _missingDates.Clear();
        AddLog("🔍 Analyzing gaps for bulk fill...");

        try
        {
            var analysis = await _bulkFillService.AnalyzeGapsAsync(SelectedEnvironment);

            if (!analysis.Success)
            {
                AddLog($"✗ Analysis failed: {analysis.Error}");
                AnalysisReport = $"Error: {analysis.Error}";
                return;
            }

            _missingDates = analysis.MissingDates;
            ExistingDates = analysis.ExistingDates;
            ExpectedDates = analysis.ExpectedDates;
            MissingDateCount = analysis.MissingDates.Count;
            CoveragePercent = analysis.CoveragePercent;

            // Build report
            var report = new System.Text.StringBuilder();
            report.AppendLine($"Date Range: {analysis.StartDate:yyyy-MM-dd} to {analysis.EndDate:yyyy-MM-dd}");
            report.AppendLine($"Expected Trading Days: {analysis.ExpectedDates:N0}");
            report.AppendLine($"Dates with Data: {analysis.ExistingDates:N0}");
            report.AppendLine($"Missing Dates: {analysis.MissingDates.Count:N0}");
            report.AppendLine($"Coverage: {analysis.CoveragePercent:F1}%");
            report.AppendLine();
            report.AppendLine($"Total Securities: {analysis.TotalSecurities:N0}");
            report.AppendLine($"Total Price Records: {analysis.TotalPriceRecords:N0}");
            report.AppendLine($"Securities with Gaps: {analysis.SecuritiesWithGaps:N0}");
            report.AppendLine();

            if (analysis.MissingDates.Count > 0)
            {
                report.AppendLine("Most Recent Missing Dates:");
                foreach (var date in analysis.MissingDates.Take(10))
                {
                    report.AppendLine($"  • {date:yyyy-MM-dd} ({date:dddd})");
                }
                if (analysis.MissingDates.Count > 10)
                {
                    report.AppendLine($"  ... and {analysis.MissingDates.Count - 10} more");
                }
            }

            AnalysisReport = report.ToString();
            AddLog($"✓ Found {analysis.MissingDates.Count} missing dates ({analysis.CoveragePercent:F1}% coverage)");
        }
        catch (Exception ex)
        {
            AddLog($"✗ Error: {ex.Message}");
            AnalysisReport = $"Error: {ex.Message}";
        }
        finally
        {
            IsAnalyzing = false;
        }
    }

    private bool CanAnalyzeGaps() => !IsAnalyzing && !IsRunning;

    [RelayCommand(CanExecute = nameof(CanStartBulkFill))]
    private async Task StartBulkFillAsync()
    {
        if (IsRunning || _missingDates.Count == 0) return;

        // Confirm action
        var isProduction = ApiUrl.Contains("psfordtaurus.com", StringComparison.OrdinalIgnoreCase) ||
                           ApiUrl.Contains("azure", StringComparison.OrdinalIgnoreCase);

        var confirmMessage = isProduction
            ? $"⚠️ PRODUCTION MODE\n\n" +
              $"This will fill {Math.Min(MaxDatesToProcess, _missingDates.Count)} dates using bulk API.\n" +
              $"Each date = 1 API call returning ALL US tickers.\n\n" +
              $"Target: {ApiUrl}\n\n" +
              $"Continue?"
            : $"Fill {Math.Min(MaxDatesToProcess, _missingDates.Count)} dates using bulk API?\n\n" +
              $"Each date = 1 API call returning ALL US tickers.\n\n" +
              $"Target: {ApiUrl}";

        var result = MessageBox.Show(
            confirmMessage,
            isProduction ? "Confirm Production Bulk Fill" : "Confirm Bulk Fill",
            MessageBoxButton.YesNo,
            isProduction ? MessageBoxImage.Warning : MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        IsRunning = true;
        DatesProcessed = 0;
        TotalDatesToProcess = Math.Min(MaxDatesToProcess, _missingDates.Count);
        RecordsInserted = 0;
        ApiCallsMade = 0;
        Progress = 0;

        AddLog($"🚀 Starting bulk fill for {TotalDatesToProcess} dates...");

        var progressHandler = new Progress<BulkFillProgress>(p =>
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                CurrentDate = p.CurrentDate;
                DatesProcessed = p.DatesProcessed;
                RecordsInserted = p.RecordsInserted;
                ApiCallsMade = p.ApiCallsMade;
                Progress = p.PercentComplete;
            });
        });

        try
        {
            var fillResult = await _bulkFillService.FillGapsAsync(
                SelectedEnvironment,
                _missingDates,
                MaxDatesToProcess,
                progressHandler);

            if (fillResult.Success)
            {
                AddLog($"✓ Bulk fill complete: {fillResult.DatesProcessed} dates, {fillResult.RecordsInserted:N0} records");

                if (fillResult.Errors.Count > 0)
                {
                    AddLog($"  ⚠️ {fillResult.Errors.Count} errors occurred");
                }

                // Re-analyze to update missing dates
                await AnalyzeGapsAsync();
            }
            else
            {
                AddLog($"✗ Bulk fill failed: {fillResult.Error}");
            }
        }
        catch (Exception ex)
        {
            AddLog($"✗ Error: {ex.Message}");
        }
        finally
        {
            IsRunning = false;
        }
    }

    private bool CanStartBulkFill() => !IsRunning && !IsAnalyzing && IsConnected && _missingDates.Count > 0;

    [RelayCommand(CanExecute = nameof(CanStopBulkFill))]
    private void StopBulkFill()
    {
        AddLog("🛑 Stopping bulk fill...");
        _bulkFillService.Stop();
    }

    private bool CanStopBulkFill() => IsRunning;

    [RelayCommand]
    private void ClearLog()
    {
        Log.Clear();
        AddLog("Log cleared");
    }

    private void AddLog(string message)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            Log.Insert(0, $"[{DateTime.Now:HH:mm:ss}] {message}");
            if (Log.Count > 500) Log.RemoveAt(Log.Count - 1);
        });
    }
}
