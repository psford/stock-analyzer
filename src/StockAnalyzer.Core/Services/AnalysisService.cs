using StockAnalyzer.Core.Models;

namespace StockAnalyzer.Core.Services;

/// <summary>
/// Service for performing stock analysis calculations.
/// </summary>
public class AnalysisService
{
    private readonly NewsService? _newsService;

    public AnalysisService(NewsService? newsService = null)
    {
        _newsService = newsService;
    }

    /// <summary>
    /// Calculate moving averages for historical data.
    /// </summary>
    public List<MovingAverageData> CalculateMovingAverages(List<OhlcvData> data)
    {
        var result = new List<MovingAverageData>();
        var closes = data.Select(d => d.Close).ToList();

        for (int i = 0; i < data.Count; i++)
        {
            result.Add(new MovingAverageData
            {
                Date = data[i].Date,
                Sma20 = CalculateSma(closes, i, 20),
                Sma50 = CalculateSma(closes, i, 50),
                Sma200 = CalculateSma(closes, i, 200)
            });
        }

        return result;
    }

    /// <summary>
    /// Detect significant price moves.
    /// </summary>
    public async Task<SignificantMovesResult> DetectSignificantMovesAsync(
        string symbol,
        List<OhlcvData> data,
        decimal threshold = 3.0m,
        bool includeNews = true)
    {
        var moves = new List<SignificantMove>();

        foreach (var day in data)
        {
            if (day.Open == 0) continue;

            var percentChange = ((day.Close - day.Open) / day.Open) * 100;

            if (Math.Abs(percentChange) >= threshold)
            {
                var move = new SignificantMove
                {
                    Date = day.Date,
                    OpenPrice = day.Open,
                    ClosePrice = day.Close,
                    PercentChange = percentChange,
                    Volume = day.Volume,
                    RelatedNews = null
                };

                // Fetch related news if requested and service is available
                // Uses sentiment-aware filtering to match news tone with price direction
                if (includeNews && _newsService != null)
                {
                    try
                    {
                        var news = await _newsService.GetNewsForDateWithSentimentAsync(
                            symbol,
                            day.Date,
                            percentChange,
                            maxArticles: 5);
                        move = move with { RelatedNews = news };
                    }
                    catch
                    {
                        // Ignore news errors, continue without news
                    }
                }

                moves.Add(move);
            }
        }

        return new SignificantMovesResult
        {
            Symbol = symbol.ToUpper(),
            Threshold = threshold,
            Moves = moves.OrderByDescending(m => m.Date).ToList()
        };
    }

    /// <summary>
    /// Calculate basic performance metrics.
    /// </summary>
    public Dictionary<string, decimal?> CalculatePerformance(List<OhlcvData> data)
    {
        if (data.Count < 2)
            return new Dictionary<string, decimal?>();

        var first = data.First();
        var last = data.Last();

        var totalReturn = first.Close > 0
            ? ((last.Close - first.Close) / first.Close) * 100
            : 0;

        // Calculate daily returns for volatility
        var dailyReturns = new List<decimal>();
        for (int i = 1; i < data.Count; i++)
        {
            if (data[i - 1].Close > 0)
            {
                var dailyReturn = (data[i].Close - data[i - 1].Close) / data[i - 1].Close;
                dailyReturns.Add(dailyReturn);
            }
        }

        // Calculate volatility (annualized standard deviation)
        var volatility = CalculateVolatility(dailyReturns);

        return new Dictionary<string, decimal?>
        {
            ["totalReturn"] = totalReturn,
            ["volatility"] = volatility,
            ["highestClose"] = data.Max(d => d.Close),
            ["lowestClose"] = data.Min(d => d.Close),
            ["averageVolume"] = (decimal)data.Average(d => d.Volume)
        };
    }

    private static decimal? CalculateSma(List<decimal> values, int index, int period)
    {
        if (index + 1 < period)
            return null;

        var sum = 0m;
        for (int i = index - period + 1; i <= index; i++)
        {
            sum += values[i];
        }
        return sum / period;
    }

    private static decimal? CalculateVolatility(List<decimal> dailyReturns)
    {
        if (dailyReturns.Count < 2)
            return null;

        var mean = dailyReturns.Average();
        var sumSquares = dailyReturns.Sum(r => (r - mean) * (r - mean));
        var variance = sumSquares / (dailyReturns.Count - 1);
        var stdDev = (decimal)Math.Sqrt((double)variance);

        // Annualize (assuming 252 trading days)
        return stdDev * (decimal)Math.Sqrt(252) * 100;
    }

