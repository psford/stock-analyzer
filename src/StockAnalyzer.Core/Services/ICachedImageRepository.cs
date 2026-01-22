namespace StockAnalyzer.Core.Services;

/// <summary>
/// Repository interface for cached processed images.
/// Supports efficient random retrieval for hover card display.
/// </summary>
public interface ICachedImageRepository
{
    /// <summary>
    /// Get a random image of the specified type.
    /// Uses SQL Server's NEWID() for true random selection.
    /// </summary>
    /// <param name="imageType">"cat" or "dog"</param>
    /// <returns>Image bytes or null if cache empty</returns>
    Task<byte[]?> GetRandomImageAsync(string imageType);

    /// <summary>
    /// Add a new processed image to the cache.
    /// </summary>
    Task AddImageAsync(string imageType, byte[] imageData);

    /// <summary>
    /// Get current count of cached images by type.
    /// </summary>
    Task<int> GetCountAsync(string imageType);

    /// <summary>
    /// Get counts for all image types.
    /// </summary>
    /// <returns>Dictionary with "cat" and "dog" counts</returns>
    Task<Dictionary<string, int>> GetAllCountsAsync();

    /// <summary>
    /// Delete oldest images when cache exceeds max size.
    /// </summary>
    /// <param name="imageType">Type to trim</param>
    /// <param name="maxCount">Maximum images to retain</param>
    /// <returns>Number of images deleted</returns>
    Task<int> TrimOldestAsync(string imageType, int maxCount);

    /// <summary>
    /// Delete all images (for testing/reset).
    /// </summary>
    Task ClearAllAsync();

    /// <summary>
    /// Get configured maximum cache size.
    /// </summary>
    int MaxCacheSize { get; }
}
