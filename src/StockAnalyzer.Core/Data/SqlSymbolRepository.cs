using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StockAnalyzer.Core.Data.Entities;
using StockAnalyzer.Core.Models;
using StockAnalyzer.Core.Services;

namespace StockAnalyzer.Core.Data;

/// <summary>
/// SQL Server implementation of ISymbolRepository.
/// Uses in-memory SymbolCache for sub-millisecond search performance.
/// Database operations (upsert, mark inactive) update both DB and cache.
/// </summary>
public class SqlSymbolRepository : ISymbolRepository
{
    private readonly StockAnalyzerDbContext _context;
    private readonly ILogger<SqlSymbolRepository> _logger;
    private readonly SymbolCache _cache;

    public SqlSymbolRepository(StockAnalyzerDbContext context, ILogger<SqlSymbolRepository> logger, SymbolCache cache)
    {
        _context = context;
        _logger = logger;
        _cache = cache;
    }

    /// <inheritdoc />
    public Task<List<SearchResult>> SearchAsync(string query, int limit = 10, bool includeInactive = false)
    {
        // Use in-memory cache for sub-millisecond search (no DB round-trip)
        if (_cache.IsLoaded)
        {
            return Task.FromResult(_cache.Search(query, limit, includeInactive));
        }

        // Fallback to DB if cache not yet loaded (rare - only during startup)
        return SearchFromDatabaseAsync(query, limit, includeInactive);
    }

    /// <summary>
    /// Database fallback for search (used before cache is loaded or in tests).
    /// </summary>
    private async Task<List<SearchResult>> SearchFromDatabaseAsync(string query, int limit, bool includeInactive)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new List<SearchResult>();

        var normalizedQuery = query.Trim().ToUpperInvariant();
        var baseQuery = _context.Symbols.AsNoTracking();

        if (!includeInactive)
        {
            baseQuery = baseQuery.Where(s => s.IsActive);
        }

        var filtered = baseQuery.Where(s =>
            s.Symbol.StartsWith(normalizedQuery) ||
            s.Description.ToUpper().Contains(normalizedQuery));

        var candidates = await filtered.ToListAsync();

        var ranked = candidates
            .Select(s => new
            {
                Entity = s,
                Rank = s.Symbol == normalizedQuery ? 1 :
                       s.Symbol.StartsWith(normalizedQuery) ? 2 : 3
            })
            .OrderBy(x => x.Rank)
            .ThenBy(x => x.Entity.Symbol)
            .Take(limit)
            .ToList();

