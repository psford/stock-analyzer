using System.Windows;
using EodhdLoader.Services;
using EodhdLoader.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StockAnalyzer.Core.Data;
using StockAnalyzer.Core.Services;

namespace EodhdLoader;

public partial class App : Application
{
    private readonly IServiceProvider _serviceProvider;

    public static IServiceProvider Services => ((App)Current)._serviceProvider;

    public App()
    {
        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Configuration
        services.AddSingleton<ConfigurationService>();

        // Local services
        services.AddSingleton<DataAnalysisService>();
        services.AddHttpClient<IndexService>();

        // These services use IHttpClientFactory directly so they can switch environments
        services.AddHttpClient();
        services.AddSingleton<StockAnalyzerApiClient>();
        services.AddSingleton<BorisService>();
        services.AddSingleton<BulkFillService>();
        services.AddSingleton<PriceCoverageAnalyzer>();
        services.AddSingleton<HolidayForwardFillService>();
        services.AddSingleton<ProdSyncService>();

        // StockAnalyzer.Core services
        services.AddDbContext<StockAnalyzerDbContext>((sp, options) =>
        {
            var config = sp.GetRequiredService<ConfigurationService>();
            options.UseSqlServer(config.LocalConnectionString);
        });

        services.AddHttpClient<EodhdService>();
        services.AddScoped<ISecurityMasterRepository, SqlSecurityMasterRepository>();
        services.AddScoped<IPriceRepository, SqlPriceRepository>();
        services.AddScoped<PriceRefreshService>();

        // Add logging for Core services
        services.AddLogging();

        // ViewModels
        services.AddTransient<BorisViewModel>();
        services.AddTransient<BulkFillViewModel>();
        services.AddTransient<CrawlerViewModel>();
        services.AddTransient<DashboardViewModel>();
        services.AddTransient<IndexManagerViewModel>();
        services.AddTransient<MainViewModel>();

        // Main Window
        services.AddTransient<MainWindow>();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Prevent unhandled UI-thread exceptions from crashing the app
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(
                $"An error occurred:\n\n{args.Exception.Message}\n\n{args.Exception.StackTrace}",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
        };

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }
}
