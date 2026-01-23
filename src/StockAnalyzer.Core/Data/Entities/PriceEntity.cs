namespace StockAnalyzer.Core.Data.Entities;

/// <summary>
/// EF Core entity for historical price data.
/// Stores OHLC (Open, High, Low, Close) plus volatility and volume per trading day.
/// Optimized for scale: ~1.26M rows (500 stocks × 252 trading days × 10 years).
/// Stored in the 'data' schema to separate domain data from operational tables.
/// </summary>
public class PriceEntity
{
    /// <summary>
    /// Auto-incrementing primary key.
    /// Using BIGINT (long) to accommodate potential future scale beyond 2 billion rows.
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// Foreign key to SecurityMaster.
    /// Links this price record to its parent security.
    /// </summary>
    public int SecurityAlias { get; set; }

    /// <summary>
    /// Trading date for this price record.
    /// Stored as DATE type (no time component) since prices are daily.
    /// Combined with SecurityAlias forms a unique constraint.
    /// </summary>
    public DateTime EffectiveDate { get; set; }

    /// <summary>
    /// Opening price for the trading day.
    /// Precision: decimal(18,4) - supports prices up to 14 digits with 4 decimal places.
    /// </summary>
    public decimal Open { get; set; }

    /// <summary>
    /// Highest price reached during the trading day.
    /// </summary>
    public decimal High { get; set; }

    /// <summary>
    /// Lowest price reached during the trading day.
    /// </summary>
    public decimal Low { get; set; }

    /// <summary>
    /// Closing price for the trading day (unadjusted).
    /// </summary>
    public decimal Close { get; set; }

    /// <summary>
    /// Calculated volatility metric (e.g., historical volatility percentage).
    /// Nullable because it may be calculated separately or not available.
    /// Precision: decimal(10,6) - supports percentages with high precision.
    /// </summary>
    public decimal? Volatility { get; set; }

    /// <summary>
    /// Trading volume for the day (number of shares traded).
    /// Nullable because some data sources may not include volume.
    /// </summary>
    public long? Volume { get; set; }

    /// <summary>
    /// Split and dividend adjusted closing price.
    /// Essential for accurate historical return calculations.
    /// Nullable because not all data sources provide adjusted prices.
    /// </summary>
    public decimal? AdjustedClose { get; set; }

    /// <summary>
    /// Timestamp when this record was created (UTC).
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Navigation property to the parent security.
    /// </summary>
    public SecurityMasterEntity? Security { get; set; }
}
