using StockAnalyzer.Core.Models;

namespace StockAnalyzer.Core.Services;

/// <summary>
/// Aggregates news from multiple sources (Finnhub, Marketaux) and applies
/// relevance scoring to provide the most relevant headlines.
/// </summary>
public class AggregatedNewsService
{
    private readonly NewsService _finnhubService;
    private readonly MarketauxService _marketauxService;
    private readonly HeadlineRelevanceService _relevanceService;

    public AggregatedNewsService(
        NewsService finnhubService,
        MarketauxService marketauxService,
        HeadlineRelevanceService relevanceService)
    {
        _finnhubService = finnhubService;
        _marketauxService = marketauxService;
        _relevanceService = relevanceService;
    }

    /// <summary>
    /// Get aggregated news for a stock from all available sources.
    /// </summary>
    public async Task<AggregatedNewsResult> GetAggregatedNewsAsync(
        string symbol,
        int days = 7,
        int maxResults = 20)
    {
        var fromDate = DateTime.Now.AddDays(-days);
        var toDate = DateTime.Now;

        // Fetch company profile for better relevance scoring
        var profile = await _finnhubService.GetCompanyProfileAsync(symbol);
        var companyName = profile?.Name;

        // Fetch from all sources in parallel
        var finnhubTask = _finnhubService.GetCompanyNewsAsync(symbol, fromDate, toDate);
        var marketauxTask = _marketauxService.GetNewsAsync(symbol, fromDate, maxResults);

        await Task.WhenAll(finnhubTask, marketauxTask);

        // Combine results
        var allArticles = new List<NewsItem>();

        // Add Finnhub articles with source tag
        var finnhubResult = await finnhubTask;
        allArticles.AddRange(finnhubResult.Articles.Select(a => a with { SourceApi = "finnhub" }));

        // Add Marketaux articles
        var marketauxArticles = await marketauxTask;
        allArticles.AddRange(marketauxArticles);

        // Aggregate, deduplicate, and score
        var aggregated = _relevanceService.AggregateNews(
            allArticles,
            symbol,
            companyName,
            maxResults);

        // Calculate source breakdown
        var sourceBreakdown = aggregated
            .GroupBy(a => a.SourceApi ?? "unknown")
            .ToDictionary(g => g.Key, g => g.Count());

        return new AggregatedNewsResult
        {
            Symbol = symbol.ToUpper(),
            CompanyName = companyName,
            FromDate = fromDate,
            ToDate = toDate,
            Articles = aggregated,
            TotalFetched = allArticles.Count,
            SourceBreakdown = sourceBreakdown,
            AverageRelevanceScore = aggregated.Any()
                ? aggregated.Average(a => a.RelevanceScore ?? 0)
                : 0
        };
    }

    /// <summary>
    /// Get aggregated market news from all sources.
    /// </summary>
    public async Task<AggregatedNewsResult> GetAggregatedMarketNewsAsync(int maxResults = 20)
    {
        // Fetch from all sources in parallel
        var finnhubTask = _finnhubService.GetMarketNewsAsync("general");
        var marketauxTask = _marketauxService.GetMarketNewsAsync(maxResults);

        await Task.WhenAll(finnhubTask, marketauxTask);

        // Combine results
        var allArticles = new List<NewsItem>();

        var finnhubResult = await finnhubTask;
        allArticles.AddRange(finnhubResult.Articles.Select(a => a with { SourceApi = "finnhub" }));

        var marketauxArticles = await marketauxTask;
        allArticles.AddRange(marketauxArticles);

        // For market news, we score based on general relevance factors
        // (recency, source quality, having sentiment data)
        var scored = allArticles
            .Select(a => a with
            {
                RelevanceScore = ScoreMarketNews(a)
            })
            .ToList();

        // Deduplicate and sort
        var deduplicated = DeduplicateByUrl(scored)
            .OrderByDescending(a => a.RelevanceScore)
            .ThenByDescending(a => a.PublishedAt)
            .Take(maxResults)
            .ToList();

        var sourceBreakdown = deduplicated
            .GroupBy(a => a.SourceApi ?? "unknown")
            .ToDictionary(g => g.Key, g => g.Count());

        return new AggregatedNewsResult
        {
            Symbol = "MARKET",
            CompanyName = null,
            FromDate = DateTime.Now.AddDays(-7),
            ToDate = DateTime.Now,
            Articles = deduplicated,
            TotalFetched = allArticles.Count,
            SourceBreakdown = sourceBreakdown,
            AverageRelevanceScore = deduplicated.Any()
                ? deduplicated.Average(a => a.RelevanceScore ?? 0)
                : 0
        };
    }

    /// <summary>
    /// Score market news (non-symbol-specific).
    /// </summary>
    private decimal ScoreMarketNews(NewsItem article)
    {
        decimal score = 0m;

        // Recency (40% weight for market news)
        var hoursAgo = (DateTime.Now - article.PublishedAt).TotalHours;
        var recencyScore = (decimal)Math.Max(0.1, Math.Min(1.0, Math.Exp(-hoursAgo / 12.0)));
        score += recencyScore * 0.4m;

        // Source quality (30% weight)
        var premiumSources = new[] { "Reuters", "Bloomberg", "CNBC", "WSJ", "Financial Times" };
        var sourceScore = premiumSources.Any(s =>
            article.Source?.Contains(s, StringComparison.OrdinalIgnoreCase) == true) ? 1.0m : 0.5m;
        score += sourceScore * 0.3m;

        // Has sentiment data (15% weight)
        score += (article.SentimentScore.HasValue ? 0.8m : 0.3m) * 0.15m;

        // Has image (15% weight - indicates more complete coverage)
        score += (!string.IsNullOrEmpty(article.ImageUrl) ? 0.8m : 0.4m) * 0.15m;

        return Math.Round(score, 3);
    }

    /// <summary>
    /// Deduplicate articles by URL.
    /// </summary>
    private List<NewsItem> DeduplicateByUrl(List<NewsItem> articles)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unique = new List<NewsItem>();

        foreach (var article in articles)
        {
            var key = article.Url ?? article.Headline;
            if (!string.IsNullOrEmpty(key) && seen.Add(key))
            {
                unique.Add(article);
            }
        }

        return unique;
    }
}

/// <summary>
/// Result from aggregated news query.
/// </summary>
public record AggregatedNewsResult
{
    public required string Symbol { get; init; }
    public string? CompanyName { get; init; }
    public required DateTime FromDate { get; init; }
    public required DateTime ToDate { get; init; }
    public required List<NewsItem> Articles { get; init; }
    public int TotalFetched { get; init; }
    public Dictionary<string, int> SourceBreakdown { get; init; } = new();
    public decimal AverageRelevanceScore { get; init; }
    public int TotalCount => Articles.Count;
}
