using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StockAnalyzer.Core.Data.Entities;
using StockAnalyzer.Core.Models;
using StockAnalyzer.Core.Services;

namespace StockAnalyzer.Core.Data;

/// <summary>
/// SQL Server implementation of ISymbolRepository.
/// Optimized for sub-10ms search latency on ~30K symbols using Full-Text Search.
/// Falls back to LINQ for InMemory testing or SQL Server without FTS installed.
/// </summary>
public class SqlSymbolRepository : ISymbolRepository
{
    private readonly StockAnalyzerDbContext _context;
    private readonly ILogger<SqlSymbolRepository> _logger;
    private bool _fullTextSearchAvailable = true;

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

        // Use provider-aware search: SQL Server uses Full-Text Search, InMemory uses LINQ
        if (_context.Database.IsSqlServer() && _fullTextSearchAvailable)
        {
            try
            {
                return await SearchWithFullTextAsync(normalizedQuery, limit, includeInactive);
            }
            catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number == 7601 || ex.Number == 7609)
            {
                // 7601: Full-text search is not available
                // 7609: Full-Text Search is not installed
                _logger.LogWarning(ex, "Full-Text Search not available, falling back to LINQ search");
                _fullTextSearchAvailable = false;
                return await SearchWithLinqAsync(normalizedQuery, limit, includeInactive);
            }
        }
        else
        {
            // Fallback for InMemory database (testing) or SQL Server without FTS
            return await SearchWithLinqAsync(normalizedQuery, limit, includeInactive);
        }
    }

    /// <summary>
    /// SQL Server search using Full-Text Search for fast description matching.
    /// Production path - achieves sub-10ms latency on 30K+ symbols.
    /// </summary>
    private async Task<List<SearchResult>> SearchWithFullTextAsync(string normalizedQuery, int limit, bool includeInactive)
    {
        // For CONTAINS Full-Text Search, quote the term and add wildcard for prefix matching
        // Example: "APPLE*" matches "APPLE", "APPLE INC", etc.
        var ftsQuery = $"\"{normalizedQuery}*\"";

        // Use Full-Text Search with CONTAINS for fast description search
        // Multi-tier ranking:
        // 1 = exact symbol match
        // 2 = symbol starts with query
        // 3 = description contains query (via Full-Text index)
        // Parameterized query to prevent SQL injection
        var results = await _context.Database
            .SqlQueryRaw<SymbolSearchResult>(
                @"SELECT TOP (@limit)
                    Symbol,
                    Description,
                    Exchange,
                    Type,
                    CASE
                        WHEN Symbol = @query THEN 1
                        WHEN Symbol LIKE @queryPrefix THEN 2
                        ELSE 3
                    END AS Rank
                FROM Symbols
                WHERE (@includeInactive = 1 OR IsActive = 1)
                  AND (
                      Symbol LIKE @queryPrefix
                      OR CONTAINS(Description, @ftsQuery)
                  )
                ORDER BY Rank, Symbol",
                new Microsoft.Data.SqlClient.SqlParameter("@limit", limit),
                new Microsoft.Data.SqlClient.SqlParameter("@query", normalizedQuery),
                new Microsoft.Data.SqlClient.SqlParameter("@queryPrefix", $"{normalizedQuery}%"),
                new Microsoft.Data.SqlClient.SqlParameter("@ftsQuery", ftsQuery),
                new Microsoft.Data.SqlClient.SqlParameter("@includeInactive", includeInactive ? 1 : 0))
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

    /// <summary>
    /// LINQ-based search for InMemory database (testing).
    /// Not as fast as Full-Text Search but works without SQL Server.
    /// </summary>
    private async Task<List<SearchResult>> SearchWithLinqAsync(string normalizedQuery, int limit, bool includeInactive)
    {
        var baseQuery = _context.Symbols.AsNoTracking();

        if (!includeInactive)
        {
            baseQuery = baseQuery.Where(s => s.IsActive);
        }

        // Filter: symbol prefix OR description contains query
        var filtered = baseQuery.Where(s =>
            s.Symbol.StartsWith(normalizedQuery) ||
            s.Description.ToUpper().Contains(normalizedQuery));

        // Load to memory for ranking (acceptable for test data volumes)
        var candidates = await filtered.ToListAsync();

        // Rank and sort in memory
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

    // Internal DTO for raw SQL query results
    private class SymbolSearchResult
    {
        public string Symbol { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Exchange { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public int Rank { get; set; }
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
