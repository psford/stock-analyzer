using System.Windows;
using EodhdLoader.Services;
using EodhdLoader.ViewModels;
using EodhdLoader.Views;
using Microsoft.Extensions.DependencyInjection;

namespace EodhdLoader;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        // Auto-refresh dashboard on load
        Loaded += async (s, e) =>
        {
            if (viewModel.Dashboard.RefreshCommand.CanExecute(null))
            {
                await viewModel.Dashboard.RefreshCommand.ExecuteAsync(null);
            }
        };
    }

    private void OnHeatmapV2Click(object sender, RoutedEventArgs e)
    {
        var apiClient = App.Services.GetRequiredService<StockAnalyzerApiClient>();
        var window = new HeatmapTestWindow(apiClient) { Owner = this };
        window.Show();
    }
}
