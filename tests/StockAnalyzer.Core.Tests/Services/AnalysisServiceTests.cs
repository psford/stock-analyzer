using FluentAssertions;
using Moq;
using StockAnalyzer.Core.Models;
using StockAnalyzer.Core.Services;
using StockAnalyzer.Core.Tests.TestHelpers;
using Xunit;

namespace StockAnalyzer.Core.Tests.Services;

public class AnalysisServiceTests
{
    private readonly AnalysisService _sut;

    public AnalysisServiceTests()
    {
        // System under test - no news service dependency for pure calculation tests
        _sut = new AnalysisService(newsService: null);
    }

    #region CalculateMovingAverages Tests

    [Fact]
    public void CalculateMovingAverages_WithSufficientData_ReturnsCorrectSma20()
    {
        // Arrange - Create 25 data points with predictable values
        var data = new List<OhlcvData>();
        for (int i = 0; i < 25; i++)
        {
            data.Add(TestDataFactory.CreateOhlcvData(
                date: DateTime.Today.AddDays(-25 + i),
                open: 100m,
                high: 105m,
                low: 95m,
                close: 100m + i,  // Close prices: 100, 101, 102, ... 124
                volume: 1000000
            ));
        }

        // Act
        var result = _sut.CalculateMovingAverages(data);

        // Assert
        result.Should().HaveCount(25);

        // First 19 points should have null SMA20 (not enough data)
        for (int i = 0; i < 19; i++)
        {
            result[i].Sma20.Should().BeNull($"index {i} should not have enough data for SMA20");
        }

        // Point at index 19 (20th point) should have SMA20
        // SMA20 at index 19 = average of closes[0..19] = average of 100..119 = 109.5
        result[19].Sma20.Should().Be(109.5m);

        // Point at index 24 should have SMA20
        // SMA20 at index 24 = average of closes[5..24] = average of 105..124 = 114.5
        result[24].Sma20.Should().Be(114.5m);
    }

    [Fact]
    public void CalculateMovingAverages_WithInsufficientData_ReturnsNullForMissingSmas()
    {
        // Arrange - Only 15 data points (not enough for SMA20, SMA50, or SMA200)
        var data = TestDataFactory.CreateOhlcvDataList(15);

        // Act
        var result = _sut.CalculateMovingAverages(data);

        // Assert
        result.Should().HaveCount(15);

        // All SMA values should be null since we don't have enough data
        result.Should().AllSatisfy(ma =>
        {
            ma.Sma20.Should().BeNull();
            ma.Sma50.Should().BeNull();
            ma.Sma200.Should().BeNull();
        });
    }

