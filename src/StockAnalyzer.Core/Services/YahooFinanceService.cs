using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using OoplesFinance.YahooFinanceAPI;
using OoplesFinance.YahooFinanceAPI.Enums;
using StockAnalyzer.Core.Helpers;
using StockAnalyzer.Core.Models;

namespace StockAnalyzer.Core.Services;

/// <summary>
/// Stock data provider using Yahoo Finance via OoplesFinance.YahooFinanceAPI.
/// Fallback provider with full symbol coverage but scraping-based (less reliable).
/// </summary>
public class YahooFinanceService : IStockDataProvider
{
    private readonly ILogger<YahooFinanceService>? _logger;

    public string ProviderName => "Yahoo";
    public int Priority => 3; // Lowest priority (fallback)
    public bool IsAvailable => true; // Always available as fallback

    public YahooFinanceService(ILogger<YahooFinanceService>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Get basic stock information for a ticker.
    /// </summary>
    public async Task<StockInfo?> GetStockInfoAsync(string symbol, CancellationToken ct = default)
    {
        try
        {
            var yahooClient = new YahooClient();
            var summary = await yahooClient.GetSummaryDetailsAsync(symbol);

            if (summary == null)
                return null;

            // Try to get asset profile for description and additional info
            string? description = null;
            string? sector = null;
            string? industry = null;
            string? website = null;
            string? country = null;
            int? employees = null;

            try
            {
                var profile = await yahooClient.GetAssetProfileAsync(symbol);
                if (profile != null)
                {
                    description = TryGetString(profile, "LongBusinessSummary");
                    sector = TryGetString(profile, "Sector");
                    industry = TryGetString(profile, "Industry");
                    website = TryGetString(profile, "Website");
                    country = TryGetString(profile, "Country");
                    employees = TryGetInt(profile, "FullTimeEmployees");
                }
            }
            catch
            {
                // Profile fetch failed, continue with basic info
            }

            return new StockInfo
            {
                Symbol = symbol.ToUpper(),
                ShortName = symbol.ToUpper(),
                LongName = null,
                Sector = sector,
                Industry = industry,
                Website = website,
                Country = country,
                Currency = summary.Currency,
                Exchange = null,
                Description = description,
                FullTimeEmployees = employees,

                CurrentPrice = TryGetDecimal(summary.Open),
                PreviousClose = TryGetDecimal(summary.PreviousClose),
                Open = TryGetDecimal(summary.Open),
                DayHigh = TryGetDecimal(summary.DayHigh),
                DayLow = TryGetDecimal(summary.DayLow),
                Volume = TryGetLong(summary.Volume),
                AverageVolume = TryGetLong(summary.AverageVolume),

                MarketCap = TryGetDecimal(summary.MarketCap),
                PeRatio = TryGetDecimal(summary.TrailingPE),
                ForwardPeRatio = TryGetDecimal(summary.ForwardPE),
                PegRatio = null,
                PriceToBook = null,

                DividendYield = ValidateDividendYield(TryGetDecimal(summary.DividendYield)),
                DividendRate = TryGetDecimal(summary.DividendRate),

                FiftyTwoWeekHigh = TryGetDecimal(summary.FiftyTwoWeekHigh),
                FiftyTwoWeekLow = TryGetDecimal(summary.FiftyTwoWeekLow),
                FiftyDayAverage = TryGetDecimal(summary.FiftyDayAverage),
                TwoHundredDayAverage = TryGetDecimal(summary.TwoHundredDayAverage)
            };
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Yahoo Finance failed for {Symbol}", LogSanitizer.Sanitize(symbol));
            return null;
        }
    }

    /// <summary>
    /// Get historical OHLCV data for a ticker.
    /// </summary>
    public async Task<HistoricalDataResult?> GetHistoricalDataAsync(
        string symbol,
        string period = "1y",
        CancellationToken ct = default)
    {
        try
        {
            var yahooClient = new YahooClient();
            var startDate = GetStartDate(period);

            var history = await yahooClient.GetHistoricalDataAsync(
                symbol,
                DataFrequency.Daily,
                startDate);

            if (history == null || !history.Any())
                return null;

            var ohlcvData = history.Select(h => new OhlcvData
            {
                Date = h.Date,
                Open = (decimal)h.Open,
                High = (decimal)h.High,
                Low = (decimal)h.Low,
                Close = (decimal)h.Close,
                Volume = (long)h.Volume,
                AdjustedClose = null
            }).OrderBy(d => d.Date).ToList();

            return new HistoricalDataResult
            {
                Symbol = symbol.ToUpper(),
                Period = period,
                StartDate = ohlcvData.First().Date,
                EndDate = ohlcvData.Last().Date,
                Data = ohlcvData
            };
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Yahoo Finance historical failed for {Symbol}", LogSanitizer.Sanitize(symbol));
            return null;
        }
    }

    /// <summary>
    /// Search for tickers by symbol or company name using Yahoo Finance search API.
    /// </summary>
    public async Task<List<SearchResult>> SearchAsync(string query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length < 2)
            return new List<SearchResult>();

        try
        {
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0");

            var url = $"https://query2.finance.yahoo.com/v1/finance/search?q={Uri.EscapeDataString(query)}&quotesCount=8&newsCount=0";
            var response = await httpClient.GetFromJsonAsync<YahooSearchResponse>(url, ct);

            if (response?.Quotes == null)
                return new List<SearchResult>();

            return response.Quotes
                .Where(q => !string.IsNullOrEmpty(q.Symbol))
                .Take(8)
                .Select(q => new SearchResult
                {
                    Symbol = q.Symbol ?? "",
                    ShortName = q.ShortName ?? q.LongName ?? q.Symbol ?? "",
                    LongName = q.LongName,
                    Exchange = q.Exchange,
                    Type = q.QuoteType
                })
                .ToList();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Yahoo Finance search failed for {Query}", LogSanitizer.Sanitize(query));
            return new List<SearchResult>();
        }
    }

