namespace EodhdLoader.Services;

using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using EodhdLoader.Models;
using Microsoft.Extensions.DependencyInjection;
using StockAnalyzer.Core.Data;

/// <summary>
/// Implements IISharesConstituentService with JSON download, parsing, and database persistence.
/// </summary>
public class ISharesConstituentService : IISharesConstituentService
{
    private readonly HttpClient _httpClient;
    private readonly StockAnalyzerDbContext _dbContext;
    private readonly Dictionary<string, EtfConfig> _etfConfigs;

    /// <summary>
    /// Rate limiting constant: minimum milliseconds between consecutive iShares API requests.
    /// Public so CrawlerViewModel and other consumers can reference it instead of hardcoding.
    /// </summary>
    public const int RequestDelayMs = 2000;

    /// <summary>
    /// iShares source ID (from seed_index_attribution.sql).
    /// </summary>
    private const int ISharesSourceId = 10;

    /// <summary>
    /// User-Agent header for iShares API requests (academic research, rate-limited).
    /// </summary>
    private const string UserAgent = "StockAnalyzer/1.0 (academic-research; single-concurrency; 2s-gap)";

    public event Action<string>? LogMessage;
    public event Action<IngestProgress>? ProgressUpdated;

    /// <summary>
    /// Creates a new ISharesConstituentService instance.
    /// </summary>
    /// <param name="httpClientFactory">DI-provided HTTP client factory</param>
    /// <param name="dbContext">EF Core database context</param>
    public ISharesConstituentService(IHttpClientFactory httpClientFactory, StockAnalyzerDbContext dbContext)
    {
        _httpClient = httpClientFactory.CreateClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(60); // AC1.4: 60-second timeout
        _dbContext = dbContext;

        // Load ETF configs from bundled JSON resource
        _etfConfigs = LoadEtfConfigs();
    }

    /// <summary>
    /// All configured ETF tickers and their metadata.
    /// </summary>
    public IReadOnlyDictionary<string, EtfConfig> EtfConfigs => _etfConfigs.AsReadOnly();

    /// <summary>
    /// Downloads and parses holdings for a single ETF.
    /// Stub: returns empty stats (full implementation in Task 10).
    /// </summary>
    public async Task<IngestStats> IngestEtfAsync(string etfTicker, DateTime? asOfDate = null, CancellationToken ct = default)
    {
        // Stub for Task 10
        return new IngestStats(Parsed: 0, Matched: 0, Created: 0, Inserted: 0, SkippedExisting: 0, Failed: 0, IdentifiersSet: 0);
    }

    /// <summary>
    /// Loads all configured ETFs with rate limiting.
    /// Stub: no-op (full implementation in Task 10).
    /// </summary>
    public async Task IngestAllEtfsAsync(DateTime? asOfDate = null, CancellationToken ct = default)
    {
        // Stub for Task 10
        await Task.CompletedTask;
    }

    /// <summary>
    /// Returns ETFs with stale constituent data.
    /// Stub: empty list (full implementation in Task 10).
    /// </summary>
    public async Task<IReadOnlyList<(string EtfTicker, string IndexCode)>> GetStaleEtfsAsync(CancellationToken ct = default)
    {
        // Stub for Task 10
        return new List<(string, string)>();
    }

