using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StockAnalyzer.Core.Helpers;
using StockAnalyzer.Core.Services;

namespace StockAnalyzer.Core.Services;

/// <summary>
/// Service for fetching historical price data from EODHD (EOD Historical Data).
/// Primary source for bulk historical data loading and daily updates.
/// API docs: https://eodhd.com/financial-apis/
/// </summary>
public class EodhdService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<EodhdService> _logger;
    private readonly string _apiKey;

    private const string BaseUrl = "https://eodhd.com/api";

    public EodhdService(
        HttpClient httpClient,
        ILogger<EodhdService> logger,
        IConfiguration configuration)
    {
        _httpClient = httpClient;
        _logger = logger;
        _apiKey = configuration["Eodhd:ApiKey"]
                  ?? configuration["EodhdApiKey"]
                  ?? Environment.GetEnvironmentVariable("EODHD_API_KEY")
                  ?? "";
    }

    /// <summary>
    /// Check if the service is configured with an API key.
    /// </summary>
    public bool IsAvailable => !string.IsNullOrEmpty(_apiKey);

    /// <summary>
    /// Get historical EOD data for a single symbol.
    /// </summary>
    /// <param name="ticker">Stock ticker symbol (e.g., "AAPL")</param>
    /// <param name="startDate">Start date (inclusive)</param>
    /// <param name="endDate">End date (inclusive)</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>List of daily price records</returns>
    public async Task<List<EodhdPriceRecord>> GetHistoricalDataAsync(
        string ticker,
        DateTime startDate,
        DateTime endDate,
        CancellationToken ct = default)
    {
        if (!IsAvailable)
        {
            _logger.LogWarning("EODHD API key not configured");
            return new List<EodhdPriceRecord>();
        }

        try
        {
            // EODHD uses {SYMBOL}.{EXCHANGE} format - US stocks use .US suffix
            var symbol = ticker.ToUpperInvariant();
            if (!symbol.Contains('.'))
            {
                symbol = $"{symbol}.US";
            }

            var url = $"{BaseUrl}/eod/{symbol}?" +
                      $"api_token={_apiKey}&" +
                      $"fmt=json&" +
                      $"from={startDate:yyyy-MM-dd}&" +
                      $"to={endDate:yyyy-MM-dd}&" +
                      $"period=d&" +
                      $"order=a";

            _logger.LogDebug("Fetching EODHD data for {Symbol} from {Start} to {End}",
                LogSanitizer.Sanitize(symbol), startDate.ToString("yyyy-MM-dd"), endDate.ToString("yyyy-MM-dd"));

            var response = await _httpClient.GetAsync(url, ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("EODHD API returned {StatusCode} for {Symbol}",
                    response.StatusCode, LogSanitizer.Sanitize(symbol));
                return new List<EodhdPriceRecord>();
            }

            var data = await response.Content.ReadFromJsonAsync<List<EodhdPriceRecord>>(ct);
            return data ?? new List<EodhdPriceRecord>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching EODHD data for {Ticker}", LogSanitizer.Sanitize(ticker));
            return new List<EodhdPriceRecord>();
        }
    }

    /// <summary>
    /// Get bulk EOD data for an entire exchange for a specific date.
    /// Uses the bulk API which is more efficient (100 API calls per request vs 1 per ticker).
    /// </summary>
    /// <param name="date">The date to fetch data for</param>
    /// <param name="exchange">Exchange code (default: "US" for all US stocks)</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>List of price records for all tickers on the exchange</returns>
    public async Task<List<EodhdBulkRecord>> GetBulkEodDataAsync(
        DateTime date,
        string exchange = "US",
        CancellationToken ct = default)
    {
        if (!IsAvailable)
        {
            _logger.LogWarning("EODHD API key not configured");
            return new List<EodhdBulkRecord>();
        }

        try
        {
            var url = $"{BaseUrl}/eod-bulk-last-day/{exchange}?" +
                      $"api_token={_apiKey}&" +
                      $"fmt=json&" +
                      $"date={date:yyyy-MM-dd}&" +
                      $"filter=extended";

            // Log the URL (mask API key for security)
            var maskedUrl = url.Replace(_apiKey, "***");
            _logger.LogInformation("Fetching EODHD bulk data: {Url}", maskedUrl);

            var response = await _httpClient.GetAsync(url, ct);

            if (!response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("EODHD bulk API returned {StatusCode}: {Content}",
                    response.StatusCode, content);
                return new List<EodhdBulkRecord>();
            }

            // Read raw content first to see what we're getting
            var rawContent = await response.Content.ReadAsStringAsync(ct);
            _logger.LogInformation("EODHD bulk API raw response length: {Length} bytes for {Date}",
                rawContent.Length, date.ToString("yyyy-MM-dd"));

            if (string.IsNullOrEmpty(rawContent) || rawContent == "[]")
            {
                _logger.LogWarning("EODHD bulk API returned empty response for {Date}", date.ToString("yyyy-MM-dd"));
                return new List<EodhdBulkRecord>();
            }

            // Parse records individually to handle malformed data gracefully
            // A single bad record shouldn't take down the entire bulk import
            var (data, skipped) = ParseBulkRecordsWithFallback(rawContent, date);

            if (skipped > 0)
            {
                _logger.LogWarning("EODHD bulk API: Skipped {Skipped} malformed records for {Date}",
                    skipped, date.ToString("yyyy-MM-dd"));
            }

            _logger.LogInformation("EODHD bulk API returned {Count} valid records for {Date}",
                data.Count, date.ToString("yyyy-MM-dd"));

            return data;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching EODHD bulk data for {Date}", date);
            return new List<EodhdBulkRecord>();
        }
    }

    /// <summary>
    /// Get bulk EOD data for specific tickers on a specific date.
    /// More efficient than individual calls when fetching multiple tickers.
    /// </summary>
    /// <param name="tickers">List of ticker symbols</param>
    /// <param name="date">The date to fetch data for</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>List of price records for the specified tickers</returns>
    public async Task<List<EodhdBulkRecord>> GetBulkEodDataForTickersAsync(
        IEnumerable<string> tickers,
        DateTime date,
        CancellationToken ct = default)
    {
        if (!IsAvailable)
        {
            _logger.LogWarning("EODHD API key not configured");
            return new List<EodhdBulkRecord>();
        }

        var tickerList = tickers.ToList();
        if (tickerList.Count == 0)
        {
            return new List<EodhdBulkRecord>();
        }

        try
        {
            // Join tickers with comma, add .US suffix if not present
            var symbols = string.Join(",", tickerList.Select(t =>
                t.Contains('.') ? t.ToUpperInvariant() : $"{t.ToUpperInvariant()}.US"));

            var url = $"{BaseUrl}/eod-bulk-last-day/US?" +
                      $"api_token={_apiKey}&" +
                      $"fmt=json&" +
                      $"date={date:yyyy-MM-dd}&" +
                      $"symbols={symbols}";

            _logger.LogDebug("Fetching EODHD bulk data for {Count} tickers on {Date}",
                tickerList.Count, date.ToString("yyyy-MM-dd"));

            var response = await _httpClient.GetAsync(url, ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("EODHD bulk API returned {StatusCode} for ticker batch",
                    response.StatusCode);
                return new List<EodhdBulkRecord>();
            }

            var data = await response.Content.ReadFromJsonAsync<List<EodhdBulkRecord>>(ct);
            return data ?? new List<EodhdBulkRecord>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching EODHD bulk data for {Count} tickers", tickerList.Count);
            return new List<EodhdBulkRecord>();
        }
    }

    /// <summary>
    /// Get all symbols for a specific exchange.
    /// Uses the exchange-symbol-list endpoint to fetch all available securities.
    /// </summary>
    /// <param name="exchange">Exchange code (e.g., "US", "LSE", "TO")</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>List of symbol records for the exchange</returns>
    public async Task<List<EodhdSymbolRecord>> GetExchangeSymbolsAsync(
        string exchange = "US",
        CancellationToken ct = default)
    {
        if (!IsAvailable)
        {
            _logger.LogWarning("EODHD API key not configured");
            return new List<EodhdSymbolRecord>();
        }

        try
        {
            var url = $"{BaseUrl}/exchange-symbol-list/{exchange}?" +
                      $"api_token={_apiKey}&" +
                      $"fmt=json";

            _logger.LogInformation("Fetching EODHD symbol list for {Exchange}", exchange);

            var response = await _httpClient.GetAsync(url, ct);

            if (!response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("EODHD exchange symbols API returned {StatusCode}: {Content}",
                    response.StatusCode, content);
                return new List<EodhdSymbolRecord>();
            }

            var data = await response.Content.ReadFromJsonAsync<List<EodhdSymbolRecord>>(ct);

            _logger.LogInformation("EODHD exchange symbols API returned {Count} records for {Exchange}",
                data?.Count ?? 0, exchange);

            return data ?? new List<EodhdSymbolRecord>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching EODHD exchange symbols for {Exchange}", exchange);
            return new List<EodhdSymbolRecord>();
        }
    }

    /// <summary>
    /// Parse bulk records individually to handle malformed data gracefully.
    /// Returns tuple of (valid records, count of skipped records).
    /// </summary>
    private (List<EodhdBulkRecord> Records, int Skipped) ParseBulkRecordsWithFallback(string rawContent, DateTime date)
    {
        var records = new List<EodhdBulkRecord>();
        int skipped = 0;

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(rawContent);
            var root = doc.RootElement;

            if (root.ValueKind != System.Text.Json.JsonValueKind.Array)
            {
                _logger.LogWarning("EODHD bulk API response is not an array for {Date}", date.ToString("yyyy-MM-dd"));
                return (records, 0);
            }

            foreach (var element in root.EnumerateArray())
            {
                try
                {
                    var record = ParseBulkRecord(element);
                    if (record != null)
                    {
                        records.Add(record);
                    }
                    else
                    {
                        skipped++;
                    }
                }
                catch (Exception ex)
                {
                    skipped++;
                    // Log first few skipped records for debugging, then just count
                    if (skipped <= 5)
                    {
                        var code = element.TryGetProperty("code", out var codeProp) ? codeProp.GetString() : "unknown";
                        _logger.LogDebug(ex, "Skipping malformed EODHD record for {Code} on {Date}",
                            code, date.ToString("yyyy-MM-dd"));
                    }
                }
            }
        }
        catch (System.Text.Json.JsonException jsonEx)
        {
            _logger.LogError(jsonEx, "Failed to parse EODHD bulk response as JSON array for {Date}. First 500 chars: {Sample}",
                date.ToString("yyyy-MM-dd"), rawContent.Substring(0, Math.Min(500, rawContent.Length)));
        }

        return (records, skipped);
    }

    /// <summary>
    /// Parse a single bulk record from a JsonElement, handling type mismatches gracefully.
    /// </summary>
    private static EodhdBulkRecord? ParseBulkRecord(System.Text.Json.JsonElement element)
    {
        // Required fields - if these fail, skip the record
        if (!element.TryGetProperty("code", out var codeProp) || codeProp.ValueKind != System.Text.Json.JsonValueKind.String)
            return null;

        if (!element.TryGetProperty("date", out var dateProp) || dateProp.ValueKind != System.Text.Json.JsonValueKind.String)
            return null;

        var record = new EodhdBulkRecord
        {
            Code = codeProp.GetString() ?? string.Empty,
            Date = dateProp.GetString() ?? string.Empty,
            Exchange = GetStringOrDefault(element, "exchange_short_name"),
            Name = GetStringOrDefault(element, "name"),
            Open = GetDecimalOrDefault(element, "open"),
            High = GetDecimalOrDefault(element, "high"),
            Low = GetDecimalOrDefault(element, "low"),
            Close = GetDecimalOrDefault(element, "close"),
            AdjustedClose = GetDecimalOrDefault(element, "adjusted_close"),
            Volume = GetDecimalOrDefault(element, "volume")
        };

        return record;
    }

    private static string GetStringOrDefault(System.Text.Json.JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == System.Text.Json.JsonValueKind.String)
            return prop.GetString() ?? string.Empty;
        return string.Empty;
    }

    private static decimal GetDecimalOrDefault(System.Text.Json.JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var prop))
            return 0m;

        return prop.ValueKind switch
        {
            System.Text.Json.JsonValueKind.Number => prop.GetDecimal(),
            System.Text.Json.JsonValueKind.String when decimal.TryParse(prop.GetString(), out var d) => d,
            _ => 0m
        };
    }
}

