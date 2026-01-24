using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EodhdLoader.Services;

namespace EodhdLoader.ViewModels;

public partial class MigrationViewModel : ViewModelBase
{
    private readonly BulkCopyService _bulkCopyService;
    private readonly ConfigurationService _config;
    private CancellationTokenSource? _cts;

    [ObservableProperty]
    private string _targetConnectionString = string.Empty;

    [ObservableProperty]
    private bool _migrateSecurityMaster = true;

    [ObservableProperty]
    private bool _migratePrices = true;

    [ObservableProperty]
    private bool _useIncrementalPrices;

    [ObservableProperty]
    private DateTime _incrementalFromDate = DateTime.Today.AddMonths(-1);

    [ObservableProperty]
    private int _batchSize = 5000;

    [ObservableProperty]
    private bool _isMigrating;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string _progressText = "Ready";

    [ObservableProperty]
    private string _sourceStatus = "Checking...";

    [ObservableProperty]
    private string _targetStatus = "Not configured";

    [ObservableProperty]
    private ObservableCollection<string> _logMessages = [];

    public MigrationViewModel(BulkCopyService bulkCopyService, ConfigurationService config)
    {
        _bulkCopyService = bulkCopyService;
        _config = config;

        if (!string.IsNullOrEmpty(_config.ProductionConnectionString))
        {
            TargetConnectionString = _config.ProductionConnectionString;
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

    [RelayCommand]
    private async Task TestConnectionsAsync()
    {
        IsBusy = true;

        // Test source
        SourceStatus = "Testing...";
        var sourceOk = await _bulkCopyService.TestConnectionAsync(_config.LocalConnectionString);
        SourceStatus = sourceOk ? "Connected" : "Failed";
        Log($"Source connection: {(sourceOk ? "OK" : "FAILED")}");

        // Test target
        if (!string.IsNullOrEmpty(TargetConnectionString))
        {
            TargetStatus = "Testing...";
            var targetOk = await _bulkCopyService.TestConnectionAsync(TargetConnectionString);
            TargetStatus = targetOk ? "Connected" : "Failed";
            Log($"Target connection: {(targetOk ? "OK" : "FAILED")}");
        }
        else
        {
            TargetStatus = "Not configured";
        }

        IsBusy = false;
    }

    [RelayCommand]
    private async Task StartMigrationAsync()
    {
        if (IsMigrating) return;
        if (string.IsNullOrEmpty(TargetConnectionString))
        {
            Log("ERROR: Target connection string not configured");
            return;
        }

        IsMigrating = true;
        IsBusy = true;
        _cts = new CancellationTokenSource();
        _bulkCopyService.BatchSize = BatchSize;

        var progressHandler = new Progress<MigrationProgress>(p =>
        {
            Progress = p.PercentComplete;
            ProgressText = p.Status;
        });

        try
        {
            if (MigrateSecurityMaster)
            {
                Log("Starting SecurityMaster migration...");
                var result = await _bulkCopyService.MigrateSecurityMasterAsync(
                    TargetConnectionString,
                    progressHandler,
                    _cts.Token);

                if (result.Success)
                {
                    Log($"SecurityMaster: Migrated {result.RowsCopied:N0} rows in {result.Duration.TotalSeconds:N1}s");
                }
                else
                {
                    Log($"SecurityMaster FAILED: {result.ErrorMessage}");
                }
            }

            if (MigratePrices && !_cts.Token.IsCancellationRequested)
            {
                var fromDate = UseIncrementalPrices ? IncrementalFromDate : (DateTime?)null;
                Log($"Starting Prices migration{(fromDate.HasValue ? $" from {fromDate:yyyy-MM-dd}" : "")}...");

                var result = await _bulkCopyService.MigratePricesAsync(
                    TargetConnectionString,
                    fromDate,
                    progressHandler,
                    _cts.Token);

                if (result.Success)
                {
                    var rate = result.Duration.TotalSeconds > 0
                        ? result.RowsCopied / result.Duration.TotalSeconds
                        : 0;
                    Log($"Prices: Migrated {result.RowsCopied:N0} rows in {result.Duration.TotalSeconds:N1}s ({rate:N0} rows/sec)");
                }
                else
                {
                    Log($"Prices FAILED: {result.ErrorMessage}");
                }
            }

            ProgressText = _cts.Token.IsCancellationRequested ? "Cancelled" : "Complete!";
            Log("Migration finished");
        }
        catch (OperationCanceledException)
        {
            Log("Migration cancelled");
            ProgressText = "Cancelled";
        }
        catch (Exception ex)
        {
            Log($"ERROR: {ex.Message}");
            ProgressText = "Error - see log";
        }
        finally
        {
            IsMigrating = false;
            IsBusy = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    [RelayCommand]
    private void CancelMigration()
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
}
