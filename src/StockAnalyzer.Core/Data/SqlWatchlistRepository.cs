using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StockAnalyzer.Core.Data.Entities;
using StockAnalyzer.Core.Models;
using StockAnalyzer.Core.Services;

namespace StockAnalyzer.Core.Data;

/// <summary>
/// SQL Server/Azure SQL implementation of IWatchlistRepository using EF Core.
/// Replaces JsonWatchlistRepository for production database storage.
/// </summary>
public class SqlWatchlistRepository : IWatchlistRepository
{
    private readonly StockAnalyzerDbContext _context;
    private readonly ILogger<SqlWatchlistRepository> _logger;

    public SqlWatchlistRepository(StockAnalyzerDbContext context, ILogger<SqlWatchlistRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<List<Watchlist>> GetAllAsync(string? userId = null)
    {
        var query = _context.Watchlists
            .Include(w => w.Tickers)
            .Include(w => w.Holdings)
            .AsQueryable();

        if (userId != null)
        {
            query = query.Where(w => w.UserId == userId);
        }

        var entities = await query.ToListAsync();
        return entities.Select(MapToModel).ToList();
    }

    public async Task<Watchlist?> GetByIdAsync(string id, string? userId = null)
    {
        var entity = await _context.Watchlists
            .Include(w => w.Tickers)
            .Include(w => w.Holdings)
            .FirstOrDefaultAsync(w => w.Id == id);

        if (entity == null)
        {
            return null;
        }

        if (userId != null && entity.UserId != userId)
        {
            return null; // User doesn't own this watchlist
        }

        return MapToModel(entity);
    }

    public async Task<Watchlist> CreateAsync(Watchlist watchlist)
    {
        var entity = new WatchlistEntity
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = watchlist.UserId,
            Name = watchlist.Name,
            WeightingMode = watchlist.WeightingMode,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Tickers = watchlist.Tickers.Select(t => new WatchlistTickerEntity { Symbol = t }).ToList(),
            Holdings = watchlist.Holdings.Select(h => new TickerHoldingEntity
            {
                Symbol = h.Ticker,
                Shares = h.Shares,
                DollarValue = h.DollarValue
            }).ToList()
        };

        _context.Watchlists.Add(entity);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Created watchlist: {Id} - {Name}", entity.Id, entity.Name);

        return MapToModel(entity);
    }

    public async Task<Watchlist?> UpdateAsync(Watchlist watchlist)
    {
        var entity = await _context.Watchlists
            .Include(w => w.Tickers)
            .Include(w => w.Holdings)
            .FirstOrDefaultAsync(w => w.Id == watchlist.Id);

        if (entity == null)
        {
            return null;
        }

        entity.Name = watchlist.Name;
        entity.WeightingMode = watchlist.WeightingMode;
        entity.UpdatedAt = DateTime.UtcNow;

        // Update tickers - remove and re-add for simplicity
        entity.Tickers.Clear();
        foreach (var ticker in watchlist.Tickers)
        {
            entity.Tickers.Add(new WatchlistTickerEntity { WatchlistId = entity.Id, Symbol = ticker });
        }

        // Update holdings - remove and re-add for simplicity
        entity.Holdings.Clear();
        foreach (var holding in watchlist.Holdings)
        {
            entity.Holdings.Add(new TickerHoldingEntity
            {
                WatchlistId = entity.Id,
                Symbol = holding.Ticker,
                Shares = holding.Shares,
                DollarValue = holding.DollarValue
            });
        }

        await _context.SaveChangesAsync();

        _logger.LogInformation("Updated watchlist: {Id}", watchlist.Id);

        return MapToModel(entity);
    }

    public async Task<bool> DeleteAsync(string id, string? userId = null)
    {
        var entity = await _context.Watchlists.FirstOrDefaultAsync(w => w.Id == id);

        if (entity == null)
        {
            return false;
        }

        if (userId != null && entity.UserId != userId)
        {
            return false; // User doesn't own this watchlist
        }

        _context.Watchlists.Remove(entity);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Deleted watchlist: {Id}", id);

        return true;
    }

    public async Task<Watchlist?> AddTickerAsync(string id, string ticker, string? userId = null)
    {
        var entity = await _context.Watchlists
            .Include(w => w.Tickers)
            .Include(w => w.Holdings)
            .FirstOrDefaultAsync(w => w.Id == id);

        if (entity == null)
        {
            return null;
        }

        if (userId != null && entity.UserId != userId)
        {
            return null;
        }

        var normalizedTicker = ticker.ToUpperInvariant().Trim();

        // Don't add duplicates
        if (entity.Tickers.Any(t => t.Symbol.Equals(normalizedTicker, StringComparison.OrdinalIgnoreCase)))
        {
            return MapToModel(entity);
        }

        entity.Tickers.Add(new WatchlistTickerEntity { WatchlistId = entity.Id, Symbol = normalizedTicker });
        entity.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        _logger.LogInformation("Added ticker {Ticker} to watchlist {Id}", normalizedTicker, id);

        return MapToModel(entity);
    }

    public async Task<Watchlist?> RemoveTickerAsync(string id, string ticker, string? userId = null)
    {
        var entity = await _context.Watchlists
            .Include(w => w.Tickers)
            .Include(w => w.Holdings)
            .FirstOrDefaultAsync(w => w.Id == id);

        if (entity == null)
        {
            return null;
        }

        if (userId != null && entity.UserId != userId)
        {
            return null;
        }

        var normalizedTicker = ticker.ToUpperInvariant().Trim();

        var tickerToRemove = entity.Tickers
            .FirstOrDefault(t => t.Symbol.Equals(normalizedTicker, StringComparison.OrdinalIgnoreCase));

        if (tickerToRemove != null)
        {
            entity.Tickers.Remove(tickerToRemove);
            entity.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }

        _logger.LogInformation("Removed ticker {Ticker} from watchlist {Id}", normalizedTicker, id);

        return MapToModel(entity);
    }

    /// <summary>
    /// Maps EF Core entity to domain model.
    /// </summary>
    private static Watchlist MapToModel(WatchlistEntity entity)
    {
        return new Watchlist
        {
            Id = entity.Id,
            Name = entity.Name,
            UserId = entity.UserId,
            WeightingMode = entity.WeightingMode,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt,
            Tickers = entity.Tickers.Select(t => t.Symbol).ToList(),
            Holdings = entity.Holdings.Select(h => new TickerHolding
            {
                Ticker = h.Symbol,
                Shares = h.Shares,
                DollarValue = h.DollarValue
            }).ToList()
        };
    }
}