    /// <summary>
    /// Get top trending stocks.
    /// </summary>
    public async Task<List<(string Symbol, string Name)>> GetTrendingStocksAsync(
        int count = 10,
        CancellationToken ct = default)
    {
        try
        {
            var yahooClient = new YahooClient();
            var trending = await yahooClient.GetTopTrendingStocksAsync(Country.UnitedStates, count);

            if (trending == null)
                return new List<(string, string)>();

            return trending
                .Select(t => (t, t))
                .Where(t => !string.IsNullOrEmpty(t.Item1))
                .ToList();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Yahoo Finance trending failed");
            return new List<(string, string)>();
        }
    }

    #region Helper Methods

    private static DateTime GetStartDate(string period)
    {
        var now = DateTime.Now;
        return period.ToLower() switch
        {
            "ytd" => new DateTime(now.Year, 1, 1),
            "1d" => now.AddDays(-1),
            "5d" => now.AddDays(-5),
            "1mo" => now.AddMonths(-1),
            "3mo" => now.AddMonths(-3),
            "6mo" => now.AddMonths(-6),
            "1y" => now.AddYears(-1),
            "2y" => now.AddYears(-2),
            "5y" => now.AddYears(-5),
            "10y" => now.AddYears(-10),
            _ => now.AddYears(-1)
        };
    }

    private static string? TryGetString(object obj, string propertyName)
    {
        try
        {
            var prop = obj.GetType().GetProperty(propertyName);
            return prop?.GetValue(obj)?.ToString();
        }
        catch
        {
            return null;
        }
    }

    private static int? TryGetInt(object obj, string propertyName)
    {
        try
        {
            var prop = obj.GetType().GetProperty(propertyName);
            var value = prop?.GetValue(obj);
            if (value == null) return null;
            if (value is int i) return i;
            if (value is long l) return (int)l;
            if (int.TryParse(value.ToString(), out var parsed)) return parsed;
            return null;
        }
        catch
        {
            return null;
        }
    }

    private static decimal? TryGetDecimal(object? value)
    {
        if (value == null) return null;

        var type = value.GetType();
        var rawProp = type.GetProperty("Raw");
        if (rawProp != null)
        {
            var rawValue = rawProp.GetValue(value);
            if (rawValue is double d) return (decimal)d;
            if (rawValue is decimal dec) return dec;
            if (rawValue is float f) return (decimal)f;
        }

        try
        {
            if (value is double d) return (decimal)d;
            if (value is decimal dec) return dec;
            if (value is float f) return (decimal)f;
            if (value is int i) return i;
            if (value is long l) return l;
        }
        catch { }

        return null;
    }

    private static long? TryGetLong(object? value)
    {
        if (value == null) return null;

        var type = value.GetType();
        var rawProp = type.GetProperty("Raw");
        if (rawProp != null)
        {
            var rawValue = rawProp.GetValue(value);
            if (rawValue is long l) return l;
            if (rawValue is double d) return (long)d;
            if (rawValue is int i) return i;
        }

        try
        {
            if (value is long l) return l;
            if (value is int i) return i;
            if (value is double d) return (long)d;
        }
        catch { }

        return null;
    }

    private static decimal? ValidateDividendYield(decimal? yield)
    {
        if (!yield.HasValue) return null;

        // Values > 10% are likely inflated by 100x (yfinance library issue)
        if (yield.Value > 0.10m)
        {
            return yield.Value / 100;
        }

        return yield;
    }

    #endregion

    #region Response Models

    private class YahooSearchResponse
    {
        [JsonPropertyName("quotes")]
        public List<YahooSearchQuote>? Quotes { get; set; }
    }

    private class YahooSearchQuote
    {
        [JsonPropertyName("symbol")]
        public string? Symbol { get; set; }

        [JsonPropertyName("shortname")]
        public string? ShortName { get; set; }

        [JsonPropertyName("longname")]
        public string? LongName { get; set; }

        [JsonPropertyName("exchange")]
        public string? Exchange { get; set; }

        [JsonPropertyName("quoteType")]
        public string? QuoteType { get; set; }
    }

    #endregion
}
