using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StockAnalyzer.Core.Data.Entities;
using StockAnalyzer.Core.Services;

namespace StockAnalyzer.Core.Data;

/// <summary>
/// SQL Server implementation of ICachedImageRepository.
/// Uses NEWID() for efficient random selection without loading all IDs.
/// </summary>
public class SqlCachedImageRepository : ICachedImageRepository
{
    private readonly StockAnalyzerDbContext _context;
    private readonly ILogger<SqlCachedImageRepository> _logger;

    public int MaxCacheSize { get; }

    public SqlCachedImageRepository(
        StockAnalyzerDbContext context,
        ILogger<SqlCachedImageRepository> logger,
        int maxCacheSize = 1000)
    {
        _context = context;
        _logger = logger;
        MaxCacheSize = maxCacheSize;
    }

    /// <inheritdoc />
    public async Task<byte[]?> GetRandomImageAsync(string imageType)
    {
        // Use raw SQL to guarantee NEWID() is executed fresh each time
        // EF.Functions.Random() gets compiled once and cached, returning same result
        var entity = await _context.CachedImages
            .FromSqlRaw("SELECT TOP 1 * FROM CachedImages WHERE ImageType = {0} ORDER BY NEWID()", imageType)
            .AsNoTracking()
            .FirstOrDefaultAsync();

        return entity?.ImageData;
    }

    /// <inheritdoc />
    public async Task AddImageAsync(string imageType, byte[] imageData)
    {
        var entity = new CachedImageEntity
        {
            ImageType = imageType,
            ImageData = imageData,
            CreatedAt = DateTime.UtcNow
        };

        _context.CachedImages.Add(entity);
        await _context.SaveChangesAsync();

        _logger.LogDebug("Added {Type} image to cache ({Size} bytes)", imageType, imageData.Length);
    }

    /// <inheritdoc />
    public async Task<int> GetCountAsync(string imageType)
    {
        return await _context.CachedImages
            .CountAsync(i => i.ImageType == imageType);
    }

    /// <inheritdoc />
    public async Task<Dictionary<string, int>> GetAllCountsAsync()
    {
        var counts = await _context.CachedImages
            .GroupBy(i => i.ImageType)
            .Select(g => new { Type = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Type, x => x.Count);

        // Ensure both types are present
        if (!counts.ContainsKey("cat")) counts["cat"] = 0;
        if (!counts.ContainsKey("dog")) counts["dog"] = 0;

        return counts;
    }

    /// <inheritdoc />
    public async Task<int> TrimOldestAsync(string imageType, int maxCount)
    {
        var currentCount = await GetCountAsync(imageType);
        if (currentCount <= maxCount)
            return 0;

        var deleteCount = currentCount - maxCount;

        // Get IDs of oldest images to delete
        var idsToDelete = await _context.CachedImages
            .Where(i => i.ImageType == imageType)
            .OrderBy(i => i.CreatedAt)
            .Take(deleteCount)
            .Select(i => i.Id)
            .ToListAsync();

        if (idsToDelete.Count == 0)
            return 0;

        // Delete using EF Core for proper tracking
        await _context.CachedImages
            .Where(i => idsToDelete.Contains(i.Id))
            .ExecuteDeleteAsync();

        _logger.LogInformation("Trimmed {Count} old {Type} images from cache", idsToDelete.Count, imageType);
        return idsToDelete.Count;
    }

    /// <inheritdoc />
    public async Task ClearAllAsync()
    {
        await _context.CachedImages.ExecuteDeleteAsync();
        _logger.LogInformation("Cleared all cached images");
    }
}
