using System.Text.Json;
using Microsoft.Extensions.Logging;
using StockAnalyzer.Core.Models;

namespace StockAnalyzer.Core.Services;

/// <summary>
/// JSON file-based implementation of IWatchlistRepository.
/// Stores watchlists in a JSON file for simple persistence.
/// Designed to be replaced with EF Core implementation for database storage.
/// </summary>
public class JsonWatchlistRepository : IWatchlistRepository
{
    private readonly string _filePath;
    private readonly ILogger<JsonWatchlistRepository> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public JsonWatchlistRepository(string filePath, ILogger<JsonWatchlistRepository> logger)
    {
        _filePath = filePath;
        _logger = logger;

        // Ensure directory exists
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    public async Task<List<Watchlist>> GetAllAsync(string? userId = null)
    {
        await _lock.WaitAsync();
        try
        {
            var storage = await LoadStorageAsync();
            var watchlists = storage.Watchlists;

            if (userId != null)
            {
                watchlists = watchlists.Where(w => w.UserId == userId).ToList();
            }

            return watchlists;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<Watchlist?> GetByIdAsync(string id, string? userId = null)
    {
        await _lock.WaitAsync();
        try
        {
            var storage = await LoadStorageAsync();
            var watchlist = storage.Watchlists.FirstOrDefault(w => w.Id == id);

            if (watchlist != null && userId != null && watchlist.UserId != userId)
            {
                return null; // User doesn't own this watchlist
            }

            return watchlist;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<Watchlist> CreateAsync(Watchlist watchlist)
    {
        await _lock.WaitAsync();
        try
        {
            var storage = await LoadStorageAsync();

            var newWatchlist = watchlist with
            {
                Id = Guid.NewGuid().ToString("N"),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            storage = storage with
            {
                Watchlists = storage.Watchlists.Append(newWatchlist).ToList()
            };

            await SaveStorageAsync(storage);
            _logger.LogInformation("Created watchlist: {Id} - {Name}", newWatchlist.Id, newWatchlist.Name);

            return newWatchlist;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<Watchlist?> UpdateAsync(Watchlist watchlist)
    {
        await _lock.WaitAsync();
        try
        {
            var storage = await LoadStorageAsync();
            var index = storage.Watchlists.FindIndex(w => w.Id == watchlist.Id);

            if (index == -1)
            {
                return null;
            }

            var updatedWatchlist = watchlist with
            {
                UpdatedAt = DateTime.UtcNow
            };

            storage.Watchlists[index] = updatedWatchlist;
            await SaveStorageAsync(storage);
            _logger.LogInformation("Updated watchlist: {Id}", watchlist.Id);

            return updatedWatchlist;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<bool> DeleteAsync(string id, string? userId = null)
    {
        await _lock.WaitAsync();
        try
        {
            var storage = await LoadStorageAsync();
            var watchlist = storage.Watchlists.FirstOrDefault(w => w.Id == id);

            if (watchlist == null)
            {
                return false;
            }

            if (userId != null && watchlist.UserId != userId)
            {
                return false; // User doesn't own this watchlist
            }

            storage = storage with
            {
                Watchlists = storage.Watchlists.Where(w => w.Id != id).ToList()
            };

            await SaveStorageAsync(storage);
            _logger.LogInformation("Deleted watchlist: {Id}", id);

            return true;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<Watchlist?> AddTickerAsync(string id, string ticker, string? userId = null)
    {
        await _lock.WaitAsync();
        try
        {
            var storage = await LoadStorageAsync();
            var watchlist = storage.Watchlists.FirstOrDefault(w => w.Id == id);

            if (watchlist == null)
            {
                return null;
            }

            if (userId != null && watchlist.UserId != userId)
            {
                return null;
            }

            var normalizedTicker = ticker.ToUpperInvariant().Trim();

            // Don't add duplicates
            if (watchlist.Tickers.Contains(normalizedTicker, StringComparer.OrdinalIgnoreCase))
            {
                return watchlist;
            }

            var updatedWatchlist = watchlist with
            {
                Tickers = watchlist.Tickers.Append(normalizedTicker).ToList(),
                UpdatedAt = DateTime.UtcNow
            };

            var index = storage.Watchlists.FindIndex(w => w.Id == id);
            storage.Watchlists[index] = updatedWatchlist;
            await SaveStorageAsync(storage);

            _logger.LogInformation("Added ticker {Ticker} to watchlist {Id}", normalizedTicker, id);

            return updatedWatchlist;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<Watchlist?> RemoveTickerAsync(string id, string ticker, string? userId = null)
    {
        await _lock.WaitAsync();
        try
        {
            var storage = await LoadStorageAsync();
            var watchlist = storage.Watchlists.FirstOrDefault(w => w.Id == id);

            if (watchlist == null)
            {
                return null;
            }

            if (userId != null && watchlist.UserId != userId)
            {
                return null;
            }

            var normalizedTicker = ticker.ToUpperInvariant().Trim();

            var updatedWatchlist = watchlist with
            {
                Tickers = watchlist.Tickers
                    .Where(t => !t.Equals(normalizedTicker, StringComparison.OrdinalIgnoreCase))
                    .ToList(),
                UpdatedAt = DateTime.UtcNow
            };

            var index = storage.Watchlists.FindIndex(w => w.Id == id);
            storage.Watchlists[index] = updatedWatchlist;
            await SaveStorageAsync(storage);

            _logger.LogInformation("Removed ticker {Ticker} from watchlist {Id}", normalizedTicker, id);

            return updatedWatchlist;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<WatchlistStorage> LoadStorageAsync()
    {
        if (!File.Exists(_filePath))
        {
            return new WatchlistStorage();
        }

        try
        {
            var json = await File.ReadAllTextAsync(_filePath);
            return JsonSerializer.Deserialize<WatchlistStorage>(json, JsonOptions)
                   ?? new WatchlistStorage();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading watchlist storage from {Path}", _filePath);
            return new WatchlistStorage();
        }
    }

    private async Task SaveStorageAsync(WatchlistStorage storage)
    {
        var json = JsonSerializer.Serialize(storage, JsonOptions);
        await File.WriteAllTextAsync(_filePath, json);
    }
}