    /// <summary>
    /// Calculate RSI (Relative Strength Index) for historical data.
    /// Uses Wilder's smoothing method with default 14-period.
    /// </summary>
    /// <param name="data">OHLCV data points</param>
    /// <param name="period">RSI period (default 14)</param>
    /// <returns>List of RSI data points</returns>
    public List<RsiData> CalculateRsi(List<OhlcvData> data, int period = 14)
    {
        var result = new List<RsiData>();

        if (data.Count < period + 1)
        {
            // Not enough data - return all nulls
            return data.Select(d => new RsiData { Date = d.Date, Rsi = null }).ToList();
        }

        var closes = data.Select(d => d.Close).ToList();

        // Calculate price changes (gains and losses)
        var gains = new List<decimal>();
        var losses = new List<decimal>();

        for (int i = 1; i < closes.Count; i++)
        {
            var change = closes[i] - closes[i - 1];
            gains.Add(change > 0 ? change : 0);
            losses.Add(change < 0 ? Math.Abs(change) : 0);
        }

        // Initialize with SMA for first RSI value
        decimal avgGain = gains.Take(period).Average();
        decimal avgLoss = losses.Take(period).Average();

        // First (period) entries have null RSI
        for (int i = 0; i < period; i++)
        {
            result.Add(new RsiData { Date = data[i].Date, Rsi = null });
        }

        // Calculate RSI using Wilder's smoothing method
        for (int i = period; i < data.Count; i++)
        {
            if (i > period)
            {
                // Wilder's smoothing: avgGain = (prevAvgGain * (period-1) + currentGain) / period
                avgGain = (avgGain * (period - 1) + gains[i - 1]) / period;
                avgLoss = (avgLoss * (period - 1) + losses[i - 1]) / period;
            }

            decimal rsi;
            if (avgLoss == 0)
            {
                rsi = 100; // No losses means RSI = 100
            }
            else
            {
                var rs = avgGain / avgLoss;
                rsi = 100 - (100 / (1 + rs));
            }

            result.Add(new RsiData { Date = data[i].Date, Rsi = rsi });
        }

        return result;
    }

    /// <summary>
    /// Calculate MACD (Moving Average Convergence Divergence) for historical data.
    /// Uses standard parameters: 12-period fast EMA, 26-period slow EMA, 9-period signal.
    /// </summary>
    /// <param name="data">OHLCV data points</param>
    /// <param name="fastPeriod">Fast EMA period (default 12)</param>
    /// <param name="slowPeriod">Slow EMA period (default 26)</param>
    /// <param name="signalPeriod">Signal line EMA period (default 9)</param>
    /// <returns>List of MACD data points</returns>
    public List<MacdData> CalculateMacd(
        List<OhlcvData> data,
        int fastPeriod = 12,
        int slowPeriod = 26,
        int signalPeriod = 9)
    {
        var result = new List<MacdData>();
        var closes = data.Select(d => d.Close).ToList();

        // Calculate EMAs
        var emaFast = CalculateEma(closes, fastPeriod);
        var emaSlow = CalculateEma(closes, slowPeriod);

        // Calculate MACD line (fast EMA - slow EMA)
        var macdLine = new List<decimal?>();
        for (int i = 0; i < closes.Count; i++)
        {
            if (emaFast[i].HasValue && emaSlow[i].HasValue)
                macdLine.Add(emaFast[i]!.Value - emaSlow[i]!.Value);
            else
                macdLine.Add(null);
        }

        // Calculate signal line (EMA of MACD line)
        // For EMA calculation, we need non-null values, so use 0 for null entries
        var macdValues = macdLine.Select(m => m ?? 0m).ToList();
        var signalLineRaw = CalculateEma(macdValues, signalPeriod);

        // Build result - signal line is only valid after we have valid MACD values
        var firstValidMacdIndex = macdLine.FindIndex(m => m.HasValue);

        for (int i = 0; i < data.Count; i++)
        {
            decimal? signal = null;
            decimal? histogram = null;

            // Signal line is valid after (slowPeriod - 1) + (signalPeriod - 1) periods
            var signalStartIndex = slowPeriod - 1 + signalPeriod - 1;
            if (i >= signalStartIndex && macdLine[i].HasValue && signalLineRaw[i].HasValue)
            {
                signal = signalLineRaw[i];
                histogram = macdLine[i]!.Value - signal!.Value;
            }

            result.Add(new MacdData
            {
                Date = data[i].Date,
                MacdLine = macdLine[i],
                SignalLine = signal,
                Histogram = histogram
            });
        }

        return result;
    }

    /// <summary>
    /// Calculate Exponential Moving Average (EMA).
    /// </summary>
    /// <param name="values">Price values</param>
    /// <param name="period">EMA period</param>
    /// <returns>List of EMA values (null for insufficient data)</returns>
    private static List<decimal?> CalculateEma(List<decimal> values, int period)
    {
        var result = new List<decimal?>();

        if (values.Count < period)
        {
            return values.Select(_ => (decimal?)null).ToList();
        }

        // EMA multiplier: 2 / (period + 1)
        decimal multiplier = 2.0m / (period + 1);

        // Start with SMA for initial EMA value
        decimal ema = values.Take(period).Average();

        for (int i = 0; i < values.Count; i++)
        {
            if (i < period - 1)
            {
                result.Add(null);
            }
            else if (i == period - 1)
            {
                result.Add(ema);
            }
            else
            {
                // EMA = (Current Price - Previous EMA) * Multiplier + Previous EMA
                ema = (values[i] - ema) * multiplier + ema;
                result.Add(ema);
            }
        }

        return result;
    }

