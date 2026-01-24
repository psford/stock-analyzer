using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EodhdLoader.Services;

namespace EodhdLoader.ViewModels;

public partial class IndexManagerViewModel : ViewModelBase
{
    private readonly IndexService _indexService;
    private readonly StockAnalyzerApiClient _apiClient;
    private readonly ConfigurationService _config;
    private CancellationTokenSource? _cts;

    [ObservableProperty]
    private TargetEnvironment _selectedEnvironment = TargetEnvironment.Local;

    [ObservableProperty]
    private IndexDefinition? _selectedIndex;

    [ObservableProperty]
    private DateTime _backfillFromDate = DateTime.Today.AddYears(-5);

    [ObservableProperty]
    private DateTime _backfillToDate = DateTime.Today;

    [ObservableProperty]
    private int _constituentCount;

    [ObservableProperty]
    private int _processedCount;

    [ObservableProperty]
    private int _errorCount;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string _progressText = "Ready";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isLoadingConstituents;

    [ObservableProperty]
    private ObservableCollection<IndexDefinition> _availableIndices = [];

    [ObservableProperty]
    private ObservableCollection<IndexConstituent> _constituents = [];

    [ObservableProperty]
    private ObservableCollection<string> _logMessages = [];

    public string[] Environments { get; } = ["Local", "Production"];

    public IndexManagerViewModel(
        IndexService indexService,
        StockAnalyzerApiClient apiClient,
        ConfigurationService config)
    {
        _indexService = indexService;
        _apiClient = apiClient;
        _config = config;

        LoadAvailableIndices();
    }

    partial void OnSelectedEnvironmentChanged(TargetEnvironment value)
    {
        _apiClient.CurrentEnvironment = value;
        Log($"Environment switched to: {value}");
    }

    private void LoadAvailableIndices()
    {
        var indices = _indexService.GetMajorIndices();
        AvailableIndices = new ObservableCollection<IndexDefinition>(indices);

        if (AvailableIndices.Count > 0)
            SelectedIndex = AvailableIndices[0];
    }

    [RelayCommand]
    private async Task LoadConstituentsAsync()
    {
        if (SelectedIndex == null || IsLoadingConstituents) return;

        IsLoadingConstituents = true;
        IsBusy = true;

        try
        {
            Log($"Fetching constituents for {SelectedIndex.Name} ({SelectedIndex.Symbol})...");

            var response = await _indexService.GetConstituentsAsync(SelectedIndex.Symbol);

            Constituents.Clear();
            foreach (var constituent in response.Constituents)
            {
                Constituents.Add(constituent);
            }

            ConstituentCount = Constituents.Count;
            Log($"Loaded {ConstituentCount} constituents from {SelectedIndex.Name}");
        }
        catch (Exception ex)
        {
            Log($"ERROR loading constituents: {ex.Message}");
        }
        finally
        {
            IsLoadingConstituents = false;
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task StartBackfillAsync()
    {
        if (SelectedIndex == null || Constituents.Count == 0 || IsLoading) return;

        // Production confirmation
        if (SelectedEnvironment == TargetEnvironment.Production)
        {
            var result = System.Windows.MessageBox.Show(
                $"You are about to backfill {ConstituentCount} securities to PRODUCTION.\n\n" +
                $"Index: {SelectedIndex.Name}\n" +
                $"Date Range: {BackfillFromDate:yyyy-MM-dd} to {BackfillToDate:yyyy-MM-dd}\n" +
                $"Target: {_config.GetApiUrl(TargetEnvironment.Production)}\n\n" +
                "Continue?",
                "Production Backfill Confirmation",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning);

            if (result != System.Windows.MessageBoxResult.Yes)
            {
                Log("Production backfill cancelled by user");
                return;
            }
        }

        IsLoading = true;
        IsBusy = true;
        _cts = new CancellationTokenSource();

        ProcessedCount = 0;
        ErrorCount = 0;
        Progress = 0;

        Log($"Starting backfill for {SelectedIndex.Name} - {ConstituentCount} tickers");
        Log($"Environment: {SelectedEnvironment}");
        Log($"Date range: {BackfillFromDate:yyyy-MM-dd} to {BackfillToDate:yyyy-MM-dd}");

        try
        {
            // Get ticker list
            var tickers = Constituents.Select(c => c.Ticker).ToList();

            // Call API to backfill
            ProgressText = $"Sending backfill request for {tickers.Count} tickers...";

            var result = await _apiClient.LoadTickersAsync(
                tickers,
                BackfillFromDate,
                BackfillToDate,
                _cts.Token);

            if (result.Success && result.Data != null)
            {
                ProcessedCount = result.Data.TickersProcessed;
                ErrorCount = result.Data.Errors;
                Progress = 100;
                ProgressText = "Complete!";

                Log($"Backfill complete! Tickers: {result.Data.TickersProcessed}, Prices: {result.Data.PricesLoaded}, Errors: {result.Data.Errors}");

                if (result.Data.FailedTickers.Count > 0)
                {
                    Log($"Failed tickers: {string.Join(", ", result.Data.FailedTickers)}");
                }
            }
            else
            {
                Log($"ERROR: {result.Error ?? "Unknown error"}");
                ProgressText = "Error - see log";
            }
        }
        catch (OperationCanceledException)
        {
            Log("Backfill cancelled");
            ProgressText = "Cancelled";
        }
        catch (Exception ex)
        {
            Log($"ERROR: {ex.Message}");
            ProgressText = "Error - see log";
        }
        finally
        {
            IsLoading = false;
            IsBusy = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    [RelayCommand]
    private void CancelBackfill()
    {
        if (_cts != null && !_cts.IsCancellationRequested)
        {
            Log("Cancellation requested...");
            _cts.Cancel();
        }
    }

    [RelayCommand]
    private void ClearLog()
    {
        LogMessages.Clear();
    }

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        IsBusy = true;

        try
        {
            Log($"Testing connection to {_config.GetApiUrl(SelectedEnvironment)}...");

            var success = await _apiClient.TestConnectionAsync();

            if (success)
            {
                Log("✓ Connection successful");
            }
            else
            {
                Log("✗ Connection failed");
            }
        }
        catch (Exception ex)
        {
            Log($"ERROR testing connection: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Log(string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        App.Current.Dispatcher.Invoke(() =>
        {
            LogMessages.Insert(0, $"[{timestamp}] {message}");
            if (LogMessages.Count > 500)
                LogMessages.RemoveAt(LogMessages.Count - 1);
        });
    }
}
