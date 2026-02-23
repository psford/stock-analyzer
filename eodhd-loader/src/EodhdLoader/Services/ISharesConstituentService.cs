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

    /// <summary>
    /// AC2: Parses iShares JSON holdings data, auto-detecting Format A/B and filtering equity only.
    /// Internal so tests can access via InternalsVisibleTo.
    /// </summary>
    /// <param name="data">JSON element with aaData array</param>
    /// <returns>List of parsed holdings (equity only, non-equity rows filtered)</returns>
    internal static List<ISharesHolding> ParseHoldings(JsonElement data)
    {
        var holdings = new List<ISharesHolding>();

        // Extract aaData array from JSON
        if (!data.TryGetProperty("aaData", out var aaDataElement) || aaDataElement.ValueKind != JsonValueKind.Array)
        {
            return holdings; // AC2.4: Malformed JSON returns empty list
        }

        var rows = aaDataElement.EnumerateArray().ToList();
        if (rows.Count == 0)
        {
            return holdings;
        }

        // Detect format (Format A vs Format B)
        var isFormatB = DetectFormatB(rows[0]);
        var colMap = isFormatB ? GetFormatBColumns() : GetFormatAColumns();

        // Parse each row
        foreach (var row in rows)
        {
            if (row.ValueKind != JsonValueKind.Array)
                continue;

            var rowArray = row.EnumerateArray().ToList();

            // Skip rows that are too short
            if (rowArray.Count < 15)
                continue;

            // Get asset class and filter non-equity
            var assetClassValue = GetStringValue(rowArray, colMap["asset_class"]);
            if (IsNonEquityAssetClass(assetClassValue))
                continue;

            // Parse holding
            try
            {
                var holding = new ISharesHolding(
                    Ticker: GetStringValue(rowArray, colMap["ticker"]) ?? "",
                    Name: GetStringValue(rowArray, colMap["name"]) ?? "",
                    Sector: GetStringValue(rowArray, colMap["sector"]),
                    MarketValue: GetDecimalValue(rowArray, colMap["market_value"]),
                    Weight: GetWeightValue(rowArray, colMap["weight_pct"]),
                    Shares: GetDecimalValue(rowArray, colMap["quantity"]),
                    Location: GetStringValue(rowArray, colMap["location"]),
                    Exchange: GetStringValue(rowArray, colMap["exchange"]),
                    Currency: GetStringValue(rowArray, colMap["currency"]),
                    Cusip: CleanIdentifier(GetStringValue(rowArray, colMap["cusip"])),
                    Isin: CleanIdentifier(GetStringValue(rowArray, colMap["isin"])),
                    Sedol: CleanIdentifier(GetStringValue(rowArray, colMap["sedol"]))
                );

                holdings.Add(holding);
            }
            catch
            {
                // AC2.4: Skip malformed rows, continue processing
                continue;
            }
        }

        return holdings;
    }

    /// <summary>
    /// Detects if JSON is Format B (19 cols, col[4] is string) vs Format A (17 cols, col[4] is object).
    /// </summary>
    private static bool DetectFormatB(JsonElement firstRow)
    {
        if (firstRow.ValueKind != JsonValueKind.Array)
            return false;

        var rowArray = firstRow.EnumerateArray().ToList();

        // Format B: 19+ columns and col[4] is a string (asset class)
        // Format A: col[4] is an object (market value with {display, raw})
        if (rowArray.Count >= 19)
        {
            var col4 = rowArray[4];
            return col4.ValueKind == JsonValueKind.String;
        }

        return false;
    }

    /// <summary>
    /// Column indices for Format A (17 cols, broad ETFs like IVV, IWB).
    /// </summary>
    private static Dictionary<string, int> GetFormatAColumns()
    {
        return new Dictionary<string, int>
        {
            ["ticker"] = 0,
            ["name"] = 1,
            ["sector"] = 2,
            ["asset_class"] = 3,
            ["market_value"] = 4,
            ["weight_pct"] = 5,
            ["quantity"] = 7,
            ["cusip"] = 8,
            ["isin"] = 9,
            ["sedol"] = 10,
            ["price"] = 11,
            ["location"] = 12,
            ["exchange"] = 13,
            ["currency"] = 14,
        };
    }

    /// <summary>
    /// Column indices for Format B (19 cols, S&P style like IJH, IJK).
    /// </summary>
    private static Dictionary<string, int> GetFormatBColumns()
    {
        return new Dictionary<string, int>
        {
            ["ticker"] = 0,
            ["name"] = 1,
            ["sector"] = 3,
            ["asset_class"] = 4,
            ["market_value"] = 5,
            ["weight_pct"] = 17,
            ["quantity"] = 7,
            ["cusip"] = 8,
            ["isin"] = 9,
            ["sedol"] = 10,
            ["price"] = 11,
            ["location"] = 12,
            ["exchange"] = 13,
            ["currency"] = 14,
        };
    }

    /// <summary>
    /// Non-equity asset classes to filter out.
    /// </summary>
    private static readonly HashSet<string> NonEquityAssetClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Cash",
        "Cash Collateral and Margins",
        "Cash and/or Derivatives",
        "Futures",
        "Money Market",
    };

    /// <summary>
    /// Checks if asset class should be filtered (non-equity).
    /// </summary>
    private static bool IsNonEquityAssetClass(string? assetClass)
    {
        return string.IsNullOrWhiteSpace(assetClass) || NonEquityAssetClasses.Contains(assetClass);
    }

    /// <summary>
    /// Extracts string value from JSON cell (direct string or from display property).
    /// </summary>
    private static string? GetStringValue(List<JsonElement> row, int colIndex)
    {
        if (colIndex < 0 || colIndex >= row.Count)
            return null;

        var element = row[colIndex];

        // If it's a dict with "display" property, try that first
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty("display", out var displayProp))
        {
            var val = displayProp.GetString();
            return string.IsNullOrWhiteSpace(val) ? null : val.Trim();
        }

        // Direct string
        if (element.ValueKind == JsonValueKind.String)
        {
            var val = element.GetString();
            return string.IsNullOrWhiteSpace(val) ? null : val.Trim();
        }

        return null;
    }

    /// <summary>
    /// Extracts decimal value from JSON cell (handles dicts with raw, strings with commas, nulls).
    /// </summary>
    private static decimal? GetDecimalValue(List<JsonElement> row, int colIndex)
    {
        if (colIndex < 0 || colIndex >= row.Count)
            return null;

        var element = row[colIndex];

        // If it's a dict with "raw" property, extract numeric value
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("raw", out var rawProp))
            {
                if (rawProp.ValueKind == JsonValueKind.Number)
                    return rawProp.GetDecimal();
                if (rawProp.ValueKind == JsonValueKind.String)
                {
                    var str = rawProp.GetString();
                    return ParseNumericString(str);
                }
                // Null raw property
                return null;
            }
        }

        // Direct numeric
        if (element.ValueKind == JsonValueKind.Number)
            return element.GetDecimal();

        // Direct string
        if (element.ValueKind == JsonValueKind.String)
        {
            var str = element.GetString();
            return ParseNumericString(str);
        }

        return null;
    }

    /// <summary>
    /// Extracts weight percentage and converts from percentage to decimal (e.g., 6.5 -> 0.065).
    /// </summary>
    private static decimal? GetWeightValue(List<JsonElement> row, int colIndex)
    {
        var pctValue = GetDecimalValue(row, colIndex);
        if (pctValue == null)
            return null;

        // Weight in source data is percentage (e.g., 6.5 = 6.5%), convert to decimal
        return pctValue / 100;
    }

    /// <summary>
    /// Parses numeric string, handling commas, hyphens, empty, and "N/A".
    /// </summary>
    private static decimal? ParseNumericString(string? str)
    {
        if (string.IsNullOrWhiteSpace(str))
            return null;

        var cleaned = str.Replace(",", "").Trim();

        if (cleaned == "-" || cleaned == "" || cleaned == "N/A")
            return null;

        if (decimal.TryParse(cleaned, System.Globalization.CultureInfo.InvariantCulture, out var result))
            return result;

        return null;
    }

    /// <summary>
    /// Cleans identifier value: strips whitespace, returns null for "-", "", "N/A".
    /// </summary>
    private static string? CleanIdentifier(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var cleaned = value.Trim();

        if (cleaned == "-" || cleaned == "" || cleaned == "N/A")
            return null;

        return cleaned;
    }
}
