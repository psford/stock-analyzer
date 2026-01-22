using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace StockAnalyzer.Core.Services;

/// <summary>
/// Background service that maintains a cache of processed cat and dog images.
/// Automatically refills the cache when it drops below the threshold.
/// </summary>
public class ImageCacheService : BackgroundService
{
    private readonly ImageProcessingService _processor;
    private readonly ILogger<ImageCacheService> _logger;
    private readonly ConcurrentQueue<byte[]> _catCache = new();
    private readonly ConcurrentQueue<byte[]> _dogCache = new();

    private readonly int _cacheSize;
    private readonly int _refillThreshold;
    private readonly TimeSpan _refillDelay = TimeSpan.FromMilliseconds(500);

    public ImageCacheService(
        ImageProcessingService processor,
        ILogger<ImageCacheService> logger,
        int cacheSize = 50,
        int refillThreshold = 10)
    {
        _processor = processor;
        _logger = logger;
        _cacheSize = cacheSize;
        _refillThreshold = refillThreshold;
    }

    /// <summary>
    /// Get a processed cat image from the cache.
    /// Returns null if cache is empty.
    /// </summary>
    public byte[]? GetCatImage()
    {
        _catCache.TryDequeue(out var image);
        return image;
    }

    /// <summary>
    /// Get a processed dog image from the cache.
    /// Returns null if cache is empty.
    /// </summary>
    public byte[]? GetDogImage()
    {
        _dogCache.TryDequeue(out var image);
        return image;
    }

    /// <summary>
    /// Get current cache status for monitoring.
    /// </summary>
    public (int cats, int dogs) GetCacheStatus()
    {
        return (_catCache.Count, _dogCache.Count);
    }

    /// <summary>
    /// Background loop that continuously refills the cache when it's low.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ImageCacheService starting. Target cache size: {Size}, refill threshold: {Threshold}",
            _cacheSize, _refillThreshold);

        // Delay startup to allow app to become responsive first
        // This prevents thread pool starvation during cold start
        _logger.LogInformation("Deferring image cache fill for 10 seconds to allow app startup...");
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        // Initial fill (non-blocking - uses gradual fill instead of batch)
        _logger.LogInformation("Starting gradual image cache fill...");

        // Continuous monitoring and refill
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var (cats, dogs) = GetCacheStatus();

                // Refill if below target cache size (not just threshold)
                // This ensures gradual fill on startup when cache is empty
                if (cats < _cacheSize || dogs < _cacheSize)
                {
                    await RefillCacheAsync(stoppingToken);
                }

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
    /// Initial cache fill on startup.
    /// </summary>
    private async Task FillCacheAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Filling image cache...");

        var catTasks = Enumerable.Range(0, _cacheSize)
            .Select(_ => ProcessAndCacheCatAsync(cancellationToken));

        var dogTasks = Enumerable.Range(0, _cacheSize)
            .Select(_ => ProcessAndCacheDogAsync(cancellationToken));

        // Process in batches to avoid overwhelming the APIs
        var allTasks = catTasks.Concat(dogTasks).ToList();

        // Process in batches of 10
        const int batchSize = 10;
        for (int i = 0; i < allTasks.Count; i += batchSize)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            var batch = allTasks.Skip(i).Take(batchSize);
            await Task.WhenAll(batch);

            // Small delay between batches to be nice to the APIs
            await Task.Delay(100, cancellationToken);
        }

        var (cats, dogs) = GetCacheStatus();
        _logger.LogInformation("Image cache filled. Cats: {Cats}, Dogs: {Dogs}", cats, dogs);
    }

    /// <summary>
    /// Refill cache when below threshold.
    /// </summary>
    private async Task RefillCacheAsync(CancellationToken cancellationToken)
    {
        var tasks = new List<Task>();

        int catsNeeded = _cacheSize - _catCache.Count;
        int dogsNeeded = _cacheSize - _dogCache.Count;

        if (catsNeeded > 0)
        {
            _logger.LogDebug("Refilling {Count} cat images", catsNeeded);
            tasks.AddRange(Enumerable.Range(0, Math.Min(catsNeeded, 5))
                .Select(_ => ProcessAndCacheCatAsync(cancellationToken)));
        }

        if (dogsNeeded > 0)
        {
            _logger.LogDebug("Refilling {Count} dog images", dogsNeeded);
            tasks.AddRange(Enumerable.Range(0, Math.Min(dogsNeeded, 5))
                .Select(_ => ProcessAndCacheDogAsync(cancellationToken)));
        }

        if (tasks.Count > 0)
        {
            await Task.WhenAll(tasks);
        }
    }

    private async Task ProcessAndCacheCatAsync(CancellationToken cancellationToken)
    {
        try
        {
            var image = await _processor.GetProcessedCatImageAsync(cancellationToken);
            if (image != null)
            {
                _catCache.Enqueue(image);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to process cat image");
        }
    }

    private async Task ProcessAndCacheDogAsync(CancellationToken cancellationToken)
    {
        try
        {
            var image = await _processor.GetProcessedDogImageAsync(cancellationToken);
            if (image != null)
            {
                _dogCache.Enqueue(image);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to process dog image");
        }
    }
}
