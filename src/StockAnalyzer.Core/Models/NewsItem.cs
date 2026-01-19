namespace StockAnalyzer.Core.Models;

/// <summary>
/// News article related to a stock.
/// </summary>
public record NewsItem
{
    public required string Headline { get; init; }
    public string? Summary { get; init; }
    public required string Source { get; init; }
    public required DateTime PublishedAt { get; init; }
    public string? Url { get; init; }
    public string? ImageUrl { get; init; }
    public string? Category { get; init; }
    public List<string>? RelatedSymbols { get; init; }

    // Sentiment (if available from API)
    public string? Sentiment { get; init; }
    public decimal? SentimentScore { get; init; }

    // Relevance scoring for aggregation
    public decimal? RelevanceScore { get; init; }
    public string? SourceApi { get; init; }  // "finnhub", "marketaux", etc.
}

/// <summary>
/// News search result with metadata.
/// </summary>
public record NewsResult
{
    public required string Symbol { get; init; }
    public required DateTime FromDate { get; init; }
    public required DateTime ToDate { get; init; }
    public required List<NewsItem> Articles { get; init; }
    public int TotalCount => Articles.Count;
}
