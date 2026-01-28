using System.Windows;
using EodhdLoader.Services;

namespace EodhdLoader.Views;

public partial class HeatmapTestWindow : Window
{
    private readonly StockAnalyzerApiClient _apiClient;

    public HeatmapTestWindow(StockAnalyzerApiClient apiClient)
    {
        InitializeComponent();
        _apiClient = apiClient;

        Loaded += async (_, _) => await LoadHeatmapDataAsync();
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        await LoadHeatmapDataAsync();
    }

    private async Task LoadHeatmapDataAsync()
    {
        HeatmapControl.IsLoading = true;
        StatusText.Text = "Fetching heatmap data from API...";
        RefreshButton.IsEnabled = false;

        try
        {
            var data = await _apiClient.GetHeatmapDataAsync();

            if (data?.Success == true && data.Cells.Count > 0)
            {
                HeatmapControl.HeatmapData = data;
                StatusText.Text = $"Loaded {data.Cells.Count} cells " +
                    $"({data.Metadata?.MinYear}-{data.Metadata?.MaxYear})  |  " +
                    $"Last refreshed: {DateTime.Now:HH:mm:ss}";
            }
            else
            {
                StatusText.Text = "No heatmap data returned. Is the API connected?";
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error: {ex.Message}";
        }
        finally
        {
            HeatmapControl.IsLoading = false;
            RefreshButton.IsEnabled = true;
        }
    }
}
