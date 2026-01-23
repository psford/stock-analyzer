namespace StockAnalyzer.Core.Data.Entities;

/// <summary>
/// EF Core entity for cached FinBERT sentiment analysis results.
/// Stores pre-computed ML sentiment to avoid expensive inference on every request.
/// </summary>
public class CachedSentimentEntity
{
    /// <summary>Auto-incrementing primary key.</summary>
    public int Id { get; set; }

    /// <summary>SHA256 hash of the headline for fast lookup.</summary>
    public string HeadlineHash { get; set; } = string.Empty;

    /// <summary>Original headline text.</summary>
    public string Headline { get; set; } = string.Empty;

    /// <summary>Predicted sentiment: "positive", "negative", or "neutral".</summary>
    public string Sentiment { get; set; } = string.Empty;

    /// <summary>Confidence score for the predicted sentiment (0-1).</summary>
    public decimal Confidence { get; set; }

    /// <summary>Probability of positive sentiment (0-1).</summary>
    public decimal PositiveProb { get; set; }

    /// <summary>Probability of negative sentiment (0-1).</summary>
    public decimal NegativeProb { get; set; }

    /// <summary>Probability of neutral sentiment (0-1).</summary>
    public decimal NeutralProb { get; set; }

    /// <summary>Version of the analyzer used (for cache invalidation on model updates).</summary>
    public string AnalyzerVersion { get; set; } = "finbert-v1";

    /// <summary>When this result was computed.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>True if queued for analysis but not yet processed.</summary>
    public bool IsPending { get; set; }
}
