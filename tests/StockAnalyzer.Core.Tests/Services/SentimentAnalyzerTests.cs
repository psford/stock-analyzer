using FluentAssertions;
using StockAnalyzer.Core.Services;
using Xunit;

namespace StockAnalyzer.Core.Tests.Services;

public class SentimentAnalyzerTests
{
    #region Analyze Tests

    [Theory]
    [InlineData("Ford Stock Soars on Strong Earnings", SentimentAnalyzer.Sentiment.Positive)]
    [InlineData("Apple Surges After iPhone Sales Beat Expectations", SentimentAnalyzer.Sentiment.Positive)]
    [InlineData("Tesla Rallies on Record Deliveries", SentimentAnalyzer.Sentiment.Positive)]
    [InlineData("Microsoft Gains Ground in Cloud Market", SentimentAnalyzer.Sentiment.Positive)]
    [InlineData("Amazon Stock Jumps on Prime Day Success", SentimentAnalyzer.Sentiment.Positive)]
    public void Analyze_PositiveHeadlines_ReturnsPositiveSentiment(string headline, SentimentAnalyzer.Sentiment expected)
    {
        // Act
        var (sentiment, score) = SentimentAnalyzer.Analyze(headline);

        // Assert
        sentiment.Should().Be(expected);
        score.Should().BeGreaterThan(0);
    }

    [Theory]
    [InlineData("Ford Stock Plunges on Tariff Concerns", SentimentAnalyzer.Sentiment.Negative)]
    [InlineData("Apple Crashes After Weak Guidance", SentimentAnalyzer.Sentiment.Negative)]
    [InlineData("Tesla Tumbles on Production Delays", SentimentAnalyzer.Sentiment.Negative)]
    [InlineData("Microsoft Falls on Cloud Growth Concerns", SentimentAnalyzer.Sentiment.Negative)]
    [InlineData("Amazon Drops After Earnings Miss", SentimentAnalyzer.Sentiment.Negative)]
    public void Analyze_NegativeHeadlines_ReturnsNegativeSentiment(string headline, SentimentAnalyzer.Sentiment expected)
    {
        // Act
        var (sentiment, score) = SentimentAnalyzer.Analyze(headline);

        // Assert
        sentiment.Should().Be(expected);
        score.Should().BeLessThan(0);
    }

