using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using StockAnalyzer.Core.Helpers;
using StockAnalyzer.Core.Models;

namespace StockAnalyzer.Core.Services;

/// <summary>
/// Stock data provider using Twelve Data API.
/// Primary provider with real-time quotes and good historical data.
/// Free tier: 8 calls/min, 800 calls/day.
/// Docs: https://twelvedata.com/docs
/// </summary>
public class TwelveDataService : IStockDataProvider
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly RateLimitTracker _rateLimiter;
    private readonly ILogger<TwelveDataService>? _logger;
    private const string BaseUrl = "https://api.twelvedata.com";

    public string ProviderName => "TwelveData";
    public int Priority => 1; // Highest priority
    public bool IsAvailable => !string.IsNullOrEmpty(_apiKey) && _rateLimiter.CanMakeCall();

    public TwelveDataService(string apiKey, ILogger<TwelveDataService>? logger = null, HttpClient? httpClient = null)
    {
        _apiKey = apiKey;
        _logger = logger;
        _httpClient = httpClient ?? new HttpClient();
        _rateLimiter = new RateLimitTracker(maxPerMinute: 8, maxPerDay: 800);

        // Configure auth header (preferred method per docs)
        if (!string.IsNullOrEmpty(apiKey))
        {
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"apikey {apiKey}");
        }
    }

    /// <summary>
    /// Get rate limit tracker for monitoring.
    /// </summary>
    public RateLimitTracker RateLimiter => _rateLimiter;

    public async Task<StockInfo?> GetStockInfoAsync(string symbol, CancellationToken ct = default)
    {
        if (!IsAvailable)
        {
            _logger?.LogDebug("TwelveData not available (no key or rate limited)");
            return null;
        }

        try
        {
            _rateLimiter.RecordCall();
            var url = $"{BaseUrl}/quote?symbol={symbol.ToUpper()}";
            var response = await _httpClient.GetFromJsonAsync<TwelveDataQuote>(url, ct);

            if (response == null || response.Status == "error" || string.IsNullOrEmpty(response.Symbol))
            {
                _logger?.LogDebug("TwelveData returned no data for {Symbol}", LogSanitizer.Sanitize(symbol));
                return null;
            }

            return MapToStockInfo(response);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "TwelveData failed for {Symbol}", LogSanitizer.Sanitize(symbol));
            return null;
        }
    }

    public async Task<HistoricalDataResult?> GetHistoricalDataAsync(
        string symbol,
        string period = "1y",
        CancellationToken ct = default)
    {
        if (!IsAvailable)
        {
            _logger?.LogDebug("TwelveData not available (no key or rate limited)");
            return null;
        }

        try
        {
            _rateLimiter.RecordCall();
            var outputSize = GetOutputSize(period);
            var url = $"{BaseUrl}/time_series?symbol={symbol.ToUpper()}&interval=1day&outputsize={outputSize}";
            var response = await _httpClient.GetFromJsonAsync<TwelveDataTimeSeries>(url, ct);

            if (response == null || response.Status == "error" || response.Values == null || response.Values.Count == 0)
            {
                _logger?.LogDebug("TwelveData returned no historical data for {Symbol}", LogSanitizer.Sanitize(symbol));
                return null;
            }

            return MapToHistoricalData(response, symbol, period);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "TwelveData historical failed for {Symbol}", LogSanitizer.Sanitize(symbol));
            return null;
        }
    }

    public async Task<List<SearchResult>> SearchAsync(string query, CancellationToken ct = default)
    {
        if (!IsAvailable || string.IsNullOrWhiteSpace(query) || query.Length < 2)
            return new List<SearchResult>();

        try
        {
            _rateLimiter.RecordCall();
            var url = $"{BaseUrl}/symbol_search?symbol={Uri.EscapeDataString(query)}";
            var response = await _httpClient.GetFromJsonAsync<TwelveDataSearch>(url, ct);

            if (response?.Data == null)
                return new List<SearchResult>();

            return response.Data
                .Where(d => d.InstrumentType == "Common Stock" || d.InstrumentType == "ETF")
                .Take(8)
                .Select(d => new SearchResult
                {
                    Symbol = d.Symbol ?? "",
                    ShortName = d.InstrumentName ?? d.Symbol ?? "",
                    LongName = d.InstrumentName,
                    Exchange = d.Exchange,
                    Type = d.InstrumentType
                })
                .ToList();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "TwelveData search failed for {Query}", LogSanitizer.Sanitize(query));
            return new List<SearchResult>();
        }
    }

    public Task<List<(string Symbol, string Name)>> GetTrendingStocksAsync(
        int count = 10,
        CancellationToken ct = default)
    {
        // Twelve Data doesn't have a trending endpoint
        return Task.FromResult(new List<(string, string)>());
    }

    #region Helper Methods

    private static StockInfo MapToStockInfo(TwelveDataQuote quote)
    {
        return new StockInfo
        {
            Symbol = quote.Symbol?.ToUpper() ?? "",
            ShortName = quote.Name ?? quote.Symbol ?? "",
            LongName = quote.Name,
            Exchange = quote.Exchange,
            Currency = quote.Currency,

            CurrentPrice = ParseDecimal(quote.Close),
            PreviousClose = ParseDecimal(quote.PreviousClose),
            Open = ParseDecimal(quote.Open),
            DayHigh = ParseDecimal(quote.High),
            DayLow = ParseDecimal(quote.Low),
            Volume = ParseLong(quote.Volume),
            AverageVolume = ParseLong(quote.AverageVolume),

            // 52-week data from nested object
            FiftyTwoWeekHigh = ParseDecimal(quote.FiftyTwoWeek?.High),
            FiftyTwoWeekLow = ParseDecimal(quote.FiftyTwoWeek?.Low),

            // Twelve Data doesn't provide these in basic quote
            MarketCap = null,
            PeRatio = null,
            ForwardPeRatio = null,
            DividendYield = null,
            DividendRate = null,
            FiftyDayAverage = null,
            TwoHundredDayAverage = null
        };
    }

    private static HistoricalDataResult MapToHistoricalData(TwelveDataTimeSeries series, string symbol, string period)
    {
        var ohlcvData = series.Values!
            .Select(v => new OhlcvData
            {
                Date = DateTime.Parse(v.Datetime ?? DateTime.MinValue.ToString("yyyy-MM-dd")),
                Open = ParseDecimal(v.Open) ?? 0,
                High = ParseDecimal(v.High) ?? 0,
                Low = ParseDecimal(v.Low) ?? 0,
                Close = ParseDecimal(v.Close) ?? 0,
                Volume = ParseLong(v.Volume) ?? 0,
                AdjustedClose = null
            })
            .Where(d => d.Date > DateTime.MinValue)
            .OrderBy(d => d.Date)
            .ToList();

        return new HistoricalDataResult
        {
            Symbol = symbol.ToUpper(),
            Period = period,
            StartDate = ohlcvData.FirstOrDefault()?.Date ?? DateTime.MinValue,
            EndDate = ohlcvData.LastOrDefault()?.Date ?? DateTime.MinValue,
            Data = ohlcvData
        };
    }

    private static int GetOutputSize(string period) => period.ToLower() switch
    {
        "1d" => 1,
        "5d" => 5,
        "1mo" => 22,
        "3mo" => 66,
        "6mo" => 132,
        "1y" => 252,
        "2y" => 504,
        "5y" => 1260,
        "10y" => 2520,
        "ytd" => (int)(DateTime.Now - new DateTime(DateTime.Now.Year, 1, 1)).TotalDays,
        _ => 252
    };

    private static decimal? ParseDecimal(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        return decimal.TryParse(value, out var result) ? result : null;
    }

    private static long? ParseLong(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        return long.TryParse(value, out var result) ? result : null;
    }

    #endregion

    #region Response Models

    private class TwelveDataQuote
    {
        [JsonPropertyName("symbol")]
        public string? Symbol { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("exchange")]
        public string? Exchange { get; set; }

        [JsonPropertyName("mic_code")]
        public string? MicCode { get; set; }

        [JsonPropertyName("currency")]
        public string? Currency { get; set; }

        [JsonPropertyName("datetime")]
        public string? Datetime { get; set; }

        [JsonPropertyName("timestamp")]
        public long? Timestamp { get; set; }

        [JsonPropertyName("open")]
        public string? Open { get; set; }

        [JsonPropertyName("high")]
        public string? High { get; set; }

        [JsonPropertyName("low")]
        public string? Low { get; set; }

        [JsonPropertyName("close")]
        public string? Close { get; set; }

        [JsonPropertyName("volume")]
        public string? Volume { get; set; }

        [JsonPropertyName("previous_close")]
        public string? PreviousClose { get; set; }

        [JsonPropertyName("change")]
        public string? Change { get; set; }

        [JsonPropertyName("percent_change")]
        public string? PercentChange { get; set; }

        [JsonPropertyName("average_volume")]
        public string? AverageVolume { get; set; }

        [JsonPropertyName("is_market_open")]
        public bool? IsMarketOpen { get; set; }

        [JsonPropertyName("fifty_two_week")]
        public FiftyTwoWeekData? FiftyTwoWeek { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }
    }

    private class FiftyTwoWeekData
    {
        [JsonPropertyName("low")]
        public string? Low { get; set; }

        [JsonPropertyName("high")]
        public string? High { get; set; }

        [JsonPropertyName("low_change")]
        public string? LowChange { get; set; }

        [JsonPropertyName("high_change")]
        public string? HighChange { get; set; }

        [JsonPropertyName("low_change_percent")]
        public string? LowChangePercent { get; set; }

        [JsonPropertyName("high_change_percent")]
        public string? HighChangePercent { get; set; }

        [JsonPropertyName("range")]
        public string? Range { get; set; }
    }

    private class TwelveDataTimeSeries
    {
        [JsonPropertyName("meta")]
        public TimeSeriesMeta? Meta { get; set; }

        [JsonPropertyName("values")]
        public List<TimeSeriesValue>? Values { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }
    }

    private class TimeSeriesMeta
    {
        [JsonPropertyName("symbol")]
        public string? Symbol { get; set; }

        [JsonPropertyName("interval")]
        public string? Interval { get; set; }

        [JsonPropertyName("currency")]
        public string? Currency { get; set; }

        [JsonPropertyName("exchange_timezone")]
        public string? ExchangeTimezone { get; set; }

        [JsonPropertyName("exchange")]
        public string? Exchange { get; set; }

        [JsonPropertyName("type")]
        public string? Type { get; set; }
    }

    private class TimeSeriesValue
    {
        [JsonPropertyName("datetime")]
        public string? Datetime { get; set; }

        [JsonPropertyName("open")]
        public string? Open { get; set; }

        [JsonPropertyName("high")]
        public string? High { get; set; }

        [JsonPropertyName("low")]
        public string? Low { get; set; }

        [JsonPropertyName("close")]
        public string? Close { get; set; }

        [JsonPropertyName("volume")]
        public string? Volume { get; set; }
    }

    private class TwelveDataSearch
    {
        [JsonPropertyName("data")]
        public List<SearchData>? Data { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }
    }

    private class SearchData
    {
        [JsonPropertyName("symbol")]
        public string? Symbol { get; set; }

        [JsonPropertyName("instrument_name")]
        public string? InstrumentName { get; set; }

        [JsonPropertyName("exchange")]
        public string? Exchange { get; set; }

        [JsonPropertyName("mic_code")]
        public string? MicCode { get; set; }

        [JsonPropertyName("exchange_timezone")]
        public string? ExchangeTimezone { get; set; }

        [JsonPropertyName("instrument_type")]
        public string? InstrumentType { get; set; }

        [JsonPropertyName("country")]
        public string? Country { get; set; }

        [JsonPropertyName("currency")]
        public string? Currency { get; set; }
    }

    #endregion
}
