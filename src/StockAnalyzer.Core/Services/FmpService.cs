using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using StockAnalyzer.Core.Models;

namespace StockAnalyzer.Core.Services;

/// <summary>
/// Stock data provider using Financial Modeling Prep API.
/// Secondary provider with good fundamentals data.
/// Free tier: 250 calls/day, limited symbol coverage (~87 symbols).
/// Docs: https://site.financialmodelingprep.com/developer/docs
/// </summary>
public class FmpService : IStockDataProvider
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly RateLimitTracker _rateLimiter;
    private readonly ILogger<FmpService>? _logger;
    private const string BaseUrl = "https://financialmodelingprep.com/stable";

    public string ProviderName => "FMP";
    public int Priority => 2; // Second priority
    public bool IsAvailable => !string.IsNullOrEmpty(_apiKey) && _rateLimiter.CanMakeCall();

    public FmpService(string apiKey, ILogger<FmpService>? logger = null, HttpClient? httpClient = null)
    {
        _apiKey = apiKey;
        _logger = logger;
        _httpClient = httpClient ?? new HttpClient();
        _rateLimiter = new RateLimitTracker(maxPerMinute: int.MaxValue, maxPerDay: 250);
    }

    /// <summary>
    /// Get rate limit tracker for monitoring.
    /// </summary>
    public RateLimitTracker RateLimiter => _rateLimiter;

    public async Task<StockInfo?> GetStockInfoAsync(string symbol, CancellationToken ct = default)
    {
        if (!IsAvailable)
        {
            _logger?.LogDebug("FMP not available (no key or rate limited)");
            return null;
        }

        try
        {
            _rateLimiter.RecordCall();
            var url = $"{BaseUrl}/quote?symbol={symbol.ToUpper()}&apikey={_apiKey}";
            var response = await _httpClient.GetFromJsonAsync<List<FmpQuote>>(url, ct);

            // FMP returns arrays even for single results
            var quote = response?.FirstOrDefault();
            if (quote == null || string.IsNullOrEmpty(quote.Symbol))
            {
                _logger?.LogDebug("FMP returned no data for {Symbol}", symbol);
                return null;
            }

            return MapToStockInfo(quote);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "FMP failed for {Symbol}", symbol);
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
            _logger?.LogDebug("FMP not available (no key or rate limited)");
            return null;
        }

        try
        {
            _rateLimiter.RecordCall();
            var (fromDate, toDate) = GetDateRange(period);
            var url = $"{BaseUrl}/historical-price-eod/full?symbol={symbol.ToUpper()}&from={fromDate:yyyy-MM-dd}&to={toDate:yyyy-MM-dd}&apikey={_apiKey}";
            var response = await _httpClient.GetFromJsonAsync<FmpHistorical>(url, ct);

            if (response?.Historical == null || response.Historical.Count == 0)
            {
                _logger?.LogDebug("FMP returned no historical data for {Symbol}", symbol);
                return null;
            }

            return MapToHistoricalData(response, symbol, period);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "FMP historical failed for {Symbol}", symbol);
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
            var url = $"{BaseUrl}/search-symbol?query={Uri.EscapeDataString(query)}&apikey={_apiKey}";
            var response = await _httpClient.GetFromJsonAsync<List<FmpSearchResult>>(url, ct);

            if (response == null)
                return new List<SearchResult>();

            return response
                .Take(8)
                .Select(r => new SearchResult
                {
                    Symbol = r.Symbol ?? "",
                    ShortName = r.Name ?? r.Symbol ?? "",
                    LongName = r.Name,
                    Exchange = r.StockExchange,
                    Type = "EQUITY"
                })
                .ToList();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "FMP search failed for {Query}", query);
            return new List<SearchResult>();
        }
    }

    public Task<List<(string Symbol, string Name)>> GetTrendingStocksAsync(
        int count = 10,
        CancellationToken ct = default)
    {
        // FMP has actives endpoint but may not be in free tier
        return Task.FromResult(new List<(string, string)>());
    }

    #region Helper Methods

    private static StockInfo MapToStockInfo(FmpQuote quote)
    {
        return new StockInfo
        {
            Symbol = quote.Symbol?.ToUpper() ?? "",
            ShortName = quote.Name ?? quote.Symbol ?? "",
            LongName = quote.Name,
            Exchange = quote.Exchange,
            Currency = "USD", // FMP doesn't always include currency in quote

            CurrentPrice = quote.Price,
            PreviousClose = quote.PreviousClose,
            Open = quote.Open,
            DayHigh = quote.DayHigh,
            DayLow = quote.DayLow,
            Volume = quote.Volume,
            AverageVolume = quote.AvgVolume,

            MarketCap = quote.MarketCap,
            PeRatio = quote.Pe,

            FiftyTwoWeekHigh = quote.YearHigh,
            FiftyTwoWeekLow = quote.YearLow,
            FiftyDayAverage = quote.PriceAvg50,
            TwoHundredDayAverage = quote.PriceAvg200,

            // FMP quote doesn't include these
            ForwardPeRatio = null,
            DividendYield = null,
            DividendRate = null
        };
    }

    private static HistoricalDataResult MapToHistoricalData(FmpHistorical data, string symbol, string period)
    {
        var ohlcvData = data.Historical!
            .Select(h => new OhlcvData
            {
                Date = DateTime.Parse(h.Date ?? DateTime.MinValue.ToString("yyyy-MM-dd")),
                Open = h.Open ?? 0,
                High = h.High ?? 0,
                Low = h.Low ?? 0,
                Close = h.Close ?? 0,
                Volume = h.Volume ?? 0,
                AdjustedClose = h.AdjClose
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

    private static (DateTime From, DateTime To) GetDateRange(string period)
    {
        var to = DateTime.Now;
        var from = period.ToLower() switch
        {
            "ytd" => new DateTime(to.Year, 1, 1),
            "1d" => to.AddDays(-1),
            "5d" => to.AddDays(-5),
            "1mo" => to.AddMonths(-1),
            "3mo" => to.AddMonths(-3),
            "6mo" => to.AddMonths(-6),
            "1y" => to.AddYears(-1),
            "2y" => to.AddYears(-2),
            "5y" => to.AddYears(-5),
            "10y" => to.AddYears(-10),
            _ => to.AddYears(-1)
        };
        return (from, to);
    }

    #endregion

    #region Response Models

    private class FmpQuote
    {
        [JsonPropertyName("symbol")]
        public string? Symbol { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("price")]
        public decimal? Price { get; set; }

        [JsonPropertyName("change")]
        public decimal? Change { get; set; }

        [JsonPropertyName("changesPercentage")]
        public decimal? ChangesPercentage { get; set; }

        [JsonPropertyName("dayLow")]
        public decimal? DayLow { get; set; }

        [JsonPropertyName("dayHigh")]
        public decimal? DayHigh { get; set; }

        [JsonPropertyName("yearLow")]
        public decimal? YearLow { get; set; }

        [JsonPropertyName("yearHigh")]
        public decimal? YearHigh { get; set; }

        [JsonPropertyName("marketCap")]
        public decimal? MarketCap { get; set; }

        [JsonPropertyName("priceAvg50")]
        public decimal? PriceAvg50 { get; set; }

        [JsonPropertyName("priceAvg200")]
        public decimal? PriceAvg200 { get; set; }

        [JsonPropertyName("volume")]
        public long? Volume { get; set; }

        [JsonPropertyName("avgVolume")]
        public long? AvgVolume { get; set; }

        [JsonPropertyName("exchange")]
        public string? Exchange { get; set; }

        [JsonPropertyName("open")]
        public decimal? Open { get; set; }

        [JsonPropertyName("previousClose")]
        public decimal? PreviousClose { get; set; }

        [JsonPropertyName("eps")]
        public decimal? Eps { get; set; }

        [JsonPropertyName("pe")]
        public decimal? Pe { get; set; }

        [JsonPropertyName("sharesOutstanding")]
        public long? SharesOutstanding { get; set; }

        [JsonPropertyName("timestamp")]
        public long? Timestamp { get; set; }
    }

    private class FmpHistorical
    {
        [JsonPropertyName("symbol")]
        public string? Symbol { get; set; }

        [JsonPropertyName("historical")]
        public List<FmpHistoricalPrice>? Historical { get; set; }
    }

    private class FmpHistoricalPrice
    {
        [JsonPropertyName("date")]
        public string? Date { get; set; }

        [JsonPropertyName("open")]
        public decimal? Open { get; set; }

        [JsonPropertyName("high")]
        public decimal? High { get; set; }

        [JsonPropertyName("low")]
        public decimal? Low { get; set; }

        [JsonPropertyName("close")]
        public decimal? Close { get; set; }

        [JsonPropertyName("adjClose")]
        public decimal? AdjClose { get; set; }

        [JsonPropertyName("volume")]
        public long? Volume { get; set; }

        [JsonPropertyName("unadjustedVolume")]
        public long? UnadjustedVolume { get; set; }

        [JsonPropertyName("change")]
        public decimal? Change { get; set; }

        [JsonPropertyName("changePercent")]
        public decimal? ChangePercent { get; set; }

        [JsonPropertyName("vwap")]
        public decimal? Vwap { get; set; }

        [JsonPropertyName("label")]
        public string? Label { get; set; }

        [JsonPropertyName("changeOverTime")]
        public decimal? ChangeOverTime { get; set; }
    }

    private class FmpSearchResult
    {
        [JsonPropertyName("symbol")]
        public string? Symbol { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("currency")]
        public string? Currency { get; set; }

        [JsonPropertyName("stockExchange")]
        public string? StockExchange { get; set; }

        [JsonPropertyName("exchangeShortName")]
        public string? ExchangeShortName { get; set; }
    }

    #endregion
}