    /// <summary>
    /// Calculate Bollinger Bands for historical data.
    /// Bollinger Bands consist of a middle band (SMA) with upper and lower bands
    /// at a specified number of standard deviations.
    /// </summary>
    /// <param name="data">OHLCV data points</param>
    /// <param name="period">SMA period (default 20)</param>
    /// <param name="standardDeviations">Number of standard deviations (default 2)</param>
    /// <returns>List of Bollinger Bands data points</returns>
    public List<BollingerData> CalculateBollingerBands(
        List<OhlcvData> data,
        int period = 20,
        decimal standardDeviations = 2.0m)
    {
        var result = new List<BollingerData>();

        if (data.Count < period)
        {
            // Not enough data - return all nulls
            return data.Select(d => new BollingerData
            {
                Date = d.Date,
                UpperBand = null,
                MiddleBand = null,
                LowerBand = null
            }).ToList();
        }

        var closes = data.Select(d => d.Close).ToList();

        for (int i = 0; i < data.Count; i++)
        {
            if (i < period - 1)
            {
                // Not enough data yet
                result.Add(new BollingerData
                {
                    Date = data[i].Date,
                    UpperBand = null,
                    MiddleBand = null,
                    LowerBand = null
                });
            }
            else
            {
                // Get the window of closes for this period
                var window = closes.Skip(i - period + 1).Take(period).ToList();

                // Calculate SMA (middle band)
                decimal sma = window.Average();

                // Calculate standard deviation
                decimal sumSquaredDiff = window.Sum(v => (v - sma) * (v - sma));
                decimal stdDev = (decimal)Math.Sqrt((double)(sumSquaredDiff / period));

                // Calculate bands
                decimal upperBand = sma + (standardDeviations * stdDev);
                decimal lowerBand = sma - (standardDeviations * stdDev);

                result.Add(new BollingerData
                {
                    Date = data[i].Date,
                    UpperBand = upperBand,
                    MiddleBand = sma,
                    LowerBand = lowerBand
                });
            }
        }

        return result;
    }

    /// <summary>
    /// Calculate Stochastic Oscillator for historical data.
    /// The Stochastic Oscillator compares a stock's closing price to its price range over a period.
    /// %K = 100 × (Close - Lowest Low) / (Highest High - Lowest Low)
    /// %D = SMA of %K
    /// </summary>
    /// <param name="data">OHLCV data points</param>
    /// <param name="kPeriod">%K period (default 14)</param>
    /// <param name="dPeriod">%D period for signal line SMA (default 3)</param>
    /// <returns>List of Stochastic data points</returns>
    public List<StochasticData> CalculateStochastic(
        List<OhlcvData> data,
        int kPeriod = 14,
        int dPeriod = 3)
    {
        var result = new List<StochasticData>();

        // Need at least kPeriod data points for first %K value
        if (data.Count < kPeriod)
        {
            // Not enough data - return all nulls
            return data.Select(d => new StochasticData
            {
                Date = d.Date,
                K = null,
                D = null
            }).ToList();
        }

        // Calculate %K values first
        var kValues = new List<decimal?>();

        for (int i = 0; i < data.Count; i++)
        {
            if (i < kPeriod - 1)
            {
                // Not enough data yet for %K
                kValues.Add(null);
            }
            else
            {
                // Get the window for this period
                var window = data.Skip(i - kPeriod + 1).Take(kPeriod).ToList();

                decimal highestHigh = window.Max(d => d.High);
                decimal lowestLow = window.Min(d => d.Low);
                decimal close = data[i].Close;

                // Avoid division by zero
                decimal k;
                if (highestHigh == lowestLow)
                {
                    // If high and low are equal, %K is 100 if close equals them, otherwise use midpoint
                    k = close == highestHigh ? 100m : 50m;
                }
                else
                {
                    k = 100m * (close - lowestLow) / (highestHigh - lowestLow);
                }

                kValues.Add(k);
            }
        }

        // Calculate %D as SMA of %K
        for (int i = 0; i < data.Count; i++)
        {
            decimal? d = null;

            // %D needs dPeriod valid %K values
            // First valid %D is at index (kPeriod - 1) + (dPeriod - 1) = kPeriod + dPeriod - 2
            if (i >= kPeriod + dPeriod - 2)
            {
                // Get last dPeriod %K values
                var kWindow = kValues.Skip(i - dPeriod + 1).Take(dPeriod).ToList();

                // All values in window should be non-null at this point
                if (kWindow.All(k => k.HasValue))
                {
                    d = kWindow.Average(k => k!.Value);
                }
            }

            result.Add(new StochasticData
            {
                Date = data[i].Date,
                K = kValues[i],
                D = d
            });
        }

        return result;
    }
}
