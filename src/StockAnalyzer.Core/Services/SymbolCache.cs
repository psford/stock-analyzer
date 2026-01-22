using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text;
using Microsoft.Extensions.Logging;
using StockAnalyzer.Core.Models;

namespace StockAnalyzer.Core.Services;

/// <summary>
/// In-memory cache for symbol data.
/// Loads ~30K symbols (~2-3 MB) at startup for sub-millisecond search performance.
/// Avoids database round-trips for every search request.
/// </summary>
public class SymbolCache
{
    private readonly ILogger<SymbolCache> _logger;

    // Primary storage: Dictionary for O(1) exact lookups
    private ConcurrentDictionary<string, CachedSymbol> _symbolsByTicker = new();

    // Secondary: List for search operations (maintains insertion order)
    private List<CachedSymbol> _allSymbols = new();
    private readonly object _refreshLock = new();

    public bool IsLoaded { get; private set; }
    public int Count => _symbolsByTicker.Count;
    public DateTime? LastRefreshTime { get; private set; }

    public SymbolCache(ILogger<SymbolCache> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Load all symbols into cache. Called at startup and on refresh.
    /// </summary>
    public void Load(IEnumerable<CachedSymbol> symbols)
    {
        lock (_refreshLock)
        {
            var newDict = new ConcurrentDictionary<string, CachedSymbol>();
            var newList = new List<CachedSymbol>();

            foreach (var symbol in symbols)
            {
                newDict[symbol.Symbol] = symbol;
                newList.Add(symbol);
            }

            // Atomic swap
            _symbolsByTicker = newDict;
            _allSymbols = newList;
            LastRefreshTime = DateTime.UtcNow;
            IsLoaded = true;

            _logger.LogInformation("Symbol cache loaded with {Count} symbols", newDict.Count);
        }
    }

    /// <summary>
    /// Search symbols by query. Returns ranked results in sub-millisecond time.
    /// </summary>
    public List<SearchResult> Search(string query, int limit = 10, bool includeInactive = false)
    {
        if (string.IsNullOrWhiteSpace(query) || !IsLoaded)
            return new List<SearchResult>();

        var normalizedQuery = query.Trim().ToUpperInvariant();

        // Fast path: exact match
        if (_symbolsByTicker.TryGetValue(normalizedQuery, out var exact) && (includeInactive || exact.IsActive))
        {
            return new List<SearchResult>
            {
                new SearchResult
                {
                    Symbol = exact.Symbol,
                    ShortName = exact.Description,
                    LongName = exact.Description,
                    Exchange = exact.Exchange,
                    Type = exact.Type
                }
            };
        }

        // Full search with ranking
        var results = _allSymbols
            .Where(s => includeInactive || s.IsActive)
            .Where(s => s.Symbol.StartsWith(normalizedQuery) ||
                       s.Description.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase))
            .Select(s => new
            {
                Symbol = s,
                Rank = s.Symbol == normalizedQuery ? 1 :
                       s.Symbol.StartsWith(normalizedQuery) ? 2 : 3
            })
            .OrderBy(x => x.Rank)
            .ThenBy(x => x.Symbol.Symbol)
            .Take(limit)
            .Select(x => new SearchResult
            {
                Symbol = x.Symbol.Symbol,
                ShortName = x.Symbol.Description,
                LongName = x.Symbol.Description,
                Exchange = x.Symbol.Exchange,
                Type = x.Symbol.Type
            })
            .ToList();

        return results;
    }

    /// <summary>
    /// Get a symbol by exact ticker match.
    /// </summary>
    public SearchResult? GetBySymbol(string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol) || !IsLoaded)
            return null;

        var normalized = symbol.Trim().ToUpperInvariant();

        if (_symbolsByTicker.TryGetValue(normalized, out var cached))
        {
            return new SearchResult
            {
                Symbol = cached.Symbol,
                ShortName = cached.Description,
                LongName = cached.Description,
                Exchange = cached.Exchange,
                Type = cached.Type
            };
        }

        return null;
    }

    /// <summary>
    /// Check if a symbol exists in cache.
    /// </summary>
    public bool Exists(string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol) || !IsLoaded)
            return false;

        return _symbolsByTicker.ContainsKey(symbol.Trim().ToUpperInvariant());
    }

    /// <summary>
    /// Get all active symbols for export/client-side search.
    /// </summary>
    public IEnumerable<CachedSymbol> GetAllActive()
    {
        if (!IsLoaded)
            return Enumerable.Empty<CachedSymbol>();

        return _allSymbols.Where(s => s.IsActive);
    }

    /// <summary>
    /// Add or update a single symbol in cache (after DB upsert).
    /// </summary>
    public void AddOrUpdate(CachedSymbol symbol)
    {
        lock (_refreshLock)
        {
            var existing = _symbolsByTicker.TryGetValue(symbol.Symbol, out var old);
            _symbolsByTicker[symbol.Symbol] = symbol;

            if (!existing)
            {
                _allSymbols.Add(symbol);
            }
            else
            {
                // Update in list
                var index = _allSymbols.FindIndex(s => s.Symbol == symbol.Symbol);
                if (index >= 0)
                {
                    _allSymbols[index] = symbol;
                }
            }
        }
    }

    /// <summary>
    /// Generate static symbols file for client-side search.
    /// Creates both uncompressed and gzipped versions.
    /// Format: SYMBOL|Description\n (pipe-delimited, one per line)
    /// </summary>
    /// <param name="wwwrootPath">Path to wwwroot directory</param>
    /// <returns>True if file was generated successfully</returns>
    public bool GenerateClientFile(string wwwrootPath)
    {
        if (!IsLoaded || _allSymbols.Count == 0)
        {
            _logger.LogWarning("Cannot generate client file - cache not loaded or empty");
            return false;
        }

        try
        {
            var dataDir = Path.Combine(wwwrootPath, "data");
            Directory.CreateDirectory(dataDir);

            var filePath = Path.Combine(dataDir, "symbols.txt");
            var gzipPath = Path.Combine(dataDir, "symbols.txt.gz");

            // Build content
            var sb = new StringBuilder();
            foreach (var symbol in _allSymbols.Where(s => s.IsActive))
            {
                sb.Append(symbol.Symbol);
                sb.Append('|');
                sb.Append(symbol.Description);
                sb.Append('\n');
            }

            var content = sb.ToString();
            var bytes = Encoding.UTF8.GetBytes(content);

            // Write uncompressed file
            File.WriteAllText(filePath, content);

            // Write gzipped file
            using (var fileStream = new FileStream(gzipPath, FileMode.Create))
            using (var gzipStream = new GZipStream(fileStream, CompressionLevel.Optimal))
            {
                gzipStream.Write(bytes, 0, bytes.Length);
            }

            var uncompressedSize = bytes.Length;
            var compressedSize = new FileInfo(gzipPath).Length;
            var compressionRatio = (1 - (double)compressedSize / uncompressedSize) * 100;

            _logger.LogInformation(
                "Generated client symbols file: {Count} symbols, {Uncompressed:N0} bytes uncompressed, {Compressed:N0} bytes gzipped ({Ratio:F1}% reduction)",
                _allSymbols.Count(s => s.IsActive),
                uncompressedSize,
                compressedSize,
                compressionRatio);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate client symbols file");
            return false;
        }
    }
}

/// <summary>
/// Lightweight symbol data for in-memory caching.
/// </summary>
public record CachedSymbol
{
    public required string Symbol { get; init; }
    public required string Description { get; init; }
    public required string Exchange { get; init; }
    public required string Type { get; init; }
    public bool IsActive { get; init; } = true;
}
