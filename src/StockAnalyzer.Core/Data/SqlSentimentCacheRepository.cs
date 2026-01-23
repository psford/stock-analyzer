using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StockAnalyzer.Core.Data.Entities;
using StockAnalyzer.Core.Services;

namespace StockAnalyzer.Core.Data;

/// <summary>
/// SQL Server implementation of ISentimentCacheRepository.
/// Uses SHA256 hashing for efficient headline lookup.
/// </summary>
public class SqlSentimentCacheRepository : ISentimentCacheRepository
{
    private readonly StockAnalyzerDbContext _context;
    private readonly ILogger<SqlSentimentCacheRepository> _logger;

    public SqlSentimentCacheRepository(
        StockAnalyzerDbContext context,
        ILogger<SqlSentimentCacheRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<List<string>> GetPendingHeadlinesAsync(int count)
    {
        return await _context.CachedSentiments
            .Where(s => s.IsPending)
            .OrderBy(s => s.CreatedAt)
            .Take(count)
            .Select(s => s.Headline)
            .ToListAsync();
    }

    /// <inheritdoc />
    public async Task QueueHeadlineAsync(string headline)
    {
        var hash = ComputeHash(headline);

        // Check if already exists (pending or cached)
        var exists = await _context.CachedSentiments
            .AnyAsync(s => s.HeadlineHash == hash);

        if (exists)
            return;

        var entity = new CachedSentimentEntity
        {
            HeadlineHash = hash,
            Headline = headline,
            IsPending = true,
            CreatedAt = DateTime.UtcNow
        };

        _context.CachedSentiments.Add(entity);
        await _context.SaveChangesAsync();

        _logger.LogDebug("Queued headline for analysis: {Headline}", headline[..Math.Min(50, headline.Length)]);
    }

    /// <inheritdoc />
    public async Task CacheResultAsync(string headline, FinBertSentimentService.FinBertResult result)
    {
        var hash = ComputeHash(headline);

        var entity = await _context.CachedSentiments
            .FirstOrDefaultAsync(s => s.HeadlineHash == hash);

        if (entity == null)
        {
            // Create new entry if not queued first
            entity = new CachedSentimentEntity
            {
                HeadlineHash = hash,
                Headline = headline,
                CreatedAt = DateTime.UtcNow
            };
            _context.CachedSentiments.Add(entity);
        }

        // Update with results
        entity.Sentiment = result.Label;
        entity.Confidence = (decimal)result.Confidence;
        entity.PositiveProb = (decimal)result.PositiveProb;
        entity.NegativeProb = (decimal)result.NegativeProb;
        entity.NeutralProb = (decimal)result.NeutralProb;
        entity.IsPending = false;

        await _context.SaveChangesAsync();
    }

    /// <inheritdoc />
    public async Task<FinBertSentimentService.FinBertResult?> GetCachedResultAsync(string headline)
    {
        var hash = ComputeHash(headline);

        var entity = await _context.CachedSentiments
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.HeadlineHash == hash && !s.IsPending);

        if (entity == null)
            return null;

        return new FinBertSentimentService.FinBertResult(
            entity.Sentiment,
            (float)entity.Confidence,
            (float)entity.PositiveProb,
            (float)entity.NegativeProb,
            (float)entity.NeutralProb
        );
    }

    /// <inheritdoc />
    public async Task<bool> HasCachedResultAsync(string headline)
    {
        var hash = ComputeHash(headline);
        return await _context.CachedSentiments
            .AnyAsync(s => s.HeadlineHash == hash);
    }

    /// <inheritdoc />
    public async Task PruneOldEntriesAsync(int maxAgeDays)
    {
        var cutoff = DateTime.UtcNow.AddDays(-maxAgeDays);

        var deleted = await _context.CachedSentiments
            .Where(s => s.CreatedAt < cutoff)
            .ExecuteDeleteAsync();

        if (deleted > 0)
        {
            _logger.LogInformation("Pruned {Count} old sentiment cache entries", deleted);
        }
    }

    /// <inheritdoc />
    public async Task<(int cached, int pending)> GetStatisticsAsync()
    {
        var stats = await _context.CachedSentiments
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Cached = g.Count(s => !s.IsPending),
                Pending = g.Count(s => s.IsPending)
            })
            .FirstOrDefaultAsync();

        return (stats?.Cached ?? 0, stats?.Pending ?? 0);
    }

    /// <summary>
    /// Compute SHA256 hash of headline for fast lookup.
    /// </summary>
    private static string ComputeHash(string headline)
    {
        var bytes = Encoding.UTF8.GetBytes(headline.ToLowerInvariant().Trim());
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }
}
