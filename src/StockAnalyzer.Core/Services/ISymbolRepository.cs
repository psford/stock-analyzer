using StockAnalyzer.Core.Models;

namespace StockAnalyzer.Core.Services;

/// <summary>
/// Repository interface for symbol lookup and search.
/// Designed for sub-10ms search performance using local database.
/// </summary>
public interface ISymbolRepository
{
    /// <summary>
    /// Search symbols by query string.
    /// Returns ranked results: exact match > prefix > contains.
    /// </summary>
    /// <param name="query">Search query (minimum 1 character)</param>
    /// <param name="limit">Maximum results to return (default 10)</param>
    /// <param name="includeInactive">Include delisted symbols (default false)</param>
    Task<List<SearchResult>> SearchAsync(string query, int limit = 10, bool includeInactive = false);

    /// <summary>
    /// Get a symbol by exact ticker match.
    /// </summary>
    Task<SearchResult?> GetBySymbolAsync(string symbol);

    /// <summary>
    /// Check if a symbol exists in the database.
    /// </summary>
    Task<bool> ExistsAsync(string symbol);

    /// <summary>
    /// Bulk upsert symbols from Finnhub refresh.
    /// </summary>
    Task<int> UpsertManyAsync(IEnumerable<SymbolUpsertDto> symbols);

    /// <summary>
    /// Mark symbols as inactive that were not in the latest refresh.
    /// </summary>
    Task<int> MarkInactiveAsync(IEnumerable<string> activeSymbols);

    /// <summary>
    /// Get count of active symbols.
    /// </summary>
    Task<int> GetActiveCountAsync();

    /// <summary>
    /// Get last refresh timestamp.
    /// </summary>
    Task<DateTime?> GetLastRefreshTimeAsync();
}

/// <summary>
/// DTO for bulk symbol upsert operations.
/// </summary>
public record SymbolUpsertDto
{
    public required string Symbol { get; init; }
    public required string DisplaySymbol { get; init; }
    public required string Description { get; init; }
    public required string Type { get; init; }
    public string? Exchange { get; init; }
    public string? Mic { get; init; }
    public string? Currency { get; init; }
    public string? Figi { get; init; }
    public string Country { get; init; } = "US";
}
