namespace StockAnalyzer.Core.Models;

/// <summary>
/// Search result from ticker/company name search.
/// </summary>
public record SearchResult
{
    public required string Symbol { get; init; }
    public required string ShortName { get; init; }
    public string? LongName { get; init; }
    public string? Exchange { get; init; }       // Set by external API services
    public string? MicCode { get; init; }        // ISO 10383 Market Identifier Code from SecurityMaster
    public string? ExchangeName { get; init; }   // Joined from MicExchange reference table
    public string? Type { get; init; }

    /// <summary>
    /// Display name combining symbol and company name.
    /// Prefers ExchangeName (e.g., "New York Stock Exchange") over Exchange for readability.
    /// </summary>
    public string DisplayName => $"{Symbol} - {ShortName}" +
        (ExchangeName != null ? $" ({ExchangeName})" :
         Exchange != null ? $" ({Exchange})" : "");
}
