namespace StockAnalyzer.Core.Models;

/// <summary>
/// Represents a significant price movement in a stock.
/// </summary>
public record SignificantMove
{
    public required DateTime Date { get; init; }
    public required decimal OpenPrice { get; init; }
    public required decimal ClosePrice { get; init; }
    public required decimal PercentChange { get; init; }
    public required long Volume { get; init; }

    // Related news that might explain the move
    public List<NewsItem>? RelatedNews { get; init; }

    // Classification
    public bool IsPositive => PercentChange > 0;
    public string Direction => IsPositive ? "up" : "down";
    public string Magnitude => Math.Abs(PercentChange) switch
    {
        >= 10 => "extreme",
        >= 5 => "major",
        >= 3 => "significant",
        _ => "notable"
    };
}

/// <summary>
/// Result of significant moves analysis.
/// </summary>
public record SignificantMovesResult
{
    public required string Symbol { get; init; }
    public required decimal Threshold { get; init; }
    public required List<SignificantMove> Moves { get; init; }

    public int PositiveMoves => Moves.Count(m => m.IsPositive);
    public int NegativeMoves => Moves.Count(m => !m.IsPositive);
    public decimal? LargestGain => Moves.Where(m => m.IsPositive).MaxBy(m => m.PercentChange)?.PercentChange;
    public decimal? LargestLoss => Moves.Where(m => !m.IsPositive).MinBy(m => m.PercentChange)?.PercentChange;
}
