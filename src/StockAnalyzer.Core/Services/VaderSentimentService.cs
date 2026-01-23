using VaderSharp2;

namespace StockAnalyzer.Core.Services;

/// <summary>
/// VADER sentiment analysis wrapper for financial headlines.
/// VADER (Valence Aware Dictionary and sEntiment Reasoner) is optimized for
/// social media and short-form text like headlines.
///
/// VADER handles:
/// - Modifiers ("not good" → negative)
/// - Intensifiers ("very good" → more positive)
/// - Punctuation emphasis ("great!!!" → stronger)
/// - Emoji and emoticons
/// </summary>
public class VaderSentimentService
{
    private readonly SentimentIntensityAnalyzer _analyzer;

    public VaderSentimentService()
    {
        _analyzer = new SentimentIntensityAnalyzer();
    }

    /// <summary>
    /// VADER sentiment analysis result.
    /// </summary>
    /// <param name="Positive">Proportion of text that is positive (0-1)</param>
    /// <param name="Negative">Proportion of text that is negative (0-1)</param>
    /// <param name="Neutral">Proportion of text that is neutral (0-1)</param>
    /// <param name="Compound">Normalized composite score from -1 (most negative) to +1 (most positive)</param>
    public record VaderResult(
        double Positive,
        double Negative,
        double Neutral,
        double Compound
    );

    /// <summary>
    /// Analyze sentiment of text using VADER.
    /// </summary>
    /// <param name="text">Text to analyze</param>
    /// <returns>VADER sentiment scores</returns>
    public VaderResult Analyze(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new VaderResult(0, 0, 1, 0);

        var scores = _analyzer.PolarityScores(text);
        return new VaderResult(
            scores.Positive,
            scores.Negative,
            scores.Neutral,
            scores.Compound
        );
    }

    /// <summary>
    /// Get simplified sentiment classification based on VADER compound score.
    /// Uses VADER's recommended thresholds.
    /// </summary>
    /// <param name="text">Text to analyze</param>
    /// <returns>Positive, Negative, or Neutral classification</returns>
    public string GetSentimentLabel(string? text)
    {
        var result = Analyze(text);

        // VADER recommended thresholds
        if (result.Compound >= 0.05)
            return "positive";
        if (result.Compound <= -0.05)
            return "negative";
        return "neutral";
    }
}
