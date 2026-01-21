using System.Net.Http.Json;
using System.Text.Json.Serialization;
using StockAnalyzer.Core.Models;

namespace StockAnalyzer.Core.Services;

/// <summary>
/// Service for fetching stock news from Finnhub API.
/// </summary>
public class NewsService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private const string BaseUrl = "https://finnhub.io/api/v1";

    public NewsService(string apiKey, HttpClient? httpClient = null)
    {
        _apiKey = apiKey;
        _httpClient = httpClient ?? new HttpClient();
    }

    /// <summary>
    /// Get company news for a symbol within a date range.
    /// </summary>
    public async Task<NewsResult> GetCompanyNewsAsync(
        string symbol,
        DateTime? from = null,
        DateTime? to = null)
    {
        var fromDate = from ?? DateTime.Now.AddMonths(-1);
        var toDate = to ?? DateTime.Now;

        var url = $"{BaseUrl}/company-news?symbol={symbol.ToUpper()}" +
                  $"&from={fromDate:yyyy-MM-dd}&to={toDate:yyyy-MM-dd}" +
                  $"&token={_apiKey}";

        try
        {
            var response = await _httpClient.GetFromJsonAsync<List<FinnhubNewsItem>>(url);

            var articles = (response ?? new List<FinnhubNewsItem>())
                .Select(item => new NewsItem
                {
                    Headline = item.Headline ?? "",
                    Summary = item.Summary,
                    Source = item.Source ?? "Unknown",
                    PublishedAt = DateTimeOffset.FromUnixTimeSeconds(item.Datetime).DateTime,
                    Url = item.Url,
                    ImageUrl = item.Image,
                    Category = item.Category,
                    RelatedSymbols = new List<string> { symbol.ToUpper() }
                })
                .OrderByDescending(a => a.PublishedAt)
                .ToList();

            return new NewsResult
            {
                Symbol = symbol.ToUpper(),
                FromDate = fromDate,
                ToDate = toDate,
                Articles = articles
            };
        }
        catch (Exception)
        {
            return new NewsResult
            {
                Symbol = symbol.ToUpper(),
                FromDate = fromDate,
                ToDate = toDate,
                Articles = new List<NewsItem>()
            };
        }
    }

    /// <summary>
    /// Get news for a specific date (for correlating with significant moves).
    /// </summary>
    public async Task<List<NewsItem>> GetNewsForDateAsync(string symbol, DateTime date)
    {
        // Get news from 2 days before to 1 day after to capture related stories
        var result = await GetCompanyNewsAsync(symbol, date.AddDays(-2), date.AddDays(1));
        return result.Articles;
    }

    /// <summary>
    /// Get general market news (not stock-specific).
    /// Uses Finnhub's /news endpoint for broad market coverage.
    /// </summary>
    /// <param name="category">Category: general, forex, crypto, merger</param>
    /// <param name="minId">Minimum news ID for pagination</param>
    public async Task<NewsResult> GetMarketNewsAsync(string category = "general", int? minId = null)
    {
        var validCategories = new[] { "general", "forex", "crypto", "merger" };
        if (!validCategories.Contains(category.ToLower()))
        {
            category = "general";
        }

        var url = $"{BaseUrl}/news?category={category.ToLower()}&token={_apiKey}";
        if (minId.HasValue)
        {
            url += $"&minId={minId}";
        }

        try
        {
            var response = await _httpClient.GetFromJsonAsync<List<FinnhubNewsItem>>(url);

            var articles = (response ?? new List<FinnhubNewsItem>())
                .Select(item => new NewsItem
                {
                    Headline = item.Headline ?? "",
                    Summary = item.Summary,
                    Source = item.Source ?? "Unknown",
                    PublishedAt = DateTimeOffset.FromUnixTimeSeconds(item.Datetime).DateTime,
                    Url = item.Url,
                    ImageUrl = item.Image,
                    Category = item.Category ?? category,
                    RelatedSymbols = !string.IsNullOrEmpty(item.Related)
                        ? item.Related.Split(',').Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToList()
                        : new List<string>()
                })
                .OrderByDescending(a => a.PublishedAt)
                .Take(20)
                .ToList();

            return new NewsResult
            {
                Symbol = "MARKET",
                FromDate = DateTime.Now.AddDays(-7),
                ToDate = DateTime.Now,
                Articles = articles
            };
        }
        catch (Exception)
        {
            return new NewsResult
            {
                Symbol = "MARKET",
                FromDate = DateTime.Now.AddDays(-7),
                ToDate = DateTime.Now,
                Articles = new List<NewsItem>()
            };
        }
    }

    /// <summary>
    /// Get company profile including identifiers (ISIN, CUSIP) from Finnhub.
    /// </summary>
    public async Task<CompanyProfile?> GetCompanyProfileAsync(string symbol)
    {
        var url = $"{BaseUrl}/stock/profile2?symbol={symbol.ToUpper()}&token={_apiKey}";

        try
        {
            var response = await _httpClient.GetFromJsonAsync<FinnhubCompanyProfile>(url);

            if (response == null || string.IsNullOrEmpty(response.Name))
                return null;

            return new CompanyProfile
            {
                Symbol = symbol.ToUpper(),
                Name = response.Name,
                Country = response.Country,
                Currency = response.Currency,
                Exchange = response.Exchange,
                Industry = response.FinnhubIndustry,
                WebUrl = response.WebUrl,
                Logo = response.Logo,
                Isin = response.Isin,
                Cusip = response.Cusip,
                MarketCapitalization = response.MarketCapitalization,
                ShareOutstanding = response.ShareOutstanding,
                IpoDate = response.Ipo
            };
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Look up SEDOL using OpenFIGI API (Bloomberg's free identifier mapping).
    /// SEDOL is primarily available for UK/Irish securities.
    /// </summary>
    public async Task<string?> GetSedolFromIsinAsync(string isin)
    {
        if (string.IsNullOrEmpty(isin))
            return null;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openfigi.com/v3/mapping")
            {
                Content = JsonContent.Create(new[]
                {
                    new { idType = "ID_ISIN", idValue = isin }
                })
            };
            request.Headers.Add("X-OPENFIGI-APIKEY", ""); // Empty string for anonymous access (limited rate)

            using var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
                return null;

            var results = await response.Content.ReadFromJsonAsync<List<OpenFigiResponse>>();
            var data = results?.FirstOrDefault()?.Data?.FirstOrDefault();

            // OpenFIGI returns securityType2 which sometimes contains SEDOL-related info
            // For UK stocks, we can extract SEDOL from the compositeFIGI mapping
            return data?.Sedol;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// OpenFIGI API response models.
    /// </summary>
    private class OpenFigiResponse
    {
        [JsonPropertyName("data")]
        public List<OpenFigiData>? Data { get; set; }
    }

    private class OpenFigiData
    {
        [JsonPropertyName("figi")]
        public string? Figi { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("ticker")]
        public string? Ticker { get; set; }

        [JsonPropertyName("exchCode")]
        public string? ExchCode { get; set; }

        [JsonPropertyName("compositeFIGI")]
        public string? CompositeFigi { get; set; }

        [JsonPropertyName("securityType")]
        public string? SecurityType { get; set; }

        // SEDOL may be included for UK/Irish securities
        [JsonPropertyName("sedol")]
        public string? Sedol { get; set; }
    }

    /// <summary>
    /// Finnhub company profile response model.
    /// </summary>
    private class FinnhubCompanyProfile
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("country")]
        public string? Country { get; set; }

        [JsonPropertyName("currency")]
        public string? Currency { get; set; }

        [JsonPropertyName("exchange")]
        public string? Exchange { get; set; }

        [JsonPropertyName("finnhubIndustry")]
        public string? FinnhubIndustry { get; set; }

        [JsonPropertyName("weburl")]
        public string? WebUrl { get; set; }

        [JsonPropertyName("logo")]
        public string? Logo { get; set; }

        [JsonPropertyName("ipo")]
        public string? Ipo { get; set; }

        [JsonPropertyName("marketCapitalization")]
        public decimal? MarketCapitalization { get; set; }

        [JsonPropertyName("shareOutstanding")]
        public decimal? ShareOutstanding { get; set; }

        [JsonPropertyName("cusip")]
        public string? Cusip { get; set; }

        [JsonPropertyName("isin")]
        public string? Isin { get; set; }
    }

    /// <summary>
    /// Finnhub API response model.
    /// </summary>
    private class FinnhubNewsItem
    {
        [JsonPropertyName("category")]
        public string? Category { get; set; }

        [JsonPropertyName("datetime")]
        public long Datetime { get; set; }

        [JsonPropertyName("headline")]
        public string? Headline { get; set; }

        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("image")]
        public string? Image { get; set; }

        [JsonPropertyName("related")]
        public string? Related { get; set; }

        [JsonPropertyName("source")]
        public string? Source { get; set; }

        [JsonPropertyName("summary")]
        public string? Summary { get; set; }

        [JsonPropertyName("url")]
        public string? Url { get; set; }
    }
}