        return ranked.Select(r => new SearchResult
        {
            Symbol = r.Entity.Symbol,
            ShortName = r.Entity.Description,
            LongName = r.Entity.Description,
            Exchange = r.Entity.Exchange,
            Type = r.Entity.Type
        }).ToList();
    }

    /// <inheritdoc />
    public Task<SearchResult?> GetBySymbolAsync(string symbol)
    {
        // Use cache for O(1) lookup
        if (_cache.IsLoaded)
        {
            return Task.FromResult(_cache.GetBySymbol(symbol));
        }

        // Fallback to DB
        return GetBySymbolFromDatabaseAsync(symbol);
    }

    private async Task<SearchResult?> GetBySymbolFromDatabaseAsync(string symbol)
    {
        var entity = await _context.Symbols
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Symbol == symbol.ToUpperInvariant());

        if (entity == null)
            return null;

        return new SearchResult
        {
            Symbol = entity.Symbol,
            ShortName = entity.Description,
            LongName = entity.Description,
            Exchange = entity.Exchange,
            Type = entity.Type
        };
    }

    /// <inheritdoc />
    public Task<bool> ExistsAsync(string symbol)
    {
        // Use cache for O(1) lookup
        if (_cache.IsLoaded)
        {
            return Task.FromResult(_cache.Exists(symbol));
        }

        // Fallback to DB
        return _context.Symbols.AnyAsync(s => s.Symbol == symbol.ToUpperInvariant());
    }

    /// <inheritdoc />
    public async Task<int> UpsertManyAsync(IEnumerable<SymbolUpsertDto> symbols)
    {
        var symbolList = symbols.ToList();
        var now = DateTime.UtcNow;
        var count = 0;

        // Process in batches of 500 for efficiency
        foreach (var batch in symbolList.Chunk(500))
        {
            foreach (var dto in batch)
            {
                var normalizedSymbol = dto.Symbol.ToUpperInvariant();
                var existing = await _context.Symbols
                    .FirstOrDefaultAsync(s => s.Symbol == normalizedSymbol);

                if (existing != null)
                {
                    // Update existing
                    existing.DisplaySymbol = dto.DisplaySymbol;
                    existing.Description = dto.Description;
                    existing.Type = dto.Type;
                    existing.Exchange = dto.Exchange;
                    existing.Mic = dto.Mic;
                    existing.Currency = dto.Currency;
                    existing.Figi = dto.Figi;
                    existing.Country = dto.Country;
                    existing.IsActive = true;
                    existing.LastUpdated = now;
                }
                else
                {
                    // Insert new
                    _context.Symbols.Add(new SymbolEntity
                    {
                        Symbol = normalizedSymbol,
                        DisplaySymbol = dto.DisplaySymbol,
                        Description = dto.Description,
                        Type = dto.Type,
                        Exchange = dto.Exchange,
                        Mic = dto.Mic,
                        Currency = dto.Currency,
                        Figi = dto.Figi,
                        Country = dto.Country,
                        IsActive = true,
                        LastUpdated = now,
                        CreatedAt = now
                    });
                }
                count++;
            }
            await _context.SaveChangesAsync();
        }

        _logger.LogInformation("Upserted {Count} symbols", count);
        return count;
    }

    /// <inheritdoc />
    public async Task<int> MarkInactiveAsync(IEnumerable<string> activeSymbols)
    {
        var activeSet = activeSymbols.Select(s => s.ToUpperInvariant()).ToHashSet();
        var now = DateTime.UtcNow;

        // Get all currently active symbols from DB (just the symbol strings)
        var currentActiveSymbols = await _context.Symbols
            .Where(s => s.IsActive)
            .Select(s => s.Symbol)
            .ToListAsync();

        // Find symbols to deactivate (in DB but not in active list)
        var toDeactivateSymbols = currentActiveSymbols.Where(s => !activeSet.Contains(s)).ToList();

        if (toDeactivateSymbols.Count == 0)
        {
            return 0;
        }

        // Process in batches to avoid loading too many entities at once
        const int batchSize = 500;
        var totalDeactivated = 0;

        foreach (var batch in toDeactivateSymbols.Chunk(batchSize))
        {
            var batchList = batch.ToList();
            var entities = await _context.Symbols
                .Where(s => batchList.Contains(s.Symbol))
                .ToListAsync();

            foreach (var entity in entities)
            {
                entity.IsActive = false;
                entity.LastUpdated = now;
            }

            await _context.SaveChangesAsync();
            totalDeactivated += entities.Count;
        }

        if (totalDeactivated > 0)
        {
            _logger.LogInformation("Marked {Count} symbols as inactive", totalDeactivated);
        }

        return totalDeactivated;
    }

    /// <inheritdoc />
    public async Task<int> GetActiveCountAsync()
    {
        return await _context.Symbols.CountAsync(s => s.IsActive);
    }

    /// <inheritdoc />
    public async Task<DateTime?> GetLastRefreshTimeAsync()
    {
        return await _context.Symbols
            .MaxAsync(s => (DateTime?)s.LastUpdated);
    }

    /// <summary>
    /// Load all symbols from database into in-memory cache.
    /// Called at application startup and after symbol refresh.
    /// </summary>
    public async Task LoadCacheAsync()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var symbols = await _context.Symbols
            .AsNoTracking()
            .Select(s => new CachedSymbol
            {
                Symbol = s.Symbol,
                Description = s.Description,
                Exchange = s.Exchange ?? "",
                Type = s.Type ?? "",
                IsActive = s.IsActive
            })
            .ToListAsync();

        _cache.Load(symbols);

        sw.Stop();
        _logger.LogInformation("Loaded {Count} symbols into cache in {ElapsedMs}ms",
            symbols.Count, sw.ElapsedMilliseconds);
    }
}
