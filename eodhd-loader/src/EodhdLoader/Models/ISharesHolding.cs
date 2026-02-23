namespace EodhdLoader.Models;

/// <summary>
/// Represents a single holding in an iShares ETF, parsed from iShares JSON API.
/// </summary>
public record ISharesHolding(
    string Ticker,
    string Name,
    string? Sector,
    decimal? MarketValue,
    decimal? Weight,        // Decimal fraction (0.065 = 6.5%)
    decimal? Shares,
    string? Location,
    string? Exchange,
    string? Currency,
    string? Cusip,
    string? Isin,
    string? Sedol
);
