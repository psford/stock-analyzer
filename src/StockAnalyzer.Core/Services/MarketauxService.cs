using System.Net.Http.Json;
using System.Text.Json.Serialization;
using StockAnalyzer.Core.Models;

namespace StockAnalyzer.Core.Services;

/// <summary>
/// Service for fetching stock news from Marketaux API.
/// Provides alternative news source to complement Finnhub.
/// Free tier: 100 requests/day, 3 articles/request.
/// </summary>
public class MarketauxService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiToken;
    private const string BaseUrl = "https://api.marketaux.com/v1";

    public MarketauxService(string apiToken, HttpClient? httpClient = null)
    {
        _apiToken = apiToken;
        _httpClient = httpClient ?? new HttpClient();
    }

    /// <summary>
    /// Get news for a specific stock symbol.
    /// </summary>
    public async Task<List<NewsItem>> GetNewsAsync(
        string symbol,
        DateTime? publishedAfter = null,
        int limit = 10)
    {
        if (string.IsNullOrEmpty(_apiToken))
        {
            return new List<NewsItem>();
        }

        var fromDate = publishedAfter ?? DateTime.Now.AddDays(-7);
        var url = $"{BaseUrl}/news/all?" +
                  $"symbols={symbol.ToUpper()}" +
                  $"&filter_entities=true" +
                  $"&published_after={fromDate:yyyy-MM-ddTHH:mm:ss}" +
                  $"&language=en" +
                  $"&limit={Math.Min(limit, 50)}" +
                  $"&api_token={_apiToken}";

        try
        {
            var response = await _httpClient.GetFromJsonAsync<MarketauxResponse>(url);

            if (response?.Data == null)
                return new List<NewsItem>();

            return response.Data
                .Select(item => new NewsItem
                {
                    Headline = item.Title ?? "",
                    Summary = item.Description,
                    Source = item.Source ?? "Unknown",
                    PublishedAt = DateTime.TryParse(item.PublishedAt, out var dt) ? dt : DateTime.Now,
                    Url = item.Url,
                    ImageUrl = item.ImageUrl,
                    Category = item.Entities?.FirstOrDefault()?.Industry,
                    RelatedSymbols = item.Entities?
                        .Where(e => !string.IsNullOrEmpty(e.Symbol))
                        .Select(e => e.Symbol!)
                        .Distinct()
                        .ToList() ?? new List<string> { symbol.ToUpper() },
                    Sentiment = MapSentiment(item.Entities?.FirstOrDefault()?.SentimentScore),
                    SentimentScore = item.Entities?.FirstOrDefault()?.SentimentScore,
                    SourceApi = "marketaux"
                })
                .ToList();
        }
        catch (Exception)
        {
            return new List<NewsItem>();
        }
    }

    /// <summary>
    /// Get general market news (not symbol-specific).
    /// </summary>
    public async Task<List<NewsItem>> GetMarketNewsAsync(int limit = 10)
    {
        if (string.IsNullOrEmpty(_apiToken))
        {
            return new List<NewsItem>();
        }

        var url = $"{BaseUrl}/news/all?" +
                  $"countries=us" +
                  $"&language=en" +
                  $"&limit={Math.Min(limit, 50)}" +
                  $"&api_token={_apiToken}";

        try
        {
            var response = await _httpClient.GetFromJsonAsync<MarketauxResponse>(url);

            if (response?.Data == null)
                return new List<NewsItem>();

            return response.Data
                .Select(item => new NewsItem
                {
                    Headline = item.Title ?? "",
                    Summary = item.Description,
                    Source = item.Source ?? "Unknown",
                    PublishedAt = DateTime.TryParse(item.PublishedAt, out var dt) ? dt : DateTime.Now,
                    Url = item.Url,
                    ImageUrl = item.ImageUrl,
                    Category = "general",
                    RelatedSymbols = item.Entities?
                        .Where(e => !string.IsNullOrEmpty(e.Symbol))
                        .Select(e => e.Symbol!)
                        .Distinct()
                        .ToList() ?? new List<string>(),
                    Sentiment = MapSentiment(item.Entities?.FirstOrDefault()?.SentimentScore),
                    SentimentScore = item.Entities?.FirstOrDefault()?.SentimentScore,
                    SourceApi = "marketaux"
                })
                .ToList();
        }
        catch (Exception)
        {
            return new List<NewsItem>();
        }
    }

    private static string? MapSentiment(decimal? score)
    {
        if (!score.HasValue) return null;
        return score.Value switch
        {
            > 0.3m => "positive",
            < -0.3m => "negative",
            _ => "neutral"
        };
    }

    // Response models for Marketaux API
    private class MarketauxResponse
    {
        [JsonPropertyName("data")]
        public List<MarketauxArticle>? Data { get; set; }

        [JsonPropertyName("meta")]
        public MarketauxMeta? Meta { get; set; }
    }

    private class MarketauxMeta
    {
        [JsonPropertyName("found")]
        public int Found { get; set; }

        [JsonPropertyName("returned")]
        public int Returned { get; set; }

        [JsonPropertyName("limit")]
        public int Limit { get; set; }

        [JsonPropertyName("page")]
        public int Page { get; set; }
    }

    private class MarketauxArticle
    {
        [JsonPropertyName("uuid")]
        public string? Uuid { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("keywords")]
        public string? Keywords { get; set; }

        [JsonPropertyName("snippet")]
        public string? Snippet { get; set; }

        [JsonPropertyName("url")]
        public string? Url { get; set; }

        [JsonPropertyName("image_url")]
        public string? ImageUrl { get; set; }

        [JsonPropertyName("language")]
        public string? Language { get; set; }

        [JsonPropertyName("published_at")]
        public string? PublishedAt { get; set; }

        [JsonPropertyName("source")]
        public string? Source { get; set; }

        [JsonPropertyName("relevance_score")]
        public decimal? RelevanceScore { get; set; }

        [JsonPropertyName("entities")]
        public List<MarketauxEntity>? Entities { get; set; }
    }

    private class MarketauxEntity
    {
        [JsonPropertyName("symbol")]
        public string? Symbol { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("exchange")]
        public string? Exchange { get; set; }

        [JsonPropertyName("exchange_long")]
        public string? ExchangeLong { get; set; }

        [JsonPropertyName("country")]
        public string? Country { get; set; }

        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("industry")]
        public string? Industry { get; set; }

        [JsonPropertyName("match_score")]
        public decimal? MatchScore { get; set; }

        [JsonPropertyName("sentiment_score")]
        public decimal? SentimentScore { get; set; }

        [JsonPropertyName("highlights")]
        public List<MarketauxHighlight>? Highlights { get; set; }
    }

    private class MarketauxHighlight
    {
        [JsonPropertyName("highlight")]
        public string? Highlight { get; set; }

        [JsonPropertyName("sentiment")]
        public decimal? Sentiment { get; set; }

        [JsonPropertyName("highlighted_in")]
        public string? HighlightedIn { get; set; }
    }
}
