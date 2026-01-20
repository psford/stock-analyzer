using FluentAssertions;
using StockAnalyzer.Core.Models;
using StockAnalyzer.Core.Tests.TestHelpers;
using Xunit;

namespace StockAnalyzer.Core.Tests.Models;

/// <summary>
/// Tests for calculated properties on model records.
/// </summary>
public class ModelCalculationTests
{
    #region HistoricalDataResult Tests

    [Fact]
    public void HistoricalDataResult_MinClose_ReturnsMinimum()
    {
        // Arrange
        var data = new List<OhlcvData>
        {
            TestDataFactory.CreateOhlcvData(DateTime.Today.AddDays(-2), 100m, 110m, 95m, 100m),
            TestDataFactory.CreateOhlcvData(DateTime.Today.AddDays(-1), 100m, 110m, 95m, 75m),  // Lowest
            TestDataFactory.CreateOhlcvData(DateTime.Today, 100m, 110m, 95m, 150m)
        };

        var result = TestDataFactory.CreateHistoricalDataResult(data: data);

        // Act & Assert
        result.MinClose.Should().Be(75m);
    }

    [Fact]
    public void HistoricalDataResult_MaxClose_ReturnsMaximum()
    {
        // Arrange
        var data = new List<OhlcvData>
        {
            TestDataFactory.CreateOhlcvData(DateTime.Today.AddDays(-2), 100m, 110m, 95m, 100m),
            TestDataFactory.CreateOhlcvData(DateTime.Today.AddDays(-1), 100m, 110m, 95m, 200m),  // Highest
            TestDataFactory.CreateOhlcvData(DateTime.Today, 100m, 110m, 95m, 150m)
        };

        var result = TestDataFactory.CreateHistoricalDataResult(data: data);

        // Act & Assert
        result.MaxClose.Should().Be(200m);
    }

    [Fact]
    public void HistoricalDataResult_AverageClose_ReturnsAverage()
    {
        // Arrange - Closes: 100, 200, 300 -> Average = 200
        var data = new List<OhlcvData>
        {
            TestDataFactory.CreateOhlcvData(DateTime.Today.AddDays(-2), 100m, 110m, 95m, 100m),
            TestDataFactory.CreateOhlcvData(DateTime.Today.AddDays(-1), 100m, 210m, 95m, 200m),
            TestDataFactory.CreateOhlcvData(DateTime.Today, 100m, 310m, 95m, 300m)
        };

        var result = TestDataFactory.CreateHistoricalDataResult(data: data);

        // Act & Assert
        result.AverageClose.Should().Be(200m);
    }

    [Fact]
    public void HistoricalDataResult_AverageVolume_ReturnsAverage()
    {
        // Arrange - Volumes: 1M, 2M, 3M -> Average = 2M
        var data = new List<OhlcvData>
        {
            TestDataFactory.CreateOhlcvData(DateTime.Today.AddDays(-2), 100m, 110m, 95m, 100m, volume: 1000000),
            TestDataFactory.CreateOhlcvData(DateTime.Today.AddDays(-1), 100m, 110m, 95m, 100m, volume: 2000000),
            TestDataFactory.CreateOhlcvData(DateTime.Today, 100m, 110m, 95m, 100m, volume: 3000000)
        };

        var result = TestDataFactory.CreateHistoricalDataResult(data: data);

        // Act & Assert
        result.AverageVolume.Should().Be(2000000);
    }

    [Fact]
    public void HistoricalDataResult_WithEmptyData_ReturnsNulls()
    {
        // Arrange
        var result = new HistoricalDataResult
        {
            Symbol = "TEST",
            Period = "1y",
            StartDate = DateTime.Today,
            EndDate = DateTime.Today,
            Data = new List<OhlcvData>()
        };

        // Act & Assert
        result.MinClose.Should().BeNull();
        result.MaxClose.Should().BeNull();
        result.AverageClose.Should().BeNull();
        result.AverageVolume.Should().BeNull();
    }

    #endregion

    #region SignificantMove Tests

    [Fact]
    public void SignificantMove_IsPositive_TrueForPositiveChange()
    {
        // Arrange
        var move = TestDataFactory.CreateSignificantMove(percentChange: 5.5m);

        // Act & Assert
        move.IsPositive.Should().BeTrue();
    }

    [Fact]
    public void SignificantMove_IsPositive_FalseForNegativeChange()
    {
        // Arrange
        var move = TestDataFactory.CreateSignificantMove(percentChange: -5.5m);

        // Act & Assert
        move.IsPositive.Should().BeFalse();
    }

    [Fact]
    public void SignificantMove_Direction_ReturnsUpForPositive()
    {
        // Arrange
        var move = TestDataFactory.CreateSignificantMove(percentChange: 5.5m);

        // Act & Assert
        move.Direction.Should().Be("up");
    }

    [Fact]
    public void SignificantMove_Direction_ReturnsDownForNegative()
    {
        // Arrange
        var move = TestDataFactory.CreateSignificantMove(percentChange: -5.5m);

        // Act & Assert
        move.Direction.Should().Be("down");
    }