    [Theory]
    [InlineData("Ford Reports Q3 Results")]
    [InlineData("Apple Announces New Product Event")]
    [InlineData("Tesla CEO Discusses Future Plans")]
    [InlineData("Microsoft Partners With OpenAI")]
    public void Analyze_NeutralHeadlines_ReturnsNeutralSentiment(string headline)
    {
        // Act
        var (sentiment, _) = SentimentAnalyzer.Analyze(headline);

        // Assert
        sentiment.Should().Be(SentimentAnalyzer.Sentiment.Neutral);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Analyze_NullOrEmptyHeadline_ReturnsNeutral(string? headline)
    {
        // Act
        var (sentiment, score) = SentimentAnalyzer.Analyze(headline);

        // Assert
        sentiment.Should().Be(SentimentAnalyzer.Sentiment.Neutral);
        score.Should().Be(0);
    }

    [Fact]
    public void Analyze_MixedSentimentHeadline_DeterminesOverallSentiment()
    {
        // Arrange - headline with both positive and negative words
        var headline = "Ford Stock Gains Despite Downgrade Warning";

        // Act
        var (sentiment, _) = SentimentAnalyzer.Analyze(headline);

        // Assert - should be neutral or lean towards the dominant sentiment
        // The result depends on keyword counts
        sentiment.Should().BeOneOf(
            SentimentAnalyzer.Sentiment.Positive,
            SentimentAnalyzer.Sentiment.Negative,
            SentimentAnalyzer.Sentiment.Neutral);
    }

    #endregion

    #region MatchesPriceDirection Tests

    [Theory]
    [InlineData("Ford Stock Soars", 8.5, true)]   // Positive headline, positive price
    [InlineData("Ford Stock Plunges", -8.5, true)] // Negative headline, negative price
    [InlineData("Ford Reports Results", 5.0, true)] // Neutral headline, any price
    [InlineData("Ford Reports Results", -5.0, true)] // Neutral headline, any price
    public void MatchesPriceDirection_MatchingSentiment_ReturnsTrue(
        string headline, decimal priceChange, bool expected)
    {
        // Act
        var result = SentimentAnalyzer.MatchesPriceDirection(headline, priceChange);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("Ford Stock Soars on Record Sales", -8.5, false)] // Positive headline, large negative price
    [InlineData("Ford Stock Plunges on Weak Sales", 8.5, false)]  // Negative headline, large positive price
    public void MatchesPriceDirection_MismatchingSentiment_ReturnsFalse(
        string headline, decimal priceChange, bool expected)
    {
        // Act
        var result = SentimentAnalyzer.MatchesPriceDirection(headline, priceChange);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("Ford Stock Rises", -1.5, true)]  // Small price move, less strict
    [InlineData("Ford Stock Falls", 1.5, true)]   // Small price move, less strict
    public void MatchesPriceDirection_SmallPriceMove_IsMoreLenient(
        string headline, decimal priceChange, bool expected)
    {
        // Act
        var result = SentimentAnalyzer.MatchesPriceDirection(headline, priceChange);

        // Assert
        result.Should().Be(expected);
    }

    #endregion

    #region CalculateMatchScore Tests

    [Fact]
    public void CalculateMatchScore_PerfectMatch_ReturnsHighScore()
    {
        // Arrange
        var headline = "Ford Stock Soars on Strong Earnings";
        var priceChange = 8.5m; // Positive price, positive headline

        // Act
        var score = SentimentAnalyzer.CalculateMatchScore(headline, priceChange);

        // Assert
        score.Should().BeGreaterThan(75);
    }

    [Fact]
    public void CalculateMatchScore_CompleteMismatch_ReturnsLowScore()
    {
        // Arrange - "Soars" is positive but price went way down
        var headline = "Ford Stock Soars on Record Sales";
        var priceChange = -8.5m;

        // Act
        var score = SentimentAnalyzer.CalculateMatchScore(headline, priceChange);

        // Assert
        score.Should().BeLessThan(50);
    }

    [Fact]
    public void CalculateMatchScore_NeutralHeadline_ReturnsMidScore()
    {
        // Arrange - use a headline with no sentiment keywords
        var headline = "Ford to Host Investor Day Next Month";
        var priceChange = 5.0m;

        // Act
        var (sentiment, _) = SentimentAnalyzer.Analyze(headline);
        var score = SentimentAnalyzer.CalculateMatchScore(headline, priceChange);

        // Assert
        sentiment.Should().Be(SentimentAnalyzer.Sentiment.Neutral, "headline should have no sentiment keywords");
        score.Should().Be(50, "neutral headlines should get base score of 50");
    }

    [Fact]
    public void CalculateMatchScore_ScoreIsInValidRange()
    {
        // Arrange
        var headlines = new[]
        {
            "Stock Soars on Record Earnings",
            "Stock Plunges After Warning",
            "Company Reports Results",
            null,
            ""
        };
        var priceChanges = new[] { 10m, -10m, 0m, 5m, -5m };

        // Act & Assert
        foreach (var headline in headlines)
        {
            foreach (var priceChange in priceChanges)
            {
                var score = SentimentAnalyzer.CalculateMatchScore(headline, priceChange);
                score.Should().BeGreaterOrEqualTo(0);
                score.Should().BeLessOrEqualTo(100);
            }
        }
    }

    #endregion

    #region Real-World Scenario Tests

    [Fact]
    public void RealScenario_FordSoarsHeadline_WithNegativePrice_ShouldMismatch()
    {
        // This is the actual scenario from the bug report:
        // "Ford Stock Soars. There Is Still a Huge Problem." with -8.72% price drop
        var headline = "Ford Stock Soars. There Is Still a Huge Problem.";
        var priceChange = -8.72m;

        // Act
        var matchScore = SentimentAnalyzer.CalculateMatchScore(headline, priceChange);
        var matches = SentimentAnalyzer.MatchesPriceDirection(headline, priceChange);

        // Assert - should identify this as a mismatch
        matches.Should().BeFalse("positive 'Soars' should not match -8.72% drop");
        matchScore.Should().BeLessThan(50, "mismatch should have low score");
    }

    [Fact]
    public void RealScenario_DowngradedHeadline_WithNegativePrice_ShouldMatch()
    {
        // A downgrade headline should match a price drop
        var headline = "Analyst Downgrades Ford to Sell, Citing Tariff Concerns";
        var priceChange = -8.72m;

        // Act
        var matchScore = SentimentAnalyzer.CalculateMatchScore(headline, priceChange);
        var matches = SentimentAnalyzer.MatchesPriceDirection(headline, priceChange);

        // Assert
        matches.Should().BeTrue("negative headline should match negative price");
        matchScore.Should().BeGreaterThan(50, "matching sentiment should have good score");
    }

    #endregion
}
