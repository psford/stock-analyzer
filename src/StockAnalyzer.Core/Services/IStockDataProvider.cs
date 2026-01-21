using StockAnalyzer.Core.Models;

namespace StockAnalyzer.Core.Services;

/// <summary>
/// Interface for stock data providers. Each provider implements this
/// to enable cascading fallback and provider abstraction.
/// </summary>
public interface IStockDataProvider
{
    /// <summary>
    /// Human-readable provider name for logging.
    /// </summary>
    string ProviderName { get; }

    /// <summary>
    /// Priority order (lower = higher priority). Used for fallback ordering.
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// Check if provider is currently available (has API key, not rate limited).
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Get stock quote and company information.
    /// </summary>
    Task<StockInfo?> GetStockInfoAsync(string symbol, CancellationToken ct = default);

    /// <summary>
    /// Get historical OHLCV data.
    /// </summary>
    Task<HistoricalDataResult?> GetHistoricalDataAsync(
        string symbol,
        string period = "1y",
        CancellationToken ct = default);

    /// <summary>
    /// Search for symbols by name or ticker.
    /// </summary>
    Task<List<SearchResult>> SearchAsync(string query, CancellationToken ct = default);

    /// <summary>
    /// Get trending stocks. Not all providers support this - return empty list if unsupported.
    /// </summary>
    Task<List<(string Symbol, string Name)>> GetTrendingStocksAsync(
        int count = 10,
        CancellationToken ct = default);
}
