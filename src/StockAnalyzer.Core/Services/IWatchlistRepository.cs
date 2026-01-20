using StockAnalyzer.Core.Models;

namespace StockAnalyzer.Core.Services;

/// <summary>
/// Repository interface for watchlist persistence.
/// Designed for easy migration from JSON file to database storage.
/// </summary>
public interface IWatchlistRepository
{
    /// <summary>
    /// Get all watchlists, optionally filtered by user.
    /// </summary>
    /// <param name="userId">Optional user ID for multi-user support. Null returns all watchlists.</param>
    Task<List<Watchlist>> GetAllAsync(string? userId = null);

    /// <summary>
    /// Get a watchlist by ID, optionally verifying ownership.
    /// </summary>
    /// <param name="id">Watchlist ID.</param>
    /// <param name="userId">Optional user ID for ownership verification.</param>
    Task<Watchlist?> GetByIdAsync(string id, string? userId = null);

    /// <summary>
    /// Create a new watchlist.
    /// </summary>
    Task<Watchlist> CreateAsync(Watchlist watchlist);

    /// <summary>
    /// Update an existing watchlist.
    /// </summary>
    Task<Watchlist?> UpdateAsync(Watchlist watchlist);

    /// <summary>
    /// Delete a watchlist by ID.
    /// </summary>
    /// <param name="id">Watchlist ID.</param>
    /// <param name="userId">Optional user ID for ownership verification.</param>
    /// <returns>True if deleted, false if not found.</returns>
    Task<bool> DeleteAsync(string id, string? userId = null);

    /// <summary>
    /// Add a ticker to a watchlist.
    /// </summary>
    /// <param name="id">Watchlist ID.</param>
    /// <param name="ticker">Ticker symbol to add.</param>
    /// <param name="userId">Optional user ID for ownership verification.</param>
    /// <returns>Updated watchlist, or null if not found.</returns>
    Task<Watchlist?> AddTickerAsync(string id, string ticker, string? userId = null);

    /// <summary>
    /// Remove a ticker from a watchlist.
    /// </summary>
    /// <param name="id">Watchlist ID.</param>
    /// <param name="ticker">Ticker symbol to remove.</param>
    /// <param name="userId">Optional user ID for ownership verification.</param>
    /// <returns>Updated watchlist, or null if not found.</returns>
    Task<Watchlist?> RemoveTickerAsync(string id, string ticker, string? userId = null);
}
