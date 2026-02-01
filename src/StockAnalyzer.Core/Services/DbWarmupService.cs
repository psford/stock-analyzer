using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StockAnalyzer.Core.Data;

namespace StockAnalyzer.Core.Services;

/// <summary>
/// Warms the ADO.NET connection pool on app startup by executing a lightweight query.
/// Without this, the first user request pays the full TCP + TLS + auth handshake cost
/// to Azure SQL (~200-500ms on Basic tier).
/// </summary>
public class DbWarmupService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DbWarmupService> _logger;

    public DbWarmupService(IServiceScopeFactory scopeFactory, ILogger<DbWarmupService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<StockAnalyzerDbContext>();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            await db.Database.ExecuteSqlRawAsync("SELECT 1", cancellationToken);
            sw.Stop();
            _logger.LogInformation("DB connection pool warmed up in {Elapsed}ms", sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DB warmup failed — first request may be slower");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
