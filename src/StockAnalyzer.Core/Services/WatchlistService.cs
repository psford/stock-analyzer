using Microsoft.Extensions.Logging;
using StockAnalyzer.Core.Helpers;
using StockAnalyzer.Core.Models;

namespace StockAnalyzer.Core.Services;

/// <summary>
/// Service for managing stock watchlists with business logic.
/// </summary>
public class WatchlistService
{
    private readonly IWatchlistRepository _repository;
    private readonly StockDataService _stockDataService;
    private readonly ILogger<WatchlistService> _logger;

    public WatchlistService(
        IWatchlistRepository repository,
        StockDataService stockDataService,
        ILogger<WatchlistService> logger)
    {
        _repository = repository;
        _stockDataService = stockDataService;
        _logger = logger;
    }

    /// <summary>
    /// Get all watchlists for a user (or all if single-user mode).
    /// </summary>
    public Task<List<Watchlist>> GetAllAsync(string? userId = null)
    {
        return _repository.GetAllAsync(userId);
    }

    /// <summary>
    /// Get a watchlist by ID.
    /// </summary>
    public Task<Watchlist?> GetByIdAsync(string id, string? userId = null)
    {
        return _repository.GetByIdAsync(id, userId);
    }

    /// <summary>
    /// Create a new watchlist.
    /// </summary>
    public async Task<Watchlist> CreateAsync(string name, string? userId = null)
    {
        var watchlist = new Watchlist
        {
            Id = string.Empty, // Will be set by repository
            Name = name.Trim(),
            Tickers = new List<string>(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            UserId = userId
        };

        return await _repository.CreateAsync(watchlist);
    }

    /// <summary>
    /// Rename a watchlist.
    /// </summary>
    public async Task<Watchlist?> RenameAsync(string id, string newName, string? userId = null)
    {
        var watchlist = await _repository.GetByIdAsync(id, userId);
        if (watchlist == null)
        {
            return null;
        }

        var updated = watchlist with { Name = newName.Trim() };
        return await _repository.UpdateAsync(updated);
    }

    /// <summary>
    /// Delete a watchlist.
    /// </summary>
    public Task<bool> DeleteAsync(string id, string? userId = null)
    {
        return _repository.DeleteAsync(id, userId);
    }

    /// <summary>
    /// Add a ticker to a watchlist.
    /// </summary>
    public Task<Watchlist?> AddTickerAsync(string id, string ticker, string? userId = null)
    {
        return _repository.AddTickerAsync(id, ticker, userId);
    }

    /// <summary>
    /// Remove a ticker from a watchlist.
    /// </summary>
    public Task<Watchlist?> RemoveTickerAsync(string id, string ticker, string? userId = null)
    {
        return _repository.RemoveTickerAsync(id, ticker, userId);
    }

    /// <summary>
    /// Get current quotes for all tickers in a watchlist.
    /// </summary>
    public async Task<WatchlistQuotes?> GetQuotesAsync(string id, string? userId = null)
    {
        var watchlist = await _repository.GetByIdAsync(id, userId);
        if (watchlist == null)
        {
            return null;
        }

        var quotes = new List<TickerQuote>();

        foreach (var ticker in watchlist.Tickers)
        {
            try
            {
                var stockInfo = await _stockDataService.GetStockInfoAsync(ticker);
                if (stockInfo != null)
                {
                    quotes.Add(new TickerQuote
                    {
                        Symbol = ticker,
                        Name = stockInfo.LongName ?? stockInfo.ShortName,
                        Price = stockInfo.CurrentPrice,
                        Change = stockInfo.DayChange,
                        ChangePercent = stockInfo.DayChangePercent
                    });
                }
                else
                {
                    quotes.Add(new TickerQuote
                    {
                        Symbol = ticker,
                        Name = null,
                        Price = null,
                        Change = null,
                        ChangePercent = null,
                        Error = "Quote unavailable"
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch quote for {Ticker}", ticker);
                quotes.Add(new TickerQuote
                {
                    Symbol = ticker,
                    Name = null,
                    Price = null,
                    Change = null,
                    ChangePercent = null,
                    Error = "Failed to fetch quote"
                });
            }
        }

        return new WatchlistQuotes
        {
            WatchlistId = watchlist.Id,
            WatchlistName = watchlist.Name,
            Quotes = quotes,
            FetchedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Update holdings (weighting mode and share/dollar values) for a watchlist.
    /// </summary>
    public async Task<Watchlist?> UpdateHoldingsAsync(
        string id,
        UpdateHoldingsRequest request,
        string? userId = null)
    {
        var watchlist = await _repository.GetByIdAsync(id, userId);
        if (watchlist == null)
        {
            return null;
        }

        // Validate weighting mode
        var validModes = new[] { "equal", "shares", "dollars" };
        if (!validModes.Contains(request.WeightingMode.ToLower()))
        {
            throw new ArgumentException($"Invalid weighting mode: {request.WeightingMode}");
        }

        var updated = watchlist with
        {
            WeightingMode = request.WeightingMode.ToLower(),
            Holdings = request.Holdings,
            UpdatedAt = DateTime.UtcNow
        };

        return await _repository.UpdateAsync(updated);
    }

    /// <summary>
    /// Get combined portfolio performance for a watchlist.
    /// Aggregates all tickers based on weighting mode and returns time series data.
    /// </summary>
    public async Task<CombinedPortfolioResult?> GetCombinedPortfolioAsync(
        string id,
        string period = "1y",
        string? benchmarkTicker = null,
        string? userId = null)
    {
        var watchlist = await _repository.GetByIdAsync(id, userId);
        if (watchlist == null || watchlist.Tickers.Count == 0)
        {
            return null;
        }

        _logger.LogInformation(
            "Fetching combined portfolio for {WatchlistName} with {TickerCount} tickers",
            watchlist.Name, watchlist.Tickers.Count);

        // Fetch historical data for all tickers in parallel
        var historyTasks = watchlist.Tickers
            .Select(t => _stockDataService.GetHistoricalDataAsync(t, period))
            .ToList();

        var histories = await Task.WhenAll(historyTasks);

        // Filter out failed fetches
        var validHistories = histories
            .Where(h => h != null && h.Data.Count > 0)
            .ToList();

        if (validHistories.Count == 0)
        {
            _logger.LogWarning("No valid historical data found for watchlist {WatchlistId}", LogSanitizer.Sanitize(id));
            return null;
        }

        // Calculate portfolio data based on weighting mode
        var (portfolioData, weights) = AggregatePortfolioData(watchlist, validHistories!);

        if (portfolioData.Count == 0)
        {
            return null;
        }

        // Calculate summary metrics
        var firstValue = portfolioData.First().PortfolioValue;
        var lastValue = portfolioData.Last().PortfolioValue;
        var totalReturn = firstValue != 0
            ? ((lastValue - firstValue) / firstValue) * 100
            : 0;

        // Calculate day change
        var dayChange = 0m;
        var dayChangePercent = 0m;
        if (portfolioData.Count >= 2)
        {
            var yesterday = portfolioData[^2].PortfolioValue;
            var today = portfolioData[^1].PortfolioValue;
            dayChange = today - yesterday;
            dayChangePercent = yesterday != 0 ? (dayChange / yesterday) * 100 : 0;
        }

        // Fetch benchmark data if requested
        List<PortfolioDataPoint>? benchmarkData = null;
        if (!string.IsNullOrEmpty(benchmarkTicker))
        {
            benchmarkData = await GetBenchmarkDataAsync(benchmarkTicker, period, portfolioData);
        }

        // Calculate significant moves (±5% days)
        var significantMoves = CalculateSignificantMoves(portfolioData, 5m);

        return new CombinedPortfolioResult
        {
            WatchlistId = watchlist.Id,
            WatchlistName = watchlist.Name,
            Period = period,
            WeightingMode = watchlist.WeightingMode,
            Data = portfolioData,
            TotalReturn = Math.Round(totalReturn, 2),
            DayChange = Math.Round(dayChange, 2),
            DayChangePercent = Math.Round(dayChangePercent, 2),
            TickerWeights = weights.ToDictionary(kv => kv.Key, kv => Math.Round(kv.Value * 100, 2)),
            BenchmarkData = benchmarkData,
            BenchmarkSymbol = benchmarkTicker,
            SignificantMoves = significantMoves
        };
    }

    /// <summary>
    /// Calculate significant moves from portfolio data (days with change >= threshold).
    /// </summary>
    private static List<PortfolioSignificantMove> CalculateSignificantMoves(
        List<PortfolioDataPoint> data,
        decimal threshold)
    {
        var moves = new List<PortfolioSignificantMove>();

        for (int i = 1; i < data.Count; i++)
        {
            var prevValue = data[i - 1].PortfolioValue;
            var currValue = data[i].PortfolioValue;

            if (prevValue == 0) continue;

            var dailyChange = ((currValue - prevValue) / prevValue) * 100;

            if (Math.Abs(dailyChange) >= threshold)
            {
                moves.Add(new PortfolioSignificantMove
                {
                    Date = data[i].Date,
                    PercentChange = Math.Round(dailyChange, 2)
                });
            }
        }

        return moves;
    }

    /// <summary>
    /// Aggregate portfolio data based on weighting mode.
    /// </summary>
    private (List<PortfolioDataPoint> Data, Dictionary<string, decimal> Weights)
        AggregatePortfolioData(Watchlist watchlist, List<HistoricalDataResult> histories)
    {
        var weights = CalculateWeights(watchlist, histories);

        // Find common dates across all tickers
        var allDates = histories
            .SelectMany(h => h.Data.Select(d => d.Date.Date))
            .GroupBy(d => d)
            .Where(g => g.Count() == histories.Count) // Only dates present in ALL tickers
            .Select(g => g.Key)
            .OrderBy(d => d)
            .ToList();

        if (allDates.Count == 0)
        {
            return (new List<PortfolioDataPoint>(), weights);
        }

        var result = new List<PortfolioDataPoint>();
        decimal? baseValue = null;

        foreach (var date in allDates)
        {
            decimal portfolioValue;

            if (watchlist.WeightingMode == "shares")
            {
                // Shares mode: value = sum of (shares * close price) for each date
                portfolioValue = CalculateSharesPortfolioValue(watchlist, histories, date);
            }
            else if (watchlist.WeightingMode == "dollars")
            {
                // Dollars mode: convert initial dollars to shares at period start
                portfolioValue = CalculateDollarsPortfolioValue(watchlist, histories, date, allDates[0]);
            }
            else
            {
                // Equal mode: average of normalized returns
                portfolioValue = CalculateEqualWeightPortfolioValue(histories, date, allDates[0]);
            }

            if (!baseValue.HasValue)
            {
                baseValue = portfolioValue;
            }

            var percentChange = baseValue > 0
                ? ((portfolioValue - baseValue.Value) / baseValue.Value) * 100
                : 0;

            result.Add(new PortfolioDataPoint
            {
                Date = date,
                PortfolioValue = Math.Round(portfolioValue, 2),
                PercentChange = Math.Round(percentChange, 2)
            });
        }

        return (result, weights);
    }

    /// <summary>
    /// Calculate weights based on weighting mode.
    /// </summary>
    private Dictionary<string, decimal> CalculateWeights(
        Watchlist watchlist,
        List<HistoricalDataResult> histories)
    {
        var weights = new Dictionary<string, decimal>();
        var validTickers = histories.Select(h => h.Symbol.ToUpper()).ToList();

        if (watchlist.WeightingMode == "equal" || watchlist.Holdings.Count == 0)
        {
            // Equal weighting
            var weight = 1m / validTickers.Count;
            foreach (var ticker in validTickers)
            {
                weights[ticker] = weight;
            }
        }
        else if (watchlist.WeightingMode == "shares")
        {
            // Weight by current market value (shares * current price)
            var totalValue = 0m;
            var tickerValues = new Dictionary<string, decimal>();

            foreach (var history in histories)
            {
                var holding = watchlist.Holdings
                    .FirstOrDefault(h => h.Ticker.Equals(history.Symbol, StringComparison.OrdinalIgnoreCase));
                var shares = holding?.Shares ?? 1;
                var currentPrice = history.Data.LastOrDefault()?.Close ?? 0;
                var value = shares * currentPrice;
                tickerValues[history.Symbol.ToUpper()] = value;
                totalValue += value;
            }

            foreach (var kv in tickerValues)
            {
                weights[kv.Key] = totalValue > 0 ? kv.Value / totalValue : 0;
            }
        }
        else if (watchlist.WeightingMode == "dollars")
        {
            // Weight by dollar allocation
            var totalDollars = watchlist.Holdings.Sum(h => h.DollarValue ?? 0);
            foreach (var holding in watchlist.Holdings)
            {
                var upperTicker = holding.Ticker.ToUpper();
                if (validTickers.Contains(upperTicker))
                {
                    weights[upperTicker] = totalDollars > 0
                        ? (holding.DollarValue ?? 0) / totalDollars
                        : 0;
                }
            }
        }

        return weights;
    }

    /// <summary>
    /// Calculate portfolio value using shares * historical close price for each date.
    /// </summary>
    private decimal CalculateSharesPortfolioValue(
        Watchlist watchlist,
        List<HistoricalDataResult> histories,
        DateTime date)
    {
        var totalValue = 0m;

        foreach (var history in histories)
        {
            var holding = watchlist.Holdings
                .FirstOrDefault(h => h.Ticker.Equals(history.Symbol, StringComparison.OrdinalIgnoreCase));
            var shares = holding?.Shares ?? 1;
            var dataPoint = history.Data.FirstOrDefault(d => d.Date.Date == date);
            if (dataPoint != null)
            {
                totalValue += shares * dataPoint.Close;
            }
        }

        return totalValue;
    }

    /// <summary>
    /// Calculate portfolio value for dollars mode.
    /// Converts initial dollars to shares at period start, then tracks value over time.
    /// </summary>
    private decimal CalculateDollarsPortfolioValue(
        Watchlist watchlist,
        List<HistoricalDataResult> histories,
        DateTime date,
        DateTime startDate)
    {
        var totalValue = 0m;

        foreach (var history in histories)
        {
            var holding = watchlist.Holdings
                .FirstOrDefault(h => h.Ticker.Equals(history.Symbol, StringComparison.OrdinalIgnoreCase));
            var dollars = holding?.DollarValue ?? 0;

            // Get price at period start to calculate implied shares
            var startDataPoint = history.Data.FirstOrDefault(d => d.Date.Date == startDate);
            if (startDataPoint == null || startDataPoint.Close == 0) continue;

            var impliedShares = dollars / startDataPoint.Close;

            // Get current price to calculate current value
            var currentDataPoint = history.Data.FirstOrDefault(d => d.Date.Date == date);
            if (currentDataPoint != null)
            {
                totalValue += impliedShares * currentDataPoint.Close;
            }
        }

        return totalValue;
    }

    /// <summary>
    /// Calculate equal-weighted portfolio value (average of normalized returns).
    /// </summary>
    private decimal CalculateEqualWeightPortfolioValue(
        List<HistoricalDataResult> histories,
        DateTime date,
        DateTime startDate)
    {
        // Use 100 as base value for equal weight (makes % change easy to read)
        const decimal baseValue = 100m;
        var totalReturn = 0m;
        var count = 0;

        foreach (var history in histories)
        {
            var startDataPoint = history.Data.FirstOrDefault(d => d.Date.Date == startDate);
            var currentDataPoint = history.Data.FirstOrDefault(d => d.Date.Date == date);

            if (startDataPoint != null && currentDataPoint != null && startDataPoint.Close > 0)
            {
                var tickerReturn = (currentDataPoint.Close - startDataPoint.Close) / startDataPoint.Close;
                totalReturn += tickerReturn;
                count++;
            }
        }

        var avgReturn = count > 0 ? totalReturn / count : 0;
        return baseValue * (1 + avgReturn);
    }

    /// <summary>
    /// Get benchmark data normalized to match portfolio dates.
    /// </summary>
    private async Task<List<PortfolioDataPoint>?> GetBenchmarkDataAsync(
        string benchmarkTicker,
        string period,
        List<PortfolioDataPoint> portfolioData)
    {
        try
        {
            var benchmarkHistory = await _stockDataService.GetHistoricalDataAsync(benchmarkTicker, period);
            if (benchmarkHistory == null || benchmarkHistory.Data.Count == 0)
            {
                return null;
            }

            var portfolioDates = portfolioData.Select(p => p.Date.Date).ToHashSet();
            var benchmarkDataPoints = benchmarkHistory.Data
                .Where(d => portfolioDates.Contains(d.Date.Date))
                .OrderBy(d => d.Date)
                .ToList();

            if (benchmarkDataPoints.Count == 0) return null;

            var basePrice = benchmarkDataPoints.First().Close;
            return benchmarkDataPoints.Select(d => new PortfolioDataPoint
            {
                Date = d.Date.Date,
                PortfolioValue = d.Close,
                PercentChange = basePrice > 0
                    ? Math.Round(((d.Close - basePrice) / basePrice) * 100, 2)
                    : 0
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch benchmark data for {Ticker}", LogSanitizer.Sanitize(benchmarkTicker));
            return null;
        }
    }
}

/// <summary>
/// Quote information for a single ticker.
/// </summary>
public record TickerQuote
{
    public required string Symbol { get; init; }
    public string? Name { get; init; }
    public decimal? Price { get; init; }
    public decimal? Change { get; init; }
    public decimal? ChangePercent { get; init; }
    public string? Error { get; init; }
}

/// <summary>
/// Quotes for all tickers in a watchlist.
/// </summary>
public record WatchlistQuotes
{
    public required string WatchlistId { get; init; }
    public required string WatchlistName { get; init; }
    public required List<TickerQuote> Quotes { get; init; }
    public DateTime FetchedAt { get; init; }
}