    [Fact]
    public void CalculateMovingAverages_WithEmptyData_ReturnsEmptyList()
    {
        // Arrange
        var data = new List<OhlcvData>();

        // Act
        var result = _sut.CalculateMovingAverages(data);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void CalculateMovingAverages_ReturnsDatesMatchingInput()
    {
        // Arrange
        var data = TestDataFactory.CreateOhlcvDataList(30);

        // Act
        var result = _sut.CalculateMovingAverages(data);

        // Assert
        result.Should().HaveCount(30);
        for (int i = 0; i < 30; i++)
        {
            result[i].Date.Should().Be(data[i].Date);
        }
    }

    #endregion

    #region DetectSignificantMovesAsync Tests

    [Fact]
    public async Task DetectSignificantMoves_WithLargeMove_DetectsMove()
    {
        // Arrange - Create data with a 6% move on day 5
        var data = TestDataFactory.CreateOhlcvDataWithSignificantMove(
            count: 10,
            significantMoveDay: 5,
            percentChange: 6.0m
        );

        // Act
        var result = await _sut.DetectSignificantMovesAsync("TEST", data, threshold: 5.0m, includeNews: false);

        // Assert
        result.Symbol.Should().Be("TEST");
        result.Threshold.Should().Be(5.0m);
        result.Moves.Should().HaveCount(1);
        result.Moves[0].PercentChange.Should().BeApproximately(6.0m, 0.5m);
    }

    [Fact]
    public async Task DetectSignificantMoves_WithSmallMove_IgnoresMove()
    {
        // Arrange - Create data with only a 2% move (below 3% default threshold)
        var data = TestDataFactory.CreateOhlcvDataWithSignificantMove(
            count: 10,
            significantMoveDay: 5,
            percentChange: 2.0m
        );

        // Act
        var result = await _sut.DetectSignificantMovesAsync("TEST", data, threshold: 3.0m, includeNews: false);

        // Assert
        result.Moves.Should().BeEmpty("2% move is below 3% threshold");
    }

    [Fact]
    public async Task DetectSignificantMoves_WithCustomThreshold_UsesThreshold()
    {
        // Arrange - Create data with a 7% move
        var data = TestDataFactory.CreateOhlcvDataWithSignificantMove(
            count: 10,
            significantMoveDay: 5,
            percentChange: 7.0m
        );

        // Act - Use 10% threshold
        var result = await _sut.DetectSignificantMovesAsync("TEST", data, threshold: 10.0m, includeNews: false);

        // Assert - 7% move should NOT be detected with 10% threshold
        result.Moves.Should().BeEmpty("7% move is below 10% threshold");
    }

    [Fact]
    public async Task DetectSignificantMoves_CorrectlyIdentifiesDirection()
    {
        // Arrange - Create data with both positive and negative moves
        var data = new List<OhlcvData>
        {
            TestDataFactory.CreateOhlcvData(DateTime.Today.AddDays(-2), 100m, 110m, 95m, 106m), // +6%
            TestDataFactory.CreateOhlcvData(DateTime.Today.AddDays(-1), 100m, 105m, 90m, 94m),  // -6%
            TestDataFactory.CreateOhlcvData(DateTime.Today, 100m, 102m, 98m, 101m)              // +1% (below threshold)
        };

        // Act
        var result = await _sut.DetectSignificantMovesAsync("TEST", data, threshold: 5.0m, includeNews: false);

        // Assert
        result.Moves.Should().HaveCount(2);

        var positiveMove = result.Moves.First(m => m.IsPositive);
        positiveMove.Direction.Should().Be("up");
        positiveMove.PercentChange.Should().BePositive();

        var negativeMove = result.Moves.First(m => !m.IsPositive);
        negativeMove.Direction.Should().Be("down");
        negativeMove.PercentChange.Should().BeNegative();
    }

    [Fact]
    public async Task DetectSignificantMoves_WithZeroOpenPrice_SkipsDay()
    {
        // Arrange - Include a day with zero open price
        var data = new List<OhlcvData>
        {
            TestDataFactory.CreateOhlcvData(DateTime.Today.AddDays(-1), 0m, 105m, 95m, 100m),  // Zero open - should skip
            TestDataFactory.CreateOhlcvData(DateTime.Today, 100m, 110m, 95m, 108m)             // Valid +8%
        };

        // Act
        var result = await _sut.DetectSignificantMovesAsync("TEST", data, threshold: 5.0m, includeNews: false);

        // Assert - Only the valid day should be detected
        result.Moves.Should().HaveCount(1);
    }

    [Fact]
    public async Task DetectSignificantMoves_OrdersResultsByDateDescending()
    {
        // Arrange
        var data = new List<OhlcvData>
        {
            TestDataFactory.CreateOhlcvData(DateTime.Today.AddDays(-3), 100m, 110m, 95m, 107m), // +7%
            TestDataFactory.CreateOhlcvData(DateTime.Today.AddDays(-2), 100m, 110m, 95m, 106m), // +6%
            TestDataFactory.CreateOhlcvData(DateTime.Today.AddDays(-1), 100m, 110m, 95m, 108m), // +8%
        };

        // Act
        var result = await _sut.DetectSignificantMovesAsync("TEST", data, threshold: 5.0m, includeNews: false);

        // Assert - Results should be ordered by date descending (most recent first)
        result.Moves.Should().BeInDescendingOrder(m => m.Date);
    }

    #endregion

    #region CalculatePerformance Tests

    [Fact]
    public void CalculatePerformance_ReturnsCorrectTotalReturn()
    {
        // Arrange - First close: 100, Last close: 150 = 50% return
        var data = new List<OhlcvData>
        {
            TestDataFactory.CreateOhlcvData(DateTime.Today.AddDays(-2), 95m, 105m, 90m, 100m),
            TestDataFactory.CreateOhlcvData(DateTime.Today.AddDays(-1), 100m, 120m, 95m, 120m),
            TestDataFactory.CreateOhlcvData(DateTime.Today, 120m, 155m, 115m, 150m)
        };

        // Act
        var result = _sut.CalculatePerformance(data);

        // Assert
        result["totalReturn"].Should().Be(50m); // (150 - 100) / 100 * 100 = 50%
    }

    [Fact]
    public void CalculatePerformance_ReturnsCorrectHighAndLow()
    {
        // Arrange
        var data = new List<OhlcvData>
        {
            TestDataFactory.CreateOhlcvData(DateTime.Today.AddDays(-2), 95m, 105m, 90m, 100m),
            TestDataFactory.CreateOhlcvData(DateTime.Today.AddDays(-1), 100m, 120m, 95m, 80m),  // Lowest
            TestDataFactory.CreateOhlcvData(DateTime.Today, 80m, 155m, 75m, 200m)               // Highest
        };

        // Act
        var result = _sut.CalculatePerformance(data);

        // Assert
        result["highestClose"].Should().Be(200m);
        result["lowestClose"].Should().Be(80m);
    }

    [Fact]
    public void CalculatePerformance_ReturnsAverageVolume()
    {
        // Arrange
        var data = new List<OhlcvData>
        {
            TestDataFactory.CreateOhlcvData(DateTime.Today.AddDays(-2), 100m, 105m, 95m, 100m, volume: 1000000),
            TestDataFactory.CreateOhlcvData(DateTime.Today.AddDays(-1), 100m, 105m, 95m, 100m, volume: 2000000),
            TestDataFactory.CreateOhlcvData(DateTime.Today, 100m, 105m, 95m, 100m, volume: 3000000)
        };

        // Act
        var result = _sut.CalculatePerformance(data);

        // Assert
        result["averageVolume"].Should().Be(2000000m); // (1M + 2M + 3M) / 3 = 2M
    }

    [Fact]
    public void CalculatePerformance_WithEmptyData_ReturnsEmptyDictionary()
    {
        // Arrange
        var data = new List<OhlcvData>();

        // Act
        var result = _sut.CalculatePerformance(data);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void CalculatePerformance_WithSingleDataPoint_ReturnsEmptyDictionary()
    {
        // Arrange
        var data = new List<OhlcvData>
        {
            TestDataFactory.CreateOhlcvData(DateTime.Today, 100m, 105m, 95m, 100m)
        };

        // Act
        var result = _sut.CalculatePerformance(data);

        // Assert
        result.Should().BeEmpty("need at least 2 data points");
    }

    [Fact]
    public void CalculatePerformance_ReturnsVolatility()
    {
        // Arrange - Create data with known price movements
        var data = TestDataFactory.CreateOhlcvDataList(30);

        // Act
        var result = _sut.CalculatePerformance(data);

        // Assert
        result.Should().ContainKey("volatility");
        result["volatility"].Should().NotBeNull();
        result["volatility"].Should().BeGreaterOrEqualTo(0);
    }

    #endregion

    #region CalculateRsi Tests

    [Fact]
    public void CalculateRsi_WithSufficientData_ReturnsValidRsiValues()
    {
        // Arrange - Create 30 data points (more than 14 period)
        var data = TestDataFactory.CreateOhlcvDataList(30);

        // Act
        var result = _sut.CalculateRsi(data);

        // Assert
        result.Should().HaveCount(30);

        // First 14 values should be null (not enough data)
        for (int i = 0; i < 14; i++)
        {
            result[i].Rsi.Should().BeNull($"index {i} should not have enough data for RSI");
        }

        // Values after index 14 should have RSI values
        for (int i = 14; i < result.Count; i++)
        {
            result[i].Rsi.Should().NotBeNull($"index {i} should have RSI value");
        }
    }

    [Fact]
    public void CalculateRsi_WithInsufficientData_ReturnsAllNulls()
    {
        // Arrange - Only 10 data points (less than 14 + 1 period)
        var data = TestDataFactory.CreateOhlcvDataList(10);

        // Act
        var result = _sut.CalculateRsi(data);

        // Assert
        result.Should().HaveCount(10);
        result.Should().AllSatisfy(r => r.Rsi.Should().BeNull());
    }

    [Fact]
    public void CalculateRsi_ValuesAreInValidRange()
    {
        // Arrange - Create data with various price movements
        var data = new List<OhlcvData>();
        var basePrice = 100m;

        // Create 30 days with alternating gains and losses
        for (int i = 0; i < 30; i++)
        {
            var change = i % 2 == 0 ? 2m : -1m; // Alternating +2, -1
            var close = basePrice + (i * 0.5m) + change;
            data.Add(TestDataFactory.CreateOhlcvData(
                date: DateTime.Today.AddDays(-30 + i),
                open: basePrice + (i * 0.5m),
                high: close + 1,
                low: close - 1,
                close: close
            ));
        }

        // Act
        var result = _sut.CalculateRsi(data);

        // Assert - All non-null RSI values should be between 0 and 100
        var validRsiValues = result.Where(r => r.Rsi.HasValue).Select(r => r.Rsi!.Value);
        validRsiValues.Should().AllSatisfy(rsi =>
        {
            rsi.Should().BeGreaterOrEqualTo(0);
            rsi.Should().BeLessOrEqualTo(100);
        });
    }

    [Fact]
    public void CalculateRsi_WithAllGains_Returns100()
    {
        // Arrange - Create data with only gains (price increases every day)
        var data = new List<OhlcvData>();
        for (int i = 0; i < 20; i++)
        {
            var close = 100m + (i * 2m); // Increase by 2 each day
            data.Add(TestDataFactory.CreateOhlcvData(
                date: DateTime.Today.AddDays(-20 + i),
                open: close - 1,
                high: close + 1,
                low: close - 2,
                close: close
            ));
        }

        // Act
        var result = _sut.CalculateRsi(data);

        // Assert - RSI should be 100 when there are only gains (no losses)
        var lastRsi = result.Last().Rsi;
        lastRsi.Should().NotBeNull();
        lastRsi.Should().Be(100m, "RSI with all gains and no losses should be 100");
    }

    [Fact]
    public void CalculateRsi_DatesMatchInput()
    {
        // Arrange
        var data = TestDataFactory.CreateOhlcvDataList(20);

        // Act
        var result = _sut.CalculateRsi(data);

        // Assert
        result.Should().HaveCount(20);
        for (int i = 0; i < 20; i++)
        {
            result[i].Date.Should().Be(data[i].Date);
        }
    }

    [Fact]
    public void CalculateRsi_WithEmptyData_ReturnsEmptyList()
    {
        // Arrange
        var data = new List<OhlcvData>();

        // Act
        var result = _sut.CalculateRsi(data);

        // Assert
        result.Should().BeEmpty();
    }

    #endregion

    #region CalculateMacd Tests

    [Fact]
    public void CalculateMacd_WithSufficientData_ReturnsAllComponents()
    {
        // Arrange - Create 50 data points (enough for 26-period slow EMA + 9-period signal)
        var data = TestDataFactory.CreateOhlcvDataList(50);

        // Act
        var result = _sut.CalculateMacd(data);

        // Assert
        result.Should().HaveCount(50);

        // After enough periods, all components should be present
        var lastData = result.Last();
        lastData.MacdLine.Should().NotBeNull("MACD line should be available at end");
        lastData.SignalLine.Should().NotBeNull("Signal line should be available at end");
        lastData.Histogram.Should().NotBeNull("Histogram should be available at end");
    }

    [Fact]
    public void CalculateMacd_HistogramEqualsLineMinusSignal()
    {
        // Arrange
        var data = TestDataFactory.CreateOhlcvDataList(50);

        // Act
        var result = _sut.CalculateMacd(data);

        // Assert - For all points with complete data, histogram = MACD - Signal
        var completeData = result.Where(m =>
            m.MacdLine.HasValue && m.SignalLine.HasValue && m.Histogram.HasValue);

        completeData.Should().AllSatisfy(m =>
        {
            var expectedHistogram = m.MacdLine!.Value - m.SignalLine!.Value;
            m.Histogram.Should().BeApproximately(expectedHistogram, 0.0001m,
                "Histogram should equal MACD line minus Signal line");
        });
    }

    [Fact]
    public void CalculateMacd_WithInsufficientData_ReturnsNullsForEarly()
    {
        // Arrange - Create 50 data points
        var data = TestDataFactory.CreateOhlcvDataList(50);

        // Act
        var result = _sut.CalculateMacd(data);

        // Assert
        // First 25 values should have null MACD line (need 26 periods for slow EMA)
        for (int i = 0; i < 25; i++)
        {
            result[i].MacdLine.Should().BeNull($"index {i} should not have MACD line (need 26 periods)");
        }

        // Index 25 (26th point) should have MACD line
        result[25].MacdLine.Should().NotBeNull("index 25 should have MACD line");
    }

    [Fact]
    public void CalculateMacd_SignalLineStartsAfterMacdLine()
    {
        // Arrange
        var data = TestDataFactory.CreateOhlcvDataList(50);

        // Act
        var result = _sut.CalculateMacd(data);

        // Assert
        // Signal line needs 26 periods (slow EMA) + 9 periods - 1 = 33 data points to start
        var signalStartIndex = 26 - 1 + 9 - 1; // = 33

        for (int i = 0; i < signalStartIndex; i++)
        {
            result[i].SignalLine.Should().BeNull($"index {i} should not have signal line yet");
        }

        result[signalStartIndex].SignalLine.Should().NotBeNull(
            $"index {signalStartIndex} should have signal line");
    }

    [Fact]
    public void CalculateMacd_DatesMatchInput()
    {
        // Arrange
        var data = TestDataFactory.CreateOhlcvDataList(40);

        // Act
        var result = _sut.CalculateMacd(data);

        // Assert
        result.Should().HaveCount(40);
        for (int i = 0; i < 40; i++)
        {
            result[i].Date.Should().Be(data[i].Date);
        }
    }

    [Fact]
    public void CalculateMacd_WithEmptyData_ReturnsEmptyList()
    {
        // Arrange
        var data = new List<OhlcvData>();

        // Act
        var result = _sut.CalculateMacd(data);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void CalculateMacd_WithTooFewDataPoints_ReturnsAllNulls()
    {
        // Arrange - Only 10 data points (not enough for 12-period fast EMA)
        var data = TestDataFactory.CreateOhlcvDataList(10);

        // Act
        var result = _sut.CalculateMacd(data);

        // Assert
        result.Should().HaveCount(10);
        result.Should().AllSatisfy(m =>
        {
            m.MacdLine.Should().BeNull();
            m.SignalLine.Should().BeNull();
            m.Histogram.Should().BeNull();
        });
    }

    #endregion

    #region CalculateStochastic Tests

    [Fact]
    public void CalculateStochastic_WithSufficientData_ReturnsValidValues()
    {
        // Arrange - Create 30 data points (more than 14 + 3 - 1 = 16 minimum)
        var data = TestDataFactory.CreateOhlcvDataList(30);

        // Act
        var result = _sut.CalculateStochastic(data);

        // Assert
        result.Should().HaveCount(30);

        // First 13 values should have null K (not enough data for 14-period lookback)
        for (int i = 0; i < 13; i++)
        {
            result[i].K.Should().BeNull($"index {i} should not have enough data for %K");
        }

        // Index 13 (14th point) should have K value but null D
        result[13].K.Should().NotBeNull("index 13 should have %K value");
        result[13].D.Should().BeNull("index 13 should not have %D value yet");

        // Index 15 (16th point) should have both K and D values (14 + 3 - 1 = 16 minimum for D)
        result[15].K.Should().NotBeNull("index 15 should have %K value");
        result[15].D.Should().NotBeNull("index 15 should have %D value");
    }

    [Fact]
    public void CalculateStochastic_WithInsufficientData_ReturnsAllNulls()
    {
        // Arrange - Only 10 data points (less than 14 period)
        var data = TestDataFactory.CreateOhlcvDataList(10);

        // Act
        var result = _sut.CalculateStochastic(data);

        // Assert
        result.Should().HaveCount(10);
        result.Should().AllSatisfy(s =>
        {
            s.K.Should().BeNull();
            s.D.Should().BeNull();
        });
    }

    [Fact]
    public void CalculateStochastic_ValuesAreInValidRange()
    {
        // Arrange - Create data with various price movements
        var data = new List<OhlcvData>();
        var basePrice = 100m;

        // Create 30 days with varying prices
        for (int i = 0; i < 30; i++)
        {
            var variance = (i % 5) * 2m - 4m; // -4, -2, 0, +2, +4 pattern
            var close = basePrice + variance;
            data.Add(TestDataFactory.CreateOhlcvData(
                date: DateTime.Today.AddDays(-30 + i),
                open: basePrice,
                high: basePrice + 5m,
                low: basePrice - 5m,
                close: close
            ));
        }

        // Act
        var result = _sut.CalculateStochastic(data);

        // Assert - All non-null K and D values should be between 0 and 100
        var validKValues = result.Where(r => r.K.HasValue).Select(r => r.K!.Value);
        validKValues.Should().AllSatisfy(k =>
        {
            k.Should().BeGreaterOrEqualTo(0);
            k.Should().BeLessOrEqualTo(100);
        });

        var validDValues = result.Where(r => r.D.HasValue).Select(r => r.D!.Value);
        validDValues.Should().AllSatisfy(d =>
        {
            d.Should().BeGreaterOrEqualTo(0);
            d.Should().BeLessOrEqualTo(100);
        });
    }

    [Fact]
    public void CalculateStochastic_WhenCloseEqualsHigh_KIsHundred()
    {
        // Arrange - Create data where close always equals high (strongest possible)
        var data = new List<OhlcvData>();
        for (int i = 0; i < 20; i++)
        {
            data.Add(TestDataFactory.CreateOhlcvData(
                date: DateTime.Today.AddDays(-20 + i),
                open: 100m,
                high: 110m,
                low: 90m,
                close: 110m  // Close equals high
            ));
        }

        // Act
        var result = _sut.CalculateStochastic(data);

        // Assert - K should be 100 when close equals the highest high in the lookback period
        var kValues = result.Where(r => r.K.HasValue).Select(r => r.K!.Value).ToList();
        kValues.Should().NotBeEmpty();
        kValues.Last().Should().Be(100m, "%K should be 100 when close equals highest high");
    }

    [Fact]
    public void CalculateStochastic_WhenCloseEqualsLow_KIsZero()
    {
        // Arrange - Create data where close always equals low (weakest possible)
        var data = new List<OhlcvData>();
        for (int i = 0; i < 20; i++)
        {
            data.Add(TestDataFactory.CreateOhlcvData(
                date: DateTime.Today.AddDays(-20 + i),
                open: 100m,
                high: 110m,
                low: 90m,
                close: 90m  // Close equals low
            ));
        }

        // Act
        var result = _sut.CalculateStochastic(data);

        // Assert - K should be 0 when close equals the lowest low in the lookback period
        var kValues = result.Where(r => r.K.HasValue).Select(r => r.K!.Value).ToList();
        kValues.Should().NotBeEmpty();
        kValues.Last().Should().Be(0m, "%K should be 0 when close equals lowest low");
    }

    [Fact]
    public void CalculateStochastic_DatesMatchInput()
    {
        // Arrange
        var data = TestDataFactory.CreateOhlcvDataList(20);

        // Act
        var result = _sut.CalculateStochastic(data);

        // Assert
        result.Should().HaveCount(20);
        for (int i = 0; i < 20; i++)
        {
            result[i].Date.Should().Be(data[i].Date);
        }
    }

    #endregion
}