    [Theory]
    [InlineData(2.5, "notable")]      // < 3%
    [InlineData(3.5, "significant")]  // 3-5%
    [InlineData(6.0, "major")]        // 5-10%
    [InlineData(12.0, "extreme")]     // >= 10%
    public void SignificantMove_Magnitude_ReturnsCorrectClassification(decimal percentChange, string expectedMagnitude)
    {
        // Arrange
        var move = TestDataFactory.CreateSignificantMove(percentChange: percentChange);

        // Act & Assert
        move.Magnitude.Should().Be(expectedMagnitude);
    }

    [Fact]
    public void SignificantMove_Magnitude_UsesAbsoluteValue()
    {
        // Arrange - negative 12% should still be "extreme"
        var move = TestDataFactory.CreateSignificantMove(percentChange: -12.0m);

        // Act & Assert
        move.Magnitude.Should().Be("extreme");
    }

    #endregion

    #region SignificantMovesResult Tests

    [Fact]
    public void SignificantMovesResult_PositiveMoves_CountsCorrectly()
    {
        // Arrange
        var moves = new List<SignificantMove>
        {
            TestDataFactory.CreateSignificantMove(5.0m),   // Positive
            TestDataFactory.CreateSignificantMove(-4.0m),  // Negative
            TestDataFactory.CreateSignificantMove(3.0m),   // Positive
            TestDataFactory.CreateSignificantMove(-6.0m)   // Negative
        };

        var result = TestDataFactory.CreateSignificantMovesResult(moves: moves);

        // Act & Assert
        result.PositiveMoves.Should().Be(2);
    }

    [Fact]
    public void SignificantMovesResult_NegativeMoves_CountsCorrectly()
    {
        // Arrange
        var moves = new List<SignificantMove>
        {
            TestDataFactory.CreateSignificantMove(5.0m),   // Positive
            TestDataFactory.CreateSignificantMove(-4.0m),  // Negative
            TestDataFactory.CreateSignificantMove(3.0m),   // Positive
            TestDataFactory.CreateSignificantMove(-6.0m)   // Negative
        };

        var result = TestDataFactory.CreateSignificantMovesResult(moves: moves);

        // Act & Assert
        result.NegativeMoves.Should().Be(2);
    }

    [Fact]
    public void SignificantMovesResult_LargestGain_ReturnsMaxPositive()
    {
        // Arrange
        var moves = new List<SignificantMove>
        {
            TestDataFactory.CreateSignificantMove(5.0m),
            TestDataFactory.CreateSignificantMove(8.5m),   // Largest gain
            TestDataFactory.CreateSignificantMove(-6.0m),
            TestDataFactory.CreateSignificantMove(3.0m)
        };

        var result = TestDataFactory.CreateSignificantMovesResult(moves: moves);

        // Act & Assert
        result.LargestGain.Should().Be(8.5m);
    }

    [Fact]
    public void SignificantMovesResult_LargestLoss_ReturnsMinNegative()
    {
        // Arrange
        var moves = new List<SignificantMove>
        {
            TestDataFactory.CreateSignificantMove(5.0m),
            TestDataFactory.CreateSignificantMove(-4.0m),
            TestDataFactory.CreateSignificantMove(-9.5m),  // Largest loss (most negative)
            TestDataFactory.CreateSignificantMove(-2.0m)
        };

        var result = TestDataFactory.CreateSignificantMovesResult(moves: moves);

        // Act & Assert
        result.LargestLoss.Should().Be(-9.5m);
    }

    [Fact]
    public void SignificantMovesResult_LargestGain_WithNoPositiveMoves_ReturnsNull()
    {
        // Arrange
        var moves = new List<SignificantMove>
        {
            TestDataFactory.CreateSignificantMove(-4.0m),
            TestDataFactory.CreateSignificantMove(-6.0m)
        };

        var result = TestDataFactory.CreateSignificantMovesResult(moves: moves);

        // Act & Assert
        result.LargestGain.Should().BeNull();
    }

    [Fact]
    public void SignificantMovesResult_LargestLoss_WithNoNegativeMoves_ReturnsNull()
    {
        // Arrange
        var moves = new List<SignificantMove>
        {
            TestDataFactory.CreateSignificantMove(4.0m),
            TestDataFactory.CreateSignificantMove(6.0m)
        };

        var result = TestDataFactory.CreateSignificantMovesResult(moves: moves);

        // Act & Assert
        result.LargestLoss.Should().BeNull();
    }

    [Fact]
    public void SignificantMovesResult_WithEmptyMoves_HandlesGracefully()
    {
        // Arrange
        var result = TestDataFactory.CreateSignificantMovesResult(moves: new List<SignificantMove>());

        // Act & Assert
        result.PositiveMoves.Should().Be(0);
        result.NegativeMoves.Should().Be(0);
        result.LargestGain.Should().BeNull();
        result.LargestLoss.Should().BeNull();
    }

    #endregion

    #region NewsResult Tests

    [Fact]
    public void NewsResult_TotalCount_ReturnsArticleCount()
    {
        // Arrange
        var articles = TestDataFactory.CreateNewsItemList(7);
        var result = TestDataFactory.CreateNewsResult(articles: articles);

        // Act & Assert
        result.TotalCount.Should().Be(7);
    }

    [Fact]
    public void NewsResult_TotalCount_WithEmptyArticles_ReturnsZero()
    {
        // Arrange
        var result = TestDataFactory.CreateNewsResult(articles: new List<NewsItem>());

        // Act & Assert
        result.TotalCount.Should().Be(0);
    }

    #endregion
}
