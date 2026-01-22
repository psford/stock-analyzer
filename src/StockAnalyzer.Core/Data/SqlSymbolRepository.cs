using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StockAnalyzer.Core.Data.Entities;
using StockAnalyzer.Core.Models;
using StockAnalyzer.Core.Services;

namespace StockAnalyzer.Core.Data;

/// <summary>
/// SQL Server implementation of ISymbolRepository.
/// Optimized for sub-10ms search latency on ~10K symbols.
/// </summary>
public class SqlSymbolRepository : ISymbolRepository
{
    private readonly StockAnalyzerDbContext _context;
    private readonly ILogger<SqlSymbolRepository> _logger;

    public SqlSymbolRepository(StockAnalyzerDbContext context, ILogger<SqlSymbolRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<List<SearchResult>> SearchAsync(string query, int limit = 10, bool includeInactive = false)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new List<SearchResult>();

        var normalizedQuery = query.Trim().ToUpperInvariant();

        // Multi-tier ranking query:
        // 1 = exact symbol match
        // 2 = symbol starts with query
        // 3 = description starts with query
        // 4 = description contains query
        var results = await _context.Symbols
            .Where(s => (includeInactive || s.IsActive) &&
                        (s.Symbol.StartsWith(normalizedQuery) ||
                         s.Description.ToUpper().Contains(normalizedQuery)))
            .Select(s => new
            {
                s.Symbol,
                s.DisplaySymbol,
                s.Description,
                s.Exchange,
                s.Type,
                Rank = s.Symbol == normalizedQuery ? 1 :
                       s.Symbol.StartsWith(normalizedQuery) ? 2 :
                       s.Description.ToUpper().StartsWith(normalizedQuery) ? 3 : 4
            })
            .OrderBy(r => r.Rank)
            .ThenBy(r => r.Symbol)
            .Take(limit)
            .ToListAsync();

        return results.Select(r => new SearchResult
        {
            Symbol = r.Symbol,
            ShortName = r.Description,
            LongName = r.Description,
            Exchange = r.Exchange,
            Type = r.Type
        }).ToList();
    }

    /// <inheritdoc />
    public async Task<SearchResult?> GetBySymbolAsync(string symbol)
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
    public async Task<bool> ExistsAsync(string symbol)
    {
        return await _context.Symbols
            .AnyAsync(s => s.Symbol == symbol.ToUpperInvariant());
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
}
