using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace StockAnalyzer.Core.Services;

/// <summary>
/// Background service that maintains a cache of processed cat and dog images.
/// Uses database persistence via ICachedImageRepository for cross-restart durability.
/// </summary>
public class ImageCacheService : BackgroundService
{
    private readonly ImageProcessingService _processor;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ImageCacheService> _logger;

    private readonly int _cacheSize;
    private readonly int _refillThreshold;
    private readonly TimeSpan _refillDelay = TimeSpan.FromMilliseconds(500);

    public ImageCacheService(
        ImageProcessingService processor,
        IServiceScopeFactory scopeFactory,
        ILogger<ImageCacheService> logger,
        int cacheSize = 1000,
        int refillThreshold = 100)
    {
        _processor = processor;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _cacheSize = cacheSize;
        _refillThreshold = refillThreshold;
    }

    /// <summary>
    /// Get a processed cat image from the cache.
    /// Returns a random image, or null if cache is empty.
    /// </summary>
    public async Task<byte[]?> GetCatImageAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ICachedImageRepository>();
        return await repo.GetRandomImageAsync("cat");
    }

    /// <summary>
    /// Get a processed dog image from the cache.
    /// Returns a random image, or null if cache is empty.
    /// </summary>
    public async Task<byte[]?> GetDogImageAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ICachedImageRepository>();
        return await repo.GetRandomImageAsync("dog");
    }

    /// <summary>
    /// Synchronous wrapper for backward compatibility.
    /// Prefer async methods for new code.
    /// </summary>
    public byte[]? GetCatImage() => GetCatImageAsync().GetAwaiter().GetResult();

    /// <summary>
    /// Synchronous wrapper for backward compatibility.
    /// Prefer async methods for new code.
    /// </summary>
    public byte[]? GetDogImage() => GetDogImageAsync().GetAwaiter().GetResult();

    /// <summary>
    /// Get current cache status for monitoring.
    /// </summary>
    public async Task<(int cats, int dogs, int maxSize)> GetCacheStatusAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ICachedImageRepository>();
        var counts = await repo.GetAllCountsAsync();
        return (counts["cat"], counts["dog"], _cacheSize);
    }

    /// <summary>
    /// Synchronous wrapper for backward compatibility with status endpoint.
    /// </summary>
    public (int cats, int dogs, int maxSize) GetCacheStatus()
    {
        return GetCacheStatusAsync().GetAwaiter().GetResult();
    }

    /// <summary>
    /// Background loop that continuously refills the cache when it's low.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "ImageCacheService starting. Target cache size: {Size}, refill threshold: {Threshold}",
            _cacheSize, _refillThreshold);

        // Delay startup to allow app to become responsive first
        // This prevents thread pool starvation during cold start
        _logger.LogInformation("Deferring image cache fill for 10 seconds to allow app startup...");
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        // Check existing cache and log status (persisted from previous run)
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<ICachedImageRepository>();
            var counts = await repo.GetAllCountsAsync();
            _logger.LogInformation(
                "Existing cache: {Cats} cats, {Dogs} dogs (persisted from previous run)",
                counts["cat"], counts["dog"]);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read existing cache status");
        }

        // Continuous monitoring and refill
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RefillIfNeededAsync(stoppingToken);
                await Task.Delay(_refillDelay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error in cache refill loop");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }

        _logger.LogInformation("ImageCacheService stopping");
    }

    /// <summary>
    /// Refill cache when below target size.
    /// </summary>
    private async Task RefillIfNeededAsync(CancellationToken cancellationToken)
    {
        Dictionary<string, int> counts;
        using (var scope = _scopeFactory.CreateScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<ICachedImageRepository>();
            counts = await repo.GetAllCountsAsync();
        }

        var tasks = new List<Task>();

        int catsNeeded = _cacheSize - counts["cat"];
        int dogsNeeded = _cacheSize - counts["dog"];

        // Process up to 5 at a time to avoid overwhelming external APIs
        if (catsNeeded > 0)
        {
            _logger.LogDebug("Refilling {Count} cat images", catsNeeded);
            tasks.AddRange(Enumerable.Range(0, Math.Min(catsNeeded, 5))
                .Select(_ => ProcessAndCacheImageAsync("cat", cancellationToken)));
        }

        if (dogsNeeded > 0)
        {
            _logger.LogDebug("Refilling {Count} dog images", dogsNeeded);
            tasks.AddRange(Enumerable.Range(0, Math.Min(dogsNeeded, 5))
                .Select(_ => ProcessAndCacheImageAsync("dog", cancellationToken)));
        }

        if (tasks.Count > 0)
        {
            await Task.WhenAll(tasks);
        }

        // Trim if over limit (safety valve)
        using (var scope = _scopeFactory.CreateScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<ICachedImageRepository>();
            await repo.TrimOldestAsync("cat", _cacheSize);
            await repo.TrimOldestAsync("dog", _cacheSize);
        }
    }

    /// <summary>
    /// Process a single image and add to the database cache.
    /// </summary>
    private async Task ProcessAndCacheImageAsync(string imageType, CancellationToken cancellationToken)
    {
        try
        {
            byte[]? image = imageType == "cat"
                ? await _processor.GetProcessedCatImageAsync(cancellationToken)
                : await _processor.GetProcessedDogImageAsync(cancellationToken);

            if (image != null)
            {
                using var scope = _scopeFactory.CreateScope();
                var repo = scope.ServiceProvider.GetRequiredService<ICachedImageRepository>();
                await repo.AddImageAsync(imageType, image);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to process {Type} image", imageType);
        }
    }
}
