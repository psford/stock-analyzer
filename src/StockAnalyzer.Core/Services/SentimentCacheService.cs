using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace StockAnalyzer.Core.Services;

/// <summary>
/// Background service that pre-computes and caches FinBERT sentiment analysis.
/// Uses database persistence via ISentimentCacheRepository.
///
/// This service runs continuously, processing queued headlines in the background
/// to ensure FinBERT results are available without blocking API requests.
/// </summary>
public class SentimentCacheService : BackgroundService
{
    private readonly FinBertSentimentService? _finbert;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SentimentCacheService> _logger;

    private readonly TimeSpan _processDelay = TimeSpan.FromMilliseconds(100);
    private readonly TimeSpan _idleDelay = TimeSpan.FromSeconds(5);
    private readonly int _batchSize = 10;

    /// <summary>
    /// Creates a new sentiment cache service.
    /// </summary>
    /// <param name="finbert">Optional FinBERT service. If null, the service runs in pass-through mode.</param>
    /// <param name="scopeFactory">Service scope factory for repository access</param>
    /// <param name="logger">Logger instance</param>
    public SentimentCacheService(
        FinBertSentimentService? finbert,
        IServiceScopeFactory scopeFactory,
        ILogger<SentimentCacheService> logger)
    {
        _finbert = finbert;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>
    /// Queue a headline for background FinBERT analysis.
    /// </summary>
    /// <param name="headline">Headline text to analyze</param>
    public async Task QueueForAnalysisAsync(string headline)
    {
        if (string.IsNullOrWhiteSpace(headline))
            return;

        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ISentimentCacheRepository>();

        // Only queue if not already cached
        if (!await repo.HasCachedResultAsync(headline))
        {
            await repo.QueueHeadlineAsync(headline);
        }
    }

    /// <summary>
    /// Get cached FinBERT result, or null if not yet analyzed.
    /// </summary>
    /// <param name="headline">Headline to look up</param>
    /// <returns>Cached result, or null if pending</returns>
    public async Task<FinBertSentimentService.FinBertResult?> GetCachedResultAsync(string headline)
    {
        if (string.IsNullOrWhiteSpace(headline))
            return new FinBertSentimentService.FinBertResult("neutral", 1.0f, 0f, 0f, 1.0f);

        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ISentimentCacheRepository>();
        return await repo.GetCachedResultAsync(headline);
    }

    /// <summary>
    /// Analyze a headline synchronously (blocking).
    /// Use this only when you need immediate results and can't wait for background processing.
    /// </summary>
    /// <param name="headline">Headline to analyze</param>
    /// <returns>FinBERT result, or null if FinBERT is not available</returns>
    public FinBertSentimentService.FinBertResult? AnalyzeNow(string headline)
    {
        if (_finbert == null || string.IsNullOrWhiteSpace(headline))
            return null;

        return _finbert.Analyze(headline);
    }

    /// <summary>
    /// Get cache statistics for monitoring.
    /// </summary>
    public async Task<(int cached, int pending, bool finbertAvailable)> GetStatisticsAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ISentimentCacheRepository>();
        var stats = await repo.GetStatisticsAsync();
        return (stats.cached, stats.pending, _finbert != null);
    }

    /// <summary>
    /// Background loop that continuously processes pending headlines.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_finbert == null)
        {
            _logger.LogWarning(
                "SentimentCacheService starting in pass-through mode (FinBERT not available). " +
                "Ensure model files are deployed to enable ML-based sentiment analysis.");
            return;
        }

        _logger.LogInformation("SentimentCacheService starting with FinBERT enabled");

        // Defer startup to allow app to become responsive first
        await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);

        // Log initial statistics
        try
        {
            var stats = await GetStatisticsAsync();
            _logger.LogInformation(
                "Sentiment cache initialized: {Cached} cached, {Pending} pending",
                stats.cached, stats.pending);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read sentiment cache statistics");
        }

        // Continuous processing loop
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                int processed = await ProcessPendingHeadlinesAsync(stoppingToken);

                // If we processed items, check for more quickly
                // If queue was empty, wait longer before checking again
                await Task.Delay(processed > 0 ? _processDelay : _idleDelay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error in sentiment cache loop");
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }

        _logger.LogInformation("SentimentCacheService stopping");
    }

    /// <summary>
    /// Process a batch of pending headlines.
    /// </summary>
    /// <returns>Number of headlines processed</returns>
    private async Task<int> ProcessPendingHeadlinesAsync(CancellationToken cancellationToken)
    {
        if (_finbert == null)
            return 0;

        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ISentimentCacheRepository>();

        // Get pending headlines
        var pending = await repo.GetPendingHeadlinesAsync(_batchSize);

        if (pending.Count == 0)
            return 0;

        int processed = 0;
        foreach (var headline in pending)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            try
            {
                var result = _finbert.Analyze(headline);
                await repo.CacheResultAsync(headline, result);
                processed++;

                _logger.LogDebug(
                    "Cached sentiment for headline: {Label} ({Confidence:P0})",
                    result.Label, result.Confidence);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to analyze headline: {Headline}", headline[..Math.Min(50, headline.Length)]);
            }
        }

        if (processed > 0)
        {
            _logger.LogDebug("Processed {Count} headlines", processed);
        }

        return processed;
    }
}
