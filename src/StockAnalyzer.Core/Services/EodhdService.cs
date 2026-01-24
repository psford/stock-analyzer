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

            _logger.LogInformation("Fetching EODHD bulk data for {Exchange} on {Date}",
                exchange, date.ToString("yyyy-MM-dd"));

            var response = await _httpClient.GetAsync(url, ct);

            if (!response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("EODHD bulk API returned {StatusCode}: {Content}",
                    response.StatusCode, content);
                return new List<EodhdBulkRecord>();
            }

            var data = await response.Content.ReadFromJsonAsync<List<EodhdBulkRecord>>(ct);

            _logger.LogInformation("EODHD bulk API returned {Count} records for {Date}",
                data?.Count ?? 0, date.ToString("yyyy-MM-dd"));

            return data ?? new List<EodhdBulkRecord>();
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
    public long Volume { get; set; }

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
    public long Volume { get; set; }

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