    /// <summary>
    /// Downloads iShares holdings JSON for a given ETF and optional date.
    /// Handles BOM prefix, network errors, and unknown ETFs gracefully.
    /// Adjusts weekend dates to last business day.
    /// </summary>
    /// <param name="etfTicker">ETF ticker (e.g., "IVV")</param>
    /// <param name="asOfDate">Optional effective date; if null, uses current date</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Parsed JSON data or null on error</returns>
    internal async Task<JsonElement?> DownloadAsync(string etfTicker, DateTime? asOfDate = null, CancellationToken ct = default)
    {
        // AC1.3: Unknown ETF ticker returns null, no exception
        if (!_etfConfigs.TryGetValue(etfTicker.ToUpperInvariant(), out var config))
        {
            LogMessage?.Invoke($"Unknown ETF: {etfTicker}");
            return null;
        }

        // AC1.5: Adjust weekend dates to last business day
        var requestDate = asOfDate?.Date ?? DateTime.UtcNow.Date;
        var adjustedDate = AdjustToLastBusinessDay(requestDate);

        // Construct URL per iShares API format
        var url = $"https://www.ishares.com/us/products/{config.ProductId}/{config.Slug}/1467271812596.ajax?fileType=json&tab=all&asOfDate={adjustedDate:yyyyMMdd}";

        try
        {
            LogMessage?.Invoke($"Downloading {etfTicker} as of {adjustedDate:yyyy-MM-dd}");

            // Create request with User-Agent header
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("User-Agent", UserAgent);

            var response = await _httpClient.SendAsync(request, ct);

            // AC1.4: Network timeout or non-200 status returns null
            if (!response.IsSuccessStatusCode)
            {
                LogMessage?.Invoke($"Download failed: HTTP {response.StatusCode}");
                return null;
            }

            var text = await response.Content.ReadAsStringAsync(ct);

            // AC1.2: Strip UTF-8 BOM if present
            text = text.TrimStart('\uFEFF');

            // Parse JSON
            using var doc = JsonDocument.Parse(text);
            return doc.RootElement.Clone();
        }
        catch (TaskCanceledException)
        {
            // AC1.4: Timeout returns null, no exception propagated
            LogMessage?.Invoke($"Download timeout for {etfTicker}");
            return null;
        }
        catch (HttpRequestException ex)
        {
            // AC1.4: Network error returns null
            LogMessage?.Invoke($"Download failed: {ex.Message}");
            return null;
        }
        catch (JsonException ex)
        {
            // AC1.4: Malformed JSON returns null
            LogMessage?.Invoke($"Failed to parse JSON: {ex.Message}");
            return null;
        }
        catch (Exception ex)
        {
            // Catch-all for unexpected errors
            LogMessage?.Invoke($"Unexpected error downloading {etfTicker}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Loads ETF configurations from bundled ishares_etf_configs.json resource.
    /// Tries multiple paths: executing assembly, entry assembly, current directory.
    /// </summary>
    private Dictionary<string, EtfConfig> LoadEtfConfigs()
    {
        var configs = new Dictionary<string, EtfConfig>(StringComparer.OrdinalIgnoreCase);

        try
        {
            // Try multiple paths to find the config file
            var pathsToTry = new[]
            {
                // Path relative to executing assembly (ISharesConstituentService)
                () =>
                {
                    var assemblyDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                    return Path.Combine(assemblyDir ?? ".", "Resources", "ishares_etf_configs.json");
                },
                // Path relative to entry assembly (e.g., EodhdLoader.exe or test runner)
                () =>
                {
                    var assemblyDir = Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly()?.Location ?? "");
                    return Path.Combine(assemblyDir ?? ".", "Resources", "ishares_etf_configs.json");
                },
                // Path relative to current working directory
                () => Path.Combine(".", "Resources", "ishares_etf_configs.json"),
                // Absolute path up two levels (for running from bin\Debug or bin\Release)
                () => Path.Combine("..", "..", "src", "EodhdLoader", "Resources", "ishares_etf_configs.json")
            };

            string? foundPath = null;
            foreach (var pathFunc in pathsToTry)
            {
                try
                {
                    var path = pathFunc();
                    if (File.Exists(path))
                    {
                        foundPath = path;
                        break;
                    }
                }
                catch
                {
                    // Continue to next path
                }
            }

            if (foundPath != null && File.Exists(foundPath))
            {
                var json = File.ReadAllText(foundPath);
                // PropertyNameCaseInsensitive handles snake_case to PascalCase mapping
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var parsed = JsonSerializer.Deserialize<Dictionary<string, EtfConfig>>(json, options);

                if (parsed != null)
                {
                    foreach (var kvp in parsed)
                    {
                        configs[kvp.Key.ToUpperInvariant()] = kvp.Value;
                    }
                }
            }
            else
            {
                LogMessage?.Invoke($"Warning: ishares_etf_configs.json not found in any expected location");
            }
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke($"Error loading ETF configs: {ex.Message}");
        }

        return configs;
    }

    /// <summary>
    /// AC1.5: Adjusts weekend dates to last business day (Friday).
    /// </summary>
    private static DateTime AdjustToLastBusinessDay(DateTime date)
    {
        while (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            date = date.AddDays(-1);
        }
        return date;
    }
}
