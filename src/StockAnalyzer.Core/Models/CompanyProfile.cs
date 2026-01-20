namespace StockAnalyzer.Core.Models;

/// <summary>
/// Company profile including identifiers from Finnhub.
/// </summary>
public record CompanyProfile
{
    public required string Symbol { get; init; }
    public string? Name { get; init; }
    public string? Country { get; init; }
    public string? Currency { get; init; }
    public string? Exchange { get; init; }
    public string? Industry { get; init; }
    public string? WebUrl { get; init; }
    public string? Logo { get; init; }
    public string? IpoDate { get; init; }
    public decimal? MarketCapitalization { get; init; }
    public decimal? ShareOutstanding { get; init; }

    // Security identifiers
    public string? Isin { get; init; }
    public string? Cusip { get; init; }
    public string? Sedol { get; init; }
}
