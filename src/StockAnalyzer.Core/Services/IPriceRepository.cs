using StockAnalyzer.Core.Data.Entities;

namespace StockAnalyzer.Core.Services;

/// <summary>
/// Repository interface for historical price data operations.
/// Designed for efficient querying of large datasets (~1.26M+ price records).
/// </summary>
public interface IPriceRepository
{
    /// <summary>
    /// Get all prices for a security within a date range.
    /// </summary>
    /// <param name="securityAlias">The security's internal alias.</param>
    /// <param name="startDate">Start of date range (inclusive).</param>
    /// <param name="endDate">End of date range (inclusive).</param>
    /// <returns>List of price records ordered by date ascending.</returns>
    Task<List<PriceEntity>> GetPricesAsync(int securityAlias, DateTime startDate, DateTime endDate);

    /// <summary>
    /// Get all prices for a security (full history).
    /// Use with caution for securities with long histories.
    /// </summary>
    /// <param name="securityAlias">The security's internal alias.</param>
    /// <returns>List of all price records ordered by date ascending.</returns>
    Task<List<PriceEntity>> GetAllPricesAsync(int securityAlias);

    /// <summary>
    /// Get prices for a specific date across all securities.
    /// Useful for batch processing and cross-sectional analysis.
    /// </summary>
    /// <param name="date">The date to retrieve prices for.</param>
    /// <returns>List of all price records for the specified date.</returns>
    Task<List<PriceEntity>> GetPricesForDateAsync(DateTime date);

    /// <summary>
    /// Get the most recent price for a security.
    /// </summary>
    /// <param name="securityAlias">The security's internal alias.</param>
    /// <returns>The latest price record or null if no prices exist.</returns>
    Task<PriceEntity?> GetLatestPriceAsync(int securityAlias);

    /// <summary>
    /// Get the latest prices for multiple securities in a single query.
    /// More efficient than calling GetLatestPriceAsync in a loop.
    /// </summary>
    /// <param name="securityAliases">Collection of security aliases.</param>
    /// <returns>Dictionary mapping security alias to its latest price.</returns>
    Task<Dictionary<int, PriceEntity>> GetLatestPricesAsync(IEnumerable<int> securityAliases);

    /// <summary>
    /// Add a single price record.
    /// </summary>
    /// <param name="dto">The price data to create.</param>
    /// <returns>The created price entity with assigned ID.</returns>
    Task<PriceEntity> CreateAsync(PriceCreateDto dto);

    /// <summary>
    /// Bulk insert prices for efficient batch loading.
    /// Significantly faster than individual inserts for large datasets.
    /// </summary>
    /// <param name="prices">Collection of prices to insert.</param>
    /// <returns>Number of records inserted.</returns>
    Task<int> BulkInsertAsync(IEnumerable<PriceCreateDto> prices);

    /// <summary>
    /// Upsert a price (insert or update if exists).
    /// Uses the unique constraint on (SecurityAlias, EffectiveDate).
    /// </summary>
    /// <param name="dto">The price data to upsert.</param>
    Task UpsertAsync(PriceCreateDto dto);

    /// <summary>
    /// Delete prices older than a given date for a security.
    /// Useful for maintaining rolling history windows.
    /// </summary>
    /// <param name="securityAlias">The security's internal alias.</param>
    /// <param name="cutoffDate">Delete prices before this date.</param>
    /// <returns>Number of records deleted.</returns>
    Task<int> DeleteOlderThanAsync(int securityAlias, DateTime cutoffDate);

    /// <summary>
    /// Get the date range available for a security.
    /// </summary>
    /// <param name="securityAlias">The security's internal alias.</param>
    /// <returns>Tuple of (earliest date, latest date) or nulls if no data.</returns>
    Task<(DateTime? Earliest, DateTime? Latest)> GetDateRangeAsync(int securityAlias);

    /// <summary>
    /// Get total count of price records in the database.
    /// Useful for monitoring and statistics.
    /// </summary>
    /// <returns>Total number of price records.</returns>
    Task<long> GetTotalCountAsync();

    /// <summary>
    /// Get count of price records for a specific security.
    /// </summary>
    /// <param name="securityAlias">The security's internal alias.</param>
    /// <returns>Number of price records for the security.</returns>
    Task<int> GetCountForSecurityAsync(int securityAlias);
}

/// <summary>
/// DTO for creating or upserting a price record.
/// </summary>
public record PriceCreateDto
{
    /// <summary>
    /// The security's internal alias (required).
    /// </summary>
    public required int SecurityAlias { get; init; }

    /// <summary>
    /// The trading date for this price (required).
    /// </summary>
    public required DateTime EffectiveDate { get; init; }

    /// <summary>
    /// Opening price (required).
    /// </summary>
    public required decimal Open { get; init; }

    /// <summary>
    /// Highest price during the day (required).
    /// </summary>
    public required decimal High { get; init; }

    /// <summary>
    /// Lowest price during the day (required).
    /// </summary>
    public required decimal Low { get; init; }

    /// <summary>
    /// Closing price (required).
    /// </summary>
    public required decimal Close { get; init; }

    /// <summary>
    /// Optional calculated volatility.
    /// </summary>
    public decimal? Volatility { get; init; }

    /// <summary>
    /// Optional trading volume.
    /// </summary>
    public long? Volume { get; init; }

    /// <summary>
    /// Optional split/dividend adjusted closing price.
    /// </summary>
    public decimal? AdjustedClose { get; init; }
}
