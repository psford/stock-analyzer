namespace StockAnalyzer.Core.Models;

/// <summary>
/// Search result from ticker/company name search.
/// </summary>
public record SearchResult
{
    public required string Symbol { get; init; }
    public required string ShortName { get; init; }
    public string? LongName { get; init; }
    public string? Exchange { get; init; }
    public string? Type { get; init; }

    /// <summary>
    /// Display name combining symbol and company name.
    /// </summary>
    public string DisplayName => $"{Symbol} - {ShortName}" + (Exchange != null ? $" ({Exchange})" : "");
}
