using System.Text.RegularExpressions;

namespace StockAnalyzer.Core.Services;

/// <summary>
/// Ensemble sentiment analyzer for news headlines.
/// Combines keyword-based analysis with VADER for improved accuracy.
/// </summary>
public static class SentimentAnalyzer
{
    // VADER service instance (thread-safe)
    private static readonly VaderSentimentService _vader = new();

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
        "dip", "dips", "dipping", "dipped",
        "slump", "slumps", "slumping", "slumped",
        "sag", "sags", "sagging",
        "weaken", "weakens", "weakening", "weakened",
        "soften", "softens", "softening",
        "worsen", "worsens", "worsening",
        "stall", "stalls", "stalling",

        // Fundamental negative
        "miss", "misses", "missing", "disappoint", "disappoints", "disappointing",
        "downgrade", "downgraded", "downgrades", "bearish", "pessimistic", "weak",
        "warning", "warns", "concern", "concerns", "worried", "fear", "fears",
        "sell rating", "underweight", "underperform", "cut", "cuts", "layoff", "layoffs",
        "earnings miss", "revenue miss", "negative", "lawsuit", "investigation", "fraud"
    };

    /// <summary>
    /// Check if text contains keyword as a whole word (not substring).
    /// Multi-word phrases use simple Contains for flexibility.
    /// </summary>
    private static bool ContainsWord(string text, string keyword)
    {
        // For multi-word phrases, use simple Contains
        if (keyword.Contains(' '))
            return text.Contains(keyword, StringComparison.OrdinalIgnoreCase);

        // For single words, use word boundary matching to prevent false positives
        // e.g., "gains" should not match "regains"
        var pattern = $@"\b{Regex.Escape(keyword)}\b";
        return Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase);
    }

    /// <summary>
    /// Analyze sentiment of a headline using ensemble approach.
    /// Combines keyword-based analysis (30%) with VADER (70%) for improved accuracy.
    /// </summary>
    /// <param name="headline">The news headline text</param>
    /// <returns>Tuple of (sentiment, score) where score ranges from -1.0 (very negative) to +1.0 (very positive)</returns>
    public static (Sentiment sentiment, decimal score) Analyze(string? headline)
    {
        if (string.IsNullOrWhiteSpace(headline))
            return (Sentiment.Neutral, 0m);

        // Get keyword-based score (financial-specific vocabulary)
        var keywordScore = AnalyzeKeywords(headline);

        // Get VADER score (general sentiment, handles modifiers/negations)
        var vaderResult = _vader.Analyze(headline);
        var vaderScore = (decimal)vaderResult.Compound;

        // Ensemble: 60% keyword (financial-specific), 40% VADER (general modifiers)
        // We weight keywords more heavily because they're tuned for financial news,
        // while VADER provides modulation for negations and intensifiers
        var combinedScore = (keywordScore * 0.6m) + (vaderScore * 0.4m);

        // Classify based on combined score
        // Using slightly tighter thresholds than before since VADER is more nuanced
        Sentiment sentiment;
        if (combinedScore > 0.05m)
            sentiment = Sentiment.Positive;
        else if (combinedScore < -0.05m)
            sentiment = Sentiment.Negative;
        else
            sentiment = Sentiment.Neutral;

        return (sentiment, combinedScore);
    }

    /// <summary>
    /// Analyze sentiment using keywords only (internal method for ensemble).
    /// </summary>
    private static decimal AnalyzeKeywords(string headline)
    {
        var text = headline.ToLowerInvariant();
        int positiveCount = 0;
        int negativeCount = 0;

        // Check for keyword matches with word boundaries
        foreach (var keyword in PositiveKeywords)
        {
            if (ContainsWord(text, keyword))
                positiveCount++;
        }

        foreach (var keyword in NegativeKeywords)
        {
            if (ContainsWord(text, keyword))
                negativeCount++;
        }

        // Calculate score: positive keywords add, negative keywords subtract
        // Normalize to -1.0 to +1.0 range
        int total = positiveCount + negativeCount;
        if (total == 0)
            return 0m;

        return (decimal)(positiveCount - negativeCount) / Math.Max(total, 1);
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
