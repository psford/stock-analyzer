namespace StockAnalyzer.Core.Models;

/// <summary>
/// Basic stock information including company details and current price data.
/// </summary>
public record StockInfo
{
    public required string Symbol { get; init; }
    public required string ShortName { get; init; }
    public string? LongName { get; init; }
    public string? Sector { get; init; }
    public string? Industry { get; init; }
    public string? Website { get; init; }
    public string? Country { get; init; }
    public string? Currency { get; init; }
    public string? Exchange { get; init; }

    // Security identifiers
    public string? Isin { get; init; }
    public string? Cusip { get; init; }
    public string? Sedol { get; init; }

    // Company profile
    public string? Description { get; init; }
    public int? FullTimeEmployees { get; init; }

    // Price data
    public decimal? CurrentPrice { get; init; }
    public decimal? PreviousClose { get; init; }
    public decimal? Open { get; init; }
    public decimal? DayHigh { get; init; }
    public decimal? DayLow { get; init; }
    public long? Volume { get; init; }
    public long? AverageVolume { get; init; }

    // Valuation metrics
    public decimal? MarketCap { get; init; }
    public decimal? PeRatio { get; init; }
    public decimal? ForwardPeRatio { get; init; }
    public decimal? PegRatio { get; init; }
    public decimal? PriceToBook { get; init; }

    // Dividend info
    public decimal? DividendYield { get; init; }
    public decimal? DividendRate { get; init; }

    // Performance metrics
    public decimal? FiftyTwoWeekHigh { get; init; }
    public decimal? FiftyTwoWeekLow { get; init; }
    public decimal? FiftyDayAverage { get; init; }
    public decimal? TwoHundredDayAverage { get; init; }

    // Calculated helpers
    public decimal? DayChange => CurrentPrice.HasValue && PreviousClose.HasValue
        ? CurrentPrice.Value - PreviousClose.Value
        : null;

    public decimal? DayChangePercent => CurrentPrice.HasValue && PreviousClose.HasValue && PreviousClose.Value != 0
        ? (CurrentPrice.Value - PreviousClose.Value) / PreviousClose.Value * 100
        : null;
}
