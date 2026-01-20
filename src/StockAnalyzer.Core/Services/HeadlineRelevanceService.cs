using StockAnalyzer.Core.Models;

namespace StockAnalyzer.Core.Services;

/// <summary>
/// Service for scoring headline relevance and aggregating news from multiple sources.
/// Uses keyword matching, recency weighting, and deduplication.
/// </summary>
public class HeadlineRelevanceService
{
    // Weight factors for relevance scoring
    private const decimal TickerMentionWeight = 0.35m;
    private const decimal CompanyNameWeight = 0.25m;
    private const decimal RecencyWeight = 0.20m;
    private const decimal SentimentWeight = 0.10m;
    private const decimal SourceQualityWeight = 0.10m;

    // High-quality financial news sources
    private static readonly HashSet<string> PremiumSources = new(StringComparer.OrdinalIgnoreCase)
    {
        "Reuters", "Bloomberg", "CNBC", "Wall Street Journal", "WSJ",
        "Financial Times", "MarketWatch", "Barron's", "Investor's Business Daily",
        "Yahoo Finance", "The Motley Fool", "Seeking Alpha"
    };

    // Medium-quality sources
    private static readonly HashSet<string> StandardSources = new(StringComparer.OrdinalIgnoreCase)
    {
        "AP", "Associated Press", "Business Insider", "Fortune", "Forbes",
        "The Street", "Benzinga", "Zacks", "InvestorPlace"
    };

    /// <summary>
    /// Score a single news item for relevance to a given symbol.
    /// </summary>
    public decimal ScoreRelevance(NewsItem article, string symbol, string? companyName = null)
    {
        decimal score = 0m;

        // 1. Ticker mention in headline/summary (highest weight)
        var tickerScore = CalculateTickerScore(article, symbol);
        score += tickerScore * TickerMentionWeight;

        // 2. Company name mention (if available)
        if (!string.IsNullOrEmpty(companyName))
        {
            var nameScore = CalculateCompanyNameScore(article, companyName);
            score += nameScore * CompanyNameWeight;
        }
        else
        {
            // Redistribute company name weight to ticker if no name provided
            score += tickerScore * CompanyNameWeight * 0.5m;
        }

        // 3. Recency (more recent = higher score)
        var recencyScore = CalculateRecencyScore(article.PublishedAt);
        score += recencyScore * RecencyWeight;

        // 4. Sentiment (having sentiment data indicates better coverage)
        var sentimentScore = article.SentimentScore.HasValue ? 0.8m : 0.3m;
        score += sentimentScore * SentimentWeight;

        // 5. Source quality
        var sourceScore = CalculateSourceQualityScore(article.Source);
        score += sourceScore * SourceQualityWeight;

        return Math.Round(Math.Min(1.0m, Math.Max(0m, score)), 3);
    }

    /// <summary>
    /// Aggregate and deduplicate news from multiple sources, scoring each article.
    /// </summary>
    public List<NewsItem> AggregateNews(
        IEnumerable<NewsItem> articles,
        string symbol,
        string? companyName = null,
        int maxResults = 20)
    {
        var scoredArticles = articles
            .Select(a => a with { RelevanceScore = ScoreRelevance(a, symbol, companyName) })
            .ToList();

        // Deduplicate by headline similarity
        var deduplicated = DeduplicateByHeadline(scoredArticles);

        // Sort by relevance score descending, then by date
        return deduplicated
            .OrderByDescending(a => a.RelevanceScore)
            .ThenByDescending(a => a.PublishedAt)
            .Take(maxResults)
            .ToList();
    }

    /// <summary>
    /// Remove duplicate articles based on headline similarity.
    /// </summary>
    private List<NewsItem> DeduplicateByHeadline(List<NewsItem> articles)
    {
        var unique = new List<NewsItem>();
        var seenHeadlines = new List<string>();

        foreach (var article in articles.OrderByDescending(a => a.RelevanceScore))
        {
            var normalizedHeadline = NormalizeHeadline(article.Headline);

            // Check if similar headline already exists
            var isDuplicate = seenHeadlines.Any(seen =>
                CalculateSimilarity(seen, normalizedHeadline) > 0.7);

            if (!isDuplicate)
            {
                unique.Add(article);
                seenHeadlines.Add(normalizedHeadline);
            }
        }

        return unique;
    }

