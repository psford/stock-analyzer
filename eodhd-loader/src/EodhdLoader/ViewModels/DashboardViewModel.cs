using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EodhdLoader.Services;

namespace EodhdLoader.ViewModels;

public partial class DashboardViewModel : ViewModelBase
{
    private readonly DataAnalysisService _analysisService;
    private readonly ConfigurationService _config;

    [ObservableProperty]
    private DataCoverageStats? _stats;

    [ObservableProperty]
    private ObservableCollection<SecurityGap> _gaps = [];

    [ObservableProperty]
    private ObservableCollection<SecurityTypeCoverage> _typeCoverage = [];

    [ObservableProperty]
    private string _connectionStatus = "Not connected";

    [ObservableProperty]
    private bool _isConnected;

    public DashboardViewModel(DataAnalysisService analysisService, ConfigurationService config)
    {
        _analysisService = analysisService;
        _config = config;
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsBusy) return;

        IsBusy = true;
        SetStatus("Loading statistics...");

        try
        {
            Stats = await _analysisService.GetCoverageStatsAsync();
            IsConnected = true;
            ConnectionStatus = "Connected to local SQL Server";

            SetStatus("Loading gaps...");
            var gapsList = await _analysisService.GetRecentGapsAsync(20);
            Gaps = new ObservableCollection<SecurityGap>(gapsList);

            SetStatus("Loading coverage by type...");
            var coverage = await _analysisService.GetCoverageByTypeAsync();
            TypeCoverage = new ObservableCollection<SecurityTypeCoverage>(coverage);

            SetStatus($"Last updated: {DateTime.Now:g}");
        }
        catch (Exception ex)
        {
            IsConnected = false;
            ConnectionStatus = $"Error: {ex.Message}";
            SetStatus($"Failed to load: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    public string ApiKeyStatus => _config.HasEodhdKey
        ? "EODHD API Key: Configured"
        : "EODHD API Key: NOT CONFIGURED";

    public string EnvPath => _config.LoadedEnvPath ?? "No .env file found";
}
