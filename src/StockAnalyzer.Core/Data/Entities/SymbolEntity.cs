namespace StockAnalyzer.Core.Data.Entities;

/// <summary>
/// EF Core entity for stock symbols cached from Finnhub.
/// Enables sub-10ms local search without API calls.
/// </summary>
public class SymbolEntity
{
    /// <summary>Primary key - the ticker symbol (e.g., "AAPL").</summary>
    public string Symbol { get; set; } = string.Empty;

    /// <summary>Display symbol from Finnhub (may differ from Symbol).</summary>
    public string DisplaySymbol { get; set; } = string.Empty;

    /// <summary>Company description/name from Finnhub.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Security type: Common Stock, ETF, ADR, etc.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Exchange name (e.g., "US" for US markets).</summary>
    public string? Exchange { get; set; }

    /// <summary>Market Identifier Code (MIC).</summary>
    public string? Mic { get; set; }

    /// <summary>Trading currency.</summary>
    public string? Currency { get; set; }

    /// <summary>FIGI identifier (if available).</summary>
    public string? Figi { get; set; }

    /// <summary>Country code (for future international support).</summary>
    public string Country { get; set; } = "US";

    /// <summary>Whether this symbol is actively traded.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>When this record was last updated from Finnhub.</summary>
    public DateTime LastUpdated { get; set; }

    /// <summary>When this record was first created.</summary>
    public DateTime CreatedAt { get; set; }
}
