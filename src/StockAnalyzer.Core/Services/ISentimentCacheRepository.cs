namespace StockAnalyzer.Core.Services;

/// <summary>
/// Repository interface for caching FinBERT sentiment analysis results.
/// Enables pre-computation and persistence of expensive ML inference.
/// </summary>
public interface ISentimentCacheRepository
{
    /// <summary>
    /// Get headlines that are queued for analysis but not yet processed.
    /// </summary>
    /// <param name="count">Maximum number of headlines to return</param>
    /// <returns>List of headline texts pending analysis</returns>
    Task<List<string>> GetPendingHeadlinesAsync(int count);

    /// <summary>
    /// Queue a headline for background FinBERT analysis.
    /// </summary>
    /// <param name="headline">Headline text to queue</param>
    Task QueueHeadlineAsync(string headline);

    /// <summary>
    /// Store the FinBERT analysis result for a headline.
    /// </summary>
    /// <param name="headline">Headline that was analyzed</param>
    /// <param name="result">FinBERT analysis result</param>
    Task CacheResultAsync(string headline, FinBertSentimentService.FinBertResult result);

    /// <summary>
    /// Get cached FinBERT result for a headline, if available.
    /// </summary>
    /// <param name="headline">Headline to look up</param>
    /// <returns>Cached result, or null if not yet analyzed</returns>
    Task<FinBertSentimentService.FinBertResult?> GetCachedResultAsync(string headline);

    /// <summary>
    /// Check if a headline has already been analyzed.
    /// </summary>
    /// <param name="headline">Headline to check</param>
    /// <returns>True if result is cached</returns>
    Task<bool> HasCachedResultAsync(string headline);

    /// <summary>
    /// Remove old entries to prevent unbounded cache growth.
    /// </summary>
    /// <param name="maxAgeDays">Maximum age in days for cached entries</param>
    Task PruneOldEntriesAsync(int maxAgeDays);

    /// <summary>
    /// Get cache statistics.
    /// </summary>
    /// <returns>Tuple of (total cached, pending analysis)</returns>
    Task<(int cached, int pending)> GetStatisticsAsync();
}