/// <summary>
/// Symbol record from EODHD exchange-symbol-list endpoint.
/// </summary>
public class EodhdSymbolRecord
{
    [JsonPropertyName("Code")]
    public string Code { get; set; } = string.Empty;

    [JsonPropertyName("Name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("Country")]
    public string? Country { get; set; }

    [JsonPropertyName("Exchange")]
    public string Exchange { get; set; } = string.Empty;

    [JsonPropertyName("Currency")]
    public string? Currency { get; set; }

    [JsonPropertyName("Type")]
    public string? Type { get; set; }

    [JsonPropertyName("Isin")]
    public string? Isin { get; set; }

    /// <summary>
    /// Get the ticker symbol without exchange suffix.
    /// </summary>
    public string Ticker => Code.Contains('.') ? Code.Split('.')[0] : Code;
}

/// <summary>
/// Price record from EODHD historical endpoint.
/// </summary>
public class EodhdPriceRecord
{
    [JsonPropertyName("date")]
    public string Date { get; set; } = string.Empty;

    [JsonPropertyName("open")]
    public decimal Open { get; set; }

    [JsonPropertyName("high")]
    public decimal High { get; set; }

    [JsonPropertyName("low")]
    public decimal Low { get; set; }

    [JsonPropertyName("close")]
    public decimal Close { get; set; }

    [JsonPropertyName("adjusted_close")]
    public decimal AdjustedClose { get; set; }

    [JsonPropertyName("volume")]
    public decimal Volume { get; set; }

    /// <summary>
    /// Parse the date string to DateTime.
    /// </summary>
    public DateTime ParsedDate => DateTime.TryParse(Date, out var dt) ? dt : DateTime.MinValue;
}

/// <summary>
/// Price record from EODHD bulk endpoint.
/// </summary>
public class EodhdBulkRecord
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    [JsonPropertyName("exchange_short_name")]
    public string Exchange { get; set; } = string.Empty;

    [JsonPropertyName("date")]
    public string Date { get; set; } = string.Empty;

    [JsonPropertyName("open")]
    public decimal Open { get; set; }

    [JsonPropertyName("high")]
    public decimal High { get; set; }

    [JsonPropertyName("low")]
    public decimal Low { get; set; }

    [JsonPropertyName("close")]
    public decimal Close { get; set; }

    [JsonPropertyName("adjusted_close")]
    public decimal AdjustedClose { get; set; }

    [JsonPropertyName("volume")]
    public decimal Volume { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// Get the ticker symbol without exchange suffix.
    /// </summary>
    public string Ticker => Code.Contains('.') ? Code.Split('.')[0] : Code;

    /// <summary>
    /// Parse the date string to DateTime.
    /// </summary>
    public DateTime ParsedDate => DateTime.TryParse(Date, out var dt) ? dt : DateTime.MinValue;
}