    /// <summary>
    /// Calculate ticker mention score.
    /// </summary>
    private decimal CalculateTickerScore(NewsItem article, string symbol)
    {
        var upperSymbol = symbol.ToUpper();
        var headline = article.Headline?.ToUpper() ?? "";
        var summary = article.Summary?.ToUpper() ?? "";

        // Check related symbols first
        if (article.RelatedSymbols?.Any(s => s.Equals(upperSymbol, StringComparison.OrdinalIgnoreCase)) == true)
        {
            return 1.0m;
        }

        // Exact ticker in headline (with word boundaries)
        if (ContainsTickerWithBoundary(headline, upperSymbol))
        {
            return 0.95m;
        }

        // Ticker in summary
        if (ContainsTickerWithBoundary(summary, upperSymbol))
        {
            return 0.7m;
        }

        return 0.1m;
    }

    /// <summary>
    /// Check if text contains ticker as a word (not part of another word).
    /// </summary>
    private bool ContainsTickerWithBoundary(string text, string ticker)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(ticker))
            return false;

        // Simple word boundary check using common delimiters
        var delimiters = new[] { ' ', ',', '.', ':', ';', '(', ')', '[', ']', '-', '/', '\'', '"' };
        var words = text.Split(delimiters, StringSplitOptions.RemoveEmptyEntries);

        return words.Any(w => w.Equals(ticker, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Calculate company name mention score.
    /// </summary>
    private decimal CalculateCompanyNameScore(NewsItem article, string companyName)
    {
        var lowerName = companyName.ToLower();
        var headline = article.Headline?.ToLower() ?? "";
        var summary = article.Summary?.ToLower() ?? "";

        // Check for full company name or significant parts
        var nameParts = lowerName.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => p.Length > 3 && !IsCommonWord(p))
            .ToList();

        // Full name in headline
        if (headline.Contains(lowerName))
        {
            return 1.0m;
        }

        // Significant part in headline
        var partsInHeadline = nameParts.Count(p => headline.Contains(p));
        if (partsInHeadline > 0)
        {
            return 0.5m + (0.3m * partsInHeadline / Math.Max(1, nameParts.Count));
        }

        // Full name or parts in summary
        if (summary.Contains(lowerName))
        {
            return 0.6m;
        }

        var partsInSummary = nameParts.Count(p => summary.Contains(p));
        if (partsInSummary > 0)
        {
            return 0.3m + (0.2m * partsInSummary / Math.Max(1, nameParts.Count));
        }

        return 0.1m;
    }

    /// <summary>
    /// Calculate recency score (exponential decay over time).
    /// </summary>
    private decimal CalculateRecencyScore(DateTime publishedAt)
    {
        var hoursAgo = (DateTime.Now - publishedAt).TotalHours;

        // Exponential decay: score = e^(-hoursAgo / halfLife)
        // Half life of 24 hours means 50% score after 1 day
        const double halfLife = 24.0;
        var decay = Math.Exp(-hoursAgo / halfLife);

        return (decimal)Math.Max(0.1, Math.Min(1.0, decay));
    }

    /// <summary>
    /// Calculate source quality score.
    /// </summary>
    private decimal CalculateSourceQualityScore(string source)
    {
        if (string.IsNullOrEmpty(source))
            return 0.3m;

        if (PremiumSources.Contains(source))
            return 1.0m;

        if (StandardSources.Contains(source))
            return 0.7m;

        // Check partial matches for premium sources
        if (PremiumSources.Any(p => source.Contains(p, StringComparison.OrdinalIgnoreCase)))
            return 0.9m;

        return 0.4m;
    }

    /// <summary>
    /// Normalize headline for comparison.
    /// </summary>
    private string NormalizeHeadline(string headline)
    {
        if (string.IsNullOrEmpty(headline))
            return "";

        // Convert to lowercase, remove punctuation, normalize whitespace
        var normalized = headline.ToLower();
        var chars = normalized.Where(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c)).ToArray();
        return string.Join(" ", new string(chars).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>
    /// Calculate similarity between two normalized headlines (Jaccard similarity).
    /// </summary>
    private double CalculateSimilarity(string headline1, string headline2)
    {
        var words1 = headline1.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var words2 = headline2.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();

        if (words1.Count == 0 || words2.Count == 0)
            return 0;

        var intersection = words1.Intersect(words2).Count();
        var union = words1.Union(words2).Count();

        return union > 0 ? (double)intersection / union : 0;
    }

    /// <summary>
    /// Check if a word is too common to be meaningful.
    /// </summary>
    private bool IsCommonWord(string word)
    {
        var common = new HashSet<string>
        {
            "the", "inc", "corp", "corporation", "company", "companies",
            "ltd", "limited", "llc", "group", "holdings", "international"
        };
        return common.Contains(word.ToLower());
    }
}
