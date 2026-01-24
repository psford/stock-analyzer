using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EodhdLoader.Services;
using Microsoft.Extensions.DependencyInjection;
using StockAnalyzer.Core.Services;

namespace EodhdLoader.ViewModels;

public partial class LoaderViewModel : ViewModelBase
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ConfigurationService _config;
    private CancellationTokenSource? _cts;

    [ObservableProperty]
    private DateTime _startDate = DateTime.Today.AddMonths(-1);

    [ObservableProperty]
    private DateTime _endDate = DateTime.Today;

    [ObservableProperty]
    private string _selectedMode = "Bulk Daily";

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string _progressText = "Ready";

    [ObservableProperty]
    private int _daysProcessed;

    [ObservableProperty]
    private int _totalDays;

    [ObservableProperty]
    private int _errors;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private ObservableCollection<string> _logMessages = [];

    public string[] LoadingModes { get; } = ["Bulk Daily", "Gap Fill"];

    public LoaderViewModel(IServiceProvider serviceProvider, ConfigurationService config)
    {
        _serviceProvider = serviceProvider;
        _config = config;
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
    private async Task StartLoadingAsync()
    {
        if (IsLoading) return;
        if (!_config.HasEodhdKey)
        {
            Log("ERROR: EODHD API key not configured");
            return;
        }

        IsLoading = true;
        IsBusy = true;
        _cts = new CancellationTokenSource();

        DaysProcessed = 0;
        Errors = 0;
        Progress = 0;

        Log($"Starting {SelectedMode} load from {StartDate:yyyy-MM-dd} to {EndDate:yyyy-MM-dd}");

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var priceService = scope.ServiceProvider.GetRequiredService<PriceRefreshService>();

            // Calculate trading days
            var currentDate = StartDate;
            var tradingDays = new List<DateTime>();

            while (currentDate <= EndDate)
            {
                if (currentDate.DayOfWeek != DayOfWeek.Saturday &&
                    currentDate.DayOfWeek != DayOfWeek.Sunday)
                {
                    tradingDays.Add(currentDate);
                }
                currentDate = currentDate.AddDays(1);
            }

            TotalDays = tradingDays.Count;
            Log($"Found {TotalDays} trading days to process");

            foreach (var date in tradingDays)
            {
                if (_cts.Token.IsCancellationRequested)
                {
                    Log("Loading cancelled by user");
                    break;
                }

                ProgressText = $"Loading {date:yyyy-MM-dd}... ({DaysProcessed + 1}/{TotalDays})";

                try
                {
                    await priceService.RefreshDateAsync(date, _cts.Token);
                    Log($"{date:yyyy-MM-dd}: Loaded successfully");
                }
                catch (Exception ex)
                {
                    Errors++;
                    Log($"ERROR on {date:yyyy-MM-dd}: {ex.Message}");
                }

                DaysProcessed++;
                Progress = (double)DaysProcessed / TotalDays * 100;

                // Small delay to respect rate limits
                await Task.Delay(500, _cts.Token);
            }

            ProgressText = _cts.Token.IsCancellationRequested
                ? "Cancelled"
                : "Complete!";

            Log($"Finished! Days processed: {DaysProcessed}, Errors: {Errors}");
        }
        catch (OperationCanceledException)
        {
            Log("Loading cancelled");
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
    private void CancelLoading()
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
