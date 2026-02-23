using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EodhdLoader.Models;
using EodhdLoader.Services;
using EodhdLoader.Utilities;

namespace EodhdLoader.ViewModels;

/// <summary>
/// ViewModel for iShares constituent loading (Phase 2).
/// Manages ETF selection, loading, and progress tracking.
/// </summary>
public partial class IndexManagerViewModel : ViewModelBase
{
    private readonly IISharesConstituentService _constituentService;
    private readonly ConfigurationService _config;
    private CancellationTokenSource? _cts;

    [ObservableProperty]
    private DateTime _asOfDate;

    [ObservableProperty]
    private string? _selectedEtfTicker;

    [ObservableProperty]
    private ObservableCollection<string> _availableEtfTickers = [];

    [ObservableProperty]
    private string _currentEtfLabel = string.Empty;

    [ObservableProperty]
    private int _totalEtfsToLoad;

    [ObservableProperty]
    private int _currentEtfIndex;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string _progressText = "Ready";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private ObservableCollection<string> _logMessages = [];

    public IndexManagerViewModel(
        IISharesConstituentService constituentService,
        ConfigurationService config)
    {
        _constituentService = constituentService;
        _config = config;

        // Initialize AsOfDate to last business day of previous month
        _asOfDate = GetLastMonthEnd();

        // Populate available ETF tickers from service config
        PopulateAvailableEtfs();
    }

    private void PopulateAvailableEtfs()
    {
        var items = new List<string> { "(All)" };
        items.AddRange(
            _constituentService.EtfConfigs
                .OrderBy(kvp => kvp.Key)
                .Select(kvp => $"{kvp.Key} - {FormatSlugAsName(kvp.Value.Slug)}"));

        AvailableEtfTickers = new ObservableCollection<string>(items);
        SelectedEtfTicker = "(All)";
    }

    /// <summary>
    /// Converts an iShares slug like "ishares-core-s-p-500-etf" to "Core S&amp;P 500 ETF".
    /// Strips the leading "ishares-" prefix and title-cases the rest.
    /// </summary>
    private static readonly HashSet<string> UppercaseAcronyms = new(StringComparer.OrdinalIgnoreCase)
    {
        "msci", "esg", "eafe", "usa", "etf", "tips", "mbs", "gnma", "cmbs",
    };

    private static string FormatSlugAsName(string slug)
    {
        // Strip "ishares-" prefix
        var name = slug.StartsWith("ishares-", StringComparison.OrdinalIgnoreCase)
            ? slug["ishares-".Length..]
            : slug;

        // Replace hyphens with spaces and apply casing rules
        var words = name.Split('-', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < words.Length; i++)
        {
            var w = words[i];
            if (UppercaseAcronyms.Contains(w) || w.Length <= 3)
                words[i] = w.ToUpperInvariant();
            else
                words[i] = char.ToUpper(w[0]) + w[1..];
        }

        return string.Join(' ', words);
    }

    private static DateTime GetLastMonthEnd() => DateUtilities.GetLastMonthEnd();

    [RelayCommand]
    private async Task LoadAllAsync()
    {
        if (IsLoading)
            return;

        IsLoading = true;
        IsBusy = true;
        _cts = new CancellationTokenSource();

        Progress = 0;
        ProgressText = "Starting load...";

        try
        {
            // Wire up event handlers
            _constituentService.LogMessage += OnServiceLogMessage;
            _constituentService.ProgressUpdated += OnServiceProgressUpdated;

            Log($"Loading iShares ETF constituents as of {AsOfDate:yyyy-MM-dd}");

            // Determine which ETF(s) to load
            // Display format is "TICKER - Name", extract just the ticker
            var ticker = SelectedEtfTicker?.Split(" - ", 2)[0];
            if (string.IsNullOrEmpty(ticker) || ticker == "(All)")
            {
                Log("Loading all configured ETFs...");
                await _constituentService.IngestAllEtfsAsync(AsOfDate, _cts.Token);
            }
            else
            {
                Log($"Loading specific ETF: {ticker}");
                await _constituentService.IngestEtfAsync(ticker, AsOfDate, _cts.Token);
            }

            Progress = 100;
            ProgressText = "Complete!";
            Log("Load operation completed successfully");
        }
        catch (OperationCanceledException)
        {
            Log("Load cancelled by user");
            ProgressText = "Cancelled";
        }
        catch (Exception ex)
        {
            Log($"ERROR during load: {ex.Message}");
            ProgressText = "Error - see log";
        }
        finally
        {
            // Unsubscribe from event handlers
            _constituentService.LogMessage -= OnServiceLogMessage;
            _constituentService.ProgressUpdated -= OnServiceProgressUpdated;

            IsLoading = false;
            IsBusy = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        if (_cts != null && !_cts.IsCancellationRequested)
        {
            Log("Cancellation requested — finishing current ETF...");
            _cts.Cancel();
        }
    }

    [RelayCommand]
    private void ClearLog()
    {
        LogMessages.Clear();
    }

    private void OnServiceLogMessage(string message)
    {
        Log(message);
    }

    private void OnServiceProgressUpdated(IngestProgress progress)
    {
        CurrentEtfLabel = $"Loading {progress.EtfTicker} ({progress.CurrentEtf} / {progress.TotalEtfs})...";
        Progress = (double)progress.CurrentEtf / progress.TotalEtfs * 100;
        ProgressText = $"{progress.Stats.Inserted} inserted, {progress.Stats.SkippedExisting} skipped, {progress.Stats.Failed} failed";

        TotalEtfsToLoad = progress.TotalEtfs;
        CurrentEtfIndex = progress.CurrentEtf;
    }

    private void Log(string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        var formattedMessage = $"[{timestamp}] {message}";

        // Handle test context where App.Current might be null
        if (App.Current?.Dispatcher != null)
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                LogMessages.Insert(0, formattedMessage);
                if (LogMessages.Count > 500)
                    LogMessages.RemoveAt(LogMessages.Count - 1);
            });
        }
        else
        {
            // For tests or when running outside WPF context
            LogMessages.Insert(0, formattedMessage);
            if (LogMessages.Count > 500)
                LogMessages.RemoveAt(LogMessages.Count - 1);
        }
    }
}
