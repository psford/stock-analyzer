namespace StockAnalyzer.Core.Services;

/// <summary>
/// Keyword-based sentiment analyzer for news headlines.
/// Detects positive, negative, or neutral sentiment based on financial news vocabulary.
/// </summary>
public static class SentimentAnalyzer
{
    /// <summary>
    /// Sentiment classification result.
    /// </summary>
    public enum Sentiment
    {
        Positive,
        Negative,
        Neutral
    }

    // Positive sentiment indicators (bullish language)
    private static readonly HashSet<string> PositiveKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        // Strong positive
        "soars", "soaring", "surge", "surges", "surging", "skyrocket", "skyrockets",
        "rally", "rallies", "rallying", "jump", "jumps", "jumping", "spike", "spikes",
        "boom", "booms", "booming", "explode", "explodes", "record high", "all-time high",

        // Moderate positive
        "rise", "rises", "rising", "gain", "gains", "gaining", "climb", "climbs", "climbing",
        "advance", "advances", "advancing", "up", "higher", "increase", "increases",

        // Fundamental positive
        "beat", "beats", "beating", "exceed", "exceeds", "exceeding", "outperform",
        "upgrade", "upgraded", "upgrades", "bullish", "optimistic", "strong", "growth",
        "profit", "profitable", "earnings beat", "revenue beat", "positive",
        "buy rating", "overweight", "outperform rating"
    };

    // Negative sentiment indicators (bearish language)
    private static readonly HashSet<string> NegativeKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        // Strong negative
        "plunge", "plunges", "plunging", "crash", "crashes", "crashing", "collapse",
        "tumble", "tumbles", "tumbling", "tank", "tanks", "tanking", "plummet", "plummets",
        "sink", "sinks", "sinking", "dive", "dives", "diving", "freefall",

        // Moderate negative
        "fall", "falls", "falling", "drop", "drops", "dropping", "decline", "declines",
        "slide", "slides", "sliding", "slip", "slips", "slipping", "down", "lower",
        "decrease", "decreases", "loss", "loses", "losing", "retreat", "retreats",

        // Fundamental negative
        "miss", "misses", "missing", "disappoint", "disappoints", "disappointing",
        "downgrade", "downgraded", "downgrades", "bearish", "pessimistic", "weak",
        "warning", "warns", "concern", "concerns", "worried", "fear", "fears",
        "sell rating", "underweight", "underperform", "cut", "cuts", "layoff", "layoffs",
        "earnings miss", "revenue miss", "negative", "lawsuit", "investigation", "fraud"
    };

    /// <summary>
    /// Analyze sentiment of a headline.
    /// </summary>
    /// <param name="headline">The news headline text</param>
    /// <returns>Tuple of (sentiment, score) where score ranges from -1.0 (very negative) to +1.0 (very positive)</returns>
    public static (Sentiment sentiment, decimal score) Analyze(string? headline)
    {
        if (string.IsNullOrWhiteSpace(headline))
            return (Sentiment.Neutral, 0m);

        var text = headline.ToLowerInvariant();
        int positiveCount = 0;
        int negativeCount = 0;

        // Check for keyword matches
        foreach (var keyword in PositiveKeywords)
        {
            if (text.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                positiveCount++;
        }

        foreach (var keyword in NegativeKeywords)
        {
            if (text.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                negativeCount++;
        }

        // Calculate score: positive keywords add, negative keywords subtract
        // Normalize to -1.0 to +1.0 range
        int total = positiveCount + negativeCount;
        if (total == 0)
            return (Sentiment.Neutral, 0m);

        decimal score = (decimal)(positiveCount - negativeCount) / Math.Max(total, 1);

        // Classify based on score
        Sentiment sentiment;
        if (score > 0.1m)
            sentiment = Sentiment.Positive;
        else if (score < -0.1m)
            sentiment = Sentiment.Negative;
        else
            sentiment = Sentiment.Neutral;

        return (sentiment, score);
    }

    /// <summary>
    /// Check if headline sentiment matches the price movement direction.
    /// </summary>
    /// <param name="headline">The news headline text</param>
    /// <param name="priceChangePercent">The price change percentage (positive = up, negative = down)</param>
    /// <returns>True if sentiment aligns with price direction or is neutral</returns>
    public static bool MatchesPriceDirection(string? headline, decimal priceChangePercent)
    {
        var (sentiment, _) = Analyze(headline);

        // Neutral headlines are always acceptable
        if (sentiment == Sentiment.Neutral)
            return true;

        // For significant moves (±3% or more), require sentiment match
        if (Math.Abs(priceChangePercent) >= 3.0m)
        {
            // Price went up - need positive or neutral sentiment
            if (priceChangePercent > 0 && sentiment == Sentiment.Negative)
                return false;

            // Price went down - need negative or neutral sentiment
            if (priceChangePercent < 0 && sentiment == Sentiment.Positive)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Score how well a headline matches the price movement.
    /// Higher score = better match. Used for ranking headlines.
    /// </summary>
    /// <param name="headline">The news headline text</param>
    /// <param name="priceChangePercent">The price change percentage</param>
    /// <returns>Match score from 0 (mismatch) to 100 (perfect match)</returns>
    public static int CalculateMatchScore(string? headline, decimal priceChangePercent)
    {
        var (sentiment, sentimentScore) = Analyze(headline);

        // Base score for having a headline at all
        int score = 50;

        // Neutral sentiment: acceptable but not ideal
        if (sentiment == Sentiment.Neutral)
            return score;

        bool priceUp = priceChangePercent > 0;
        bool sentimentPositive = sentiment == Sentiment.Positive;

        // Perfect match: sentiment aligns with price direction
        if ((priceUp && sentimentPositive) || (!priceUp && !sentimentPositive))
        {
            // Bonus based on sentiment strength
            score += (int)(Math.Abs(sentimentScore) * 50);
            return Math.Min(score, 100);
        }

        // Mismatch: sentiment contradicts price direction
        // More severe penalty for stronger sentiment
        score -= (int)(Math.Abs(sentimentScore) * 50);
        return Math.Max(score, 0);
    }
}
