namespace StockAnalyzer.Core.Models;

/// <summary>
/// Historical OHLCV (Open, High, Low, Close, Volume) data point.
/// </summary>
public record OhlcvData
{
    public required DateTime Date { get; init; }
    public required decimal Open { get; init; }
    public required decimal High { get; init; }
    public required decimal Low { get; init; }
    public required decimal Close { get; init; }
    public required long Volume { get; init; }

    // Optional adjusted close (for splits/dividends)
    public decimal? AdjustedClose { get; init; }
}

/// <summary>
/// Collection of historical data with metadata.
/// </summary>
public record HistoricalDataResult
{
    public required string Symbol { get; init; }
    public required string Period { get; init; }
    public required DateTime StartDate { get; init; }
    public required DateTime EndDate { get; init; }
    public required List<OhlcvData> Data { get; init; }

    // Calculated statistics
    public decimal? MinClose => Data.Count > 0 ? Data.Min(d => d.Close) : null;
    public decimal? MaxClose => Data.Count > 0 ? Data.Max(d => d.Close) : null;
    public decimal? AverageClose => Data.Count > 0 ? Data.Average(d => d.Close) : null;
    public long? AverageVolume => Data.Count > 0 ? (long)Data.Average(d => d.Volume) : null;
}

/// <summary>
/// Moving average data point.
/// </summary>
public record MovingAverageData
{
    public required DateTime Date { get; init; }
    public decimal? Sma20 { get; init; }
    public decimal? Sma50 { get; init; }
    public decimal? Sma200 { get; init; }
}
