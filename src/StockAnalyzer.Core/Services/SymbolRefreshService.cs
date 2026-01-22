using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StockAnalyzer.Core.Data;

namespace StockAnalyzer.Core.Services;

/// <summary>
/// Background service that periodically refreshes the local symbol database from Finnhub.
/// Runs daily at 2 AM UTC, or immediately on startup if database is empty.
/// </summary>
public class SymbolRefreshService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SymbolRefreshService> _logger;
    private readonly HttpClient _httpClient;
    private readonly SymbolCache _symbolCache;
    private readonly string _finnhubApiKey;
    private readonly int _targetHourUtc;
    private readonly string? _wwwrootPath;

    private const string FinnhubBaseUrl = "https://finnhub.io/api/v1";
    private DateTime _lastRefresh = DateTime.MinValue;

    public SymbolRefreshService(
        IServiceProvider serviceProvider,
        ILogger<SymbolRefreshService> logger,
        IConfiguration configuration,
        SymbolCache symbolCache,
        HttpClient? httpClient = null)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _httpClient = httpClient ?? new HttpClient();
        _symbolCache = symbolCache;
        _finnhubApiKey = configuration["Finnhub:ApiKey"]
                      ?? Environment.GetEnvironmentVariable("FINNHUB_API_KEY")
                      ?? "";
        _targetHourUtc = configuration.GetValue("SymbolDatabase:RefreshHourUtc", 2);
        _wwwrootPath = configuration["WebRoot:Path"];
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SymbolRefreshService starting");

        // Defer startup to allow app to initialize
        await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);

        // Check if initial seed is needed
        using (var scope = _serviceProvider.CreateScope())
        {
            var repo = scope.ServiceProvider.GetService<ISymbolRepository>();
            if (repo == null)
            {
                _logger.LogWarning("ISymbolRepository not registered, symbol refresh disabled");
                return;
            }

            var count = await repo.GetActiveCountAsync();

            if (count == 0)
            {
                _logger.LogInformation("Symbol database is empty, performing initial seed...");
                await RefreshSymbolsAsync(stoppingToken);
            }
            else
            {
                _logger.LogInformation("Symbol database has {Count} active symbols", count);
                _lastRefresh = await repo.GetLastRefreshTimeAsync() ?? DateTime.MinValue;
            }
        }

        // Main refresh loop
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTime.UtcNow;
                var nextRefresh = CalculateNextRefreshTime(now);
                var delay = nextRefresh - now;

                if (delay > TimeSpan.Zero)
                {
                    _logger.LogDebug("Next symbol refresh scheduled for {NextRefresh} (in {Delay})",
                        nextRefresh, delay);
                    await Task.Delay(delay, stoppingToken);
                }

                if (!stoppingToken.IsCancellationRequested)
                {
                    await RefreshSymbolsAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in symbol refresh loop, retrying in 1 hour");
                await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
            }
        }

        _logger.LogInformation("SymbolRefreshService stopping");
    }

    /// <summary>
    /// Calculate next refresh time (default 2 AM UTC daily).
    /// </summary>
    private DateTime CalculateNextRefreshTime(DateTime now)
    {
        var todayTarget = new DateTime(now.Year, now.Month, now.Day, _targetHourUtc, 0, 0, DateTimeKind.Utc);

        if (now < todayTarget)
            return todayTarget;

        return todayTarget.AddDays(1);
    }

    /// <summary>
    /// Fetch symbols from Finnhub and update local database.
    /// Public to allow manual trigger via admin endpoint.
    /// </summary>
    public async Task<int> RefreshSymbolsAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_finnhubApiKey))
        {
            _logger.LogWarning("Finnhub API key not configured, skipping symbol refresh");
            return 0;
        }

        _logger.LogInformation("Starting symbol refresh from Finnhub...");
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            // Fetch US stock symbols
            var url = $"{FinnhubBaseUrl}/stock/symbol?exchange=US&token={_finnhubApiKey}";
            var response = await _httpClient.GetFromJsonAsync<List<FinnhubSymbol>>(url, ct);

            if (response == null || response.Count == 0)
            {
                _logger.LogWarning("Finnhub returned no symbols");
                return 0;
            }

            _logger.LogInformation("Fetched {Count} symbols from Finnhub", response.Count);

            // Convert to DTOs
            var symbols = response
                .Where(s => !string.IsNullOrEmpty(s.Symbol))
                .Select(s => new SymbolUpsertDto
                {
                    Symbol = s.Symbol!,
                    DisplaySymbol = s.DisplaySymbol ?? s.Symbol ?? "",
                    Description = s.Description ?? "",
                    Type = s.Type ?? "Common Stock",
                    Exchange = "US",
                    Mic = s.Mic,
                    Currency = s.Currency ?? "USD",
                    Figi = s.Figi,
                    Country = "US"
                })
                .ToList();

            // Upsert to database
            using var scope = _serviceProvider.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<ISymbolRepository>();

            var upsertCount = await repo.UpsertManyAsync(symbols);

            // Mark missing symbols as inactive
            var activeSymbols = symbols.Select(s => s.Symbol).ToList();
            var inactiveCount = await repo.MarkInactiveAsync(activeSymbols);

            // Reload cache with fresh data
            if (repo is SqlSymbolRepository sqlRepo)
            {
                await sqlRepo.LoadCacheAsync();
            }

            // Regenerate static file for client-side search
            if (!string.IsNullOrEmpty(_wwwrootPath))
            {
                _symbolCache.GenerateClientFile(_wwwrootPath);
            }

            stopwatch.Stop();
            _lastRefresh = DateTime.UtcNow;

            _logger.LogInformation(
                "Symbol refresh complete: {Upserted} upserted, {Inactive} marked inactive, took {Elapsed}ms",
                upsertCount, inactiveCount, stopwatch.ElapsedMilliseconds);

            return upsertCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh symbols from Finnhub");
            throw;
        }
    }

    /// <summary>
    /// Get status of the refresh service.
    /// </summary>
    public (DateTime lastRefresh, bool isApiKeyConfigured) GetStatus()
    {
        return (_lastRefresh, !string.IsNullOrEmpty(_finnhubApiKey));
    }

    #region Finnhub Response Model

    private class FinnhubSymbol
    {
        [JsonPropertyName("symbol")]
        public string? Symbol { get; set; }

        [JsonPropertyName("displaySymbol")]
        public string? DisplaySymbol { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("mic")]
        public string? Mic { get; set; }

        [JsonPropertyName("figi")]
        public string? Figi { get; set; }

        [JsonPropertyName("shareClassFIGI")]
        public string? ShareClassFigi { get; set; }

        [JsonPropertyName("currency")]
        public string? Currency { get; set; }

        [JsonPropertyName("symbol2")]
        public string? Symbol2 { get; set; }

        [JsonPropertyName("isin")]
        public string? Isin { get; set; }
    }

    #endregion
}
