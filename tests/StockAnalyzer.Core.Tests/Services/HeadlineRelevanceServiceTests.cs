using FluentAssertions;
using StockAnalyzer.Core.Models;
using StockAnalyzer.Core.Services;
using Xunit;

namespace StockAnalyzer.Core.Tests.Services;

public class HeadlineRelevanceServiceTests
{
    private readonly HeadlineRelevanceService _sut = new();

    #region ScoreRelevance Tests

    [Fact]
    public void ScoreRelevance_WithTickerInRelatedSymbols_ReturnsHighScore()
    {
        // Arrange
        var article = CreateNewsItem(
            headline: "Market Update",
            relatedSymbols: new List<string> { "AAPL", "MSFT" });

        // Act
        var score = _sut.ScoreRelevance(article, "AAPL");

        // Assert
        score.Should().BeGreaterThan(0.3m, "ticker in related symbols should contribute high score");
    }

    [Fact]
    public void ScoreRelevance_WithTickerInHeadline_ReturnsHighScore()
    {
        // Arrange
        var article = CreateNewsItem(
            headline: "AAPL Stock Surges After Earnings Beat");

        // Act
        var score = _sut.ScoreRelevance(article, "AAPL");

        // Assert
        score.Should().BeGreaterThan(0.3m, "ticker in headline should contribute high score");
    }

    [Fact]
    public void ScoreRelevance_WithTickerInSummary_ReturnsModerateScore()
    {
        // Arrange
        var article = CreateNewsItem(
            headline: "Tech Stocks Rally",
            summary: "AAPL led the rally with a 5% gain");

        // Act
        var score = _sut.ScoreRelevance(article, "AAPL");

        // Assert
        score.Should().BeGreaterThan(0.2m, "ticker in summary should contribute moderate score");
    }

    [Fact]
    public void ScoreRelevance_WithCompanyNameInHeadline_ReturnsHighScore()
    {
        // Arrange
        var article = CreateNewsItem(
            headline: "Apple Inc Reports Record Revenue");

        // Act
        var score = _sut.ScoreRelevance(article, "AAPL", "Apple Inc");

        // Assert
        score.Should().BeGreaterThan(0.4m, "full company name in headline should boost score");
    }

    [Fact]
    public void ScoreRelevance_WithPartialCompanyNameInHeadline_ReturnsModerateScore()
    {
        // Arrange
        var article = CreateNewsItem(
            headline: "Apple Announces New Product Line");

        // Act
        var score = _sut.ScoreRelevance(article, "AAPL", "Apple Inc");

        // Assert
        score.Should().BeGreaterThan(0.2m, "partial company name match should contribute");
    }

    [Fact]
    public void ScoreRelevance_WithPremiumSource_IncludesSourceBonus()
    {
        // Arrange
        var reutersArticle = CreateNewsItem(
            headline: "Market Update",
            source: "Reuters");

        var unknownArticle = CreateNewsItem(
            headline: "Market Update",
            source: "Unknown Blog");

        // Act
        var reutersScore = _sut.ScoreRelevance(reutersArticle, "AAPL");
        var unknownScore = _sut.ScoreRelevance(unknownArticle, "AAPL");

        // Assert
        reutersScore.Should().BeGreaterThan(unknownScore, "premium sources should score higher");
    }

    [Fact]
    public void ScoreRelevance_WithRecentArticle_ScoresHigherThanOldArticle()
    {
        // Arrange
        var recentArticle = CreateNewsItem(
            headline: "Breaking: AAPL News",
            publishedAt: DateTime.Now.AddHours(-1));

        var oldArticle = CreateNewsItem(
            headline: "Breaking: AAPL News",
            publishedAt: DateTime.Now.AddDays(-5));

        // Act
        var recentScore = _sut.ScoreRelevance(recentArticle, "AAPL");
        var oldScore = _sut.ScoreRelevance(oldArticle, "AAPL");

        // Assert
        recentScore.Should().BeGreaterThan(oldScore, "more recent articles should score higher");
    }

    [Fact]
    public void ScoreRelevance_WithSentimentData_ScoresHigherThanWithout()
    {
        // Arrange
        var withSentiment = CreateNewsItem(
            headline: "AAPL Update",
            sentimentScore: 0.5m);

        var withoutSentiment = CreateNewsItem(
            headline: "AAPL Update",
            sentimentScore: null);

        // Act
        var withSentimentScore = _sut.ScoreRelevance(withSentiment, "AAPL");
        var withoutSentimentScore = _sut.ScoreRelevance(withoutSentiment, "AAPL");

        // Assert
        withSentimentScore.Should().BeGreaterThan(withoutSentimentScore,
            "articles with sentiment data should score higher");
    }

    [Fact]
    public void ScoreRelevance_ReturnsValueBetweenZeroAndOne()
    {
        // Arrange
        var article = CreateNewsItem(
            headline: "AAPL AAPL AAPL Apple Inc Apple Inc",
            summary: "AAPL AAPL Apple Apple",
            source: "Reuters",
            sentimentScore: 1.0m,
            relatedSymbols: new List<string> { "AAPL" },
            publishedAt: DateTime.Now);

        // Act
        var score = _sut.ScoreRelevance(article, "AAPL", "Apple Inc");

        // Assert
        score.Should().BeGreaterThanOrEqualTo(0m);
        score.Should().BeLessThanOrEqualTo(1.0m);
    }

    [Fact]
    public void ScoreRelevance_WithNoRelevantContent_ReturnsLowScore()
    {
        // Arrange
        var article = CreateNewsItem(
            headline: "Weather Report for Tomorrow",
            summary: "Expect sunshine in most areas",
            source: "Unknown",
            publishedAt: DateTime.Now.AddDays(-10));

        // Act
        var score = _sut.ScoreRelevance(article, "AAPL");

        // Assert
        score.Should().BeLessThan(0.3m, "irrelevant content should score low");
    }

    [Theory]
    [InlineData("AAPL Stock Price Rises", "AAPL")]
    [InlineData("MSFT Earnings Beat Expectations", "MSFT")]
    [InlineData("TSLA Announces New Model", "TSLA")]
    public void ScoreRelevance_WithVariousTickers_DetectsTicker(string headline, string ticker)
    {
        // Arrange
        var article = CreateNewsItem(headline: headline);

        // Act
        var score = _sut.ScoreRelevance(article, ticker);

        // Assert
        score.Should().BeGreaterThan(0.3m, $"should detect {ticker} in headline");
    }

    [Fact]
    public void ScoreRelevance_DoesNotMatchPartialTicker()
    {
        // Arrange - "AAPLX" should not match "AAPL"
        var article = CreateNewsItem(
            headline: "AAPLX Fund Performance Report");

        // Act
        var score = _sut.ScoreRelevance(article, "AAPL");

        // Assert
        // The service gives some baseline score for recency/source, so we expect it to be lower
        // than when the ticker is actually found (which would typically be >0.35)
        score.Should().BeLessThan(0.35m, "partial ticker matches should not boost score significantly");
    }

    #endregion

    #region AggregateNews Tests

    [Fact]
    public void AggregateNews_SortsArticlesByRelevanceDescending()
    {
        // Arrange
        var articles = new List<NewsItem>
        {
            CreateNewsItem(headline: "Weather Report", publishedAt: DateTime.Now),
            CreateNewsItem(headline: "AAPL Earnings Report", relatedSymbols: new List<string> { "AAPL" }),
            CreateNewsItem(headline: "General Market News")
        };

        // Act
        var result = _sut.AggregateNews(articles, "AAPL");

        // Assert
        result.Should().HaveCount(3);
        result.First().Headline.Should().Contain("AAPL");
        result.Should().BeInDescendingOrder(a => a.RelevanceScore);
    }

    [Fact]
    public void AggregateNews_DeduplicatesSimilarHeadlines()
    {
        // Arrange
        var articles = new List<NewsItem>
        {
            CreateNewsItem(headline: "Apple Stock Rises on Strong Earnings", source: "Source1"),
            CreateNewsItem(headline: "Apple Stock Rises on Strong Earnings Report", source: "Source2"),
            CreateNewsItem(headline: "Different Article About Tech", source: "Source3")
        };

        // Act
        var result = _sut.AggregateNews(articles, "AAPL", "Apple Inc");

        // Assert
        result.Should().HaveCount(2, "similar headlines should be deduplicated");
    }

    [Fact]
    public void AggregateNews_RespectsMaxResultsLimit()
    {
        // Arrange
        var articles = Enumerable.Range(0, 50)
            .Select(i => CreateNewsItem(headline: $"Article {i} about AAPL"))
            .ToList();

        // Act
        var result = _sut.AggregateNews(articles, "AAPL", maxResults: 10);

        // Assert
        result.Should().HaveCount(10);
    }

    [Fact]
    public void AggregateNews_AssignsRelevanceScoresToAllArticles()
    {
        // Arrange
        var articles = new List<NewsItem>
        {
            CreateNewsItem(headline: "Article 1"),
            CreateNewsItem(headline: "Article 2"),
            CreateNewsItem(headline: "Article 3")
        };

        // Act
        var result = _sut.AggregateNews(articles, "AAPL");

        // Assert
        result.Should().AllSatisfy(a => a.RelevanceScore.Should().NotBeNull());
    }

    [Fact]
    public void AggregateNews_WithEmptyInput_ReturnsEmptyList()
    {
        // Arrange
        var articles = new List<NewsItem>();

        // Act
        var result = _sut.AggregateNews(articles, "AAPL");

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void AggregateNews_PreservesHigherScoredArticleWhenDeduplicating()
    {
        // Arrange - Same headline but different sources (premium vs unknown)
        var articles = new List<NewsItem>
        {
            CreateNewsItem(
                headline: "AAPL Breaking News Today",
                source: "Unknown Blog",
                relatedSymbols: new List<string> { "AAPL" }),
            CreateNewsItem(
                headline: "AAPL Breaking News Today",
                source: "Reuters",
                relatedSymbols: new List<string> { "AAPL" })
        };

        // Act
        var result = _sut.AggregateNews(articles, "AAPL");

        // Assert
        result.Should().HaveCount(1);
        result.First().Source.Should().Be("Reuters", "should keep higher-scored article");
    }

    #endregion

    #region Helper Methods

    private static NewsItem CreateNewsItem(
        string headline = "Test Headline",
        string? summary = null,
        string source = "Test Source",
        DateTime? publishedAt = null,
        decimal? sentimentScore = null,
        List<string>? relatedSymbols = null)
    {
        return new NewsItem
        {
            Headline = headline,
            Summary = summary ?? "Test summary",
            Source = source,
            PublishedAt = publishedAt ?? DateTime.Now.AddHours(-2),
            Url = "https://example.com/article",
            SentimentScore = sentimentScore,
            RelatedSymbols = relatedSymbols ?? new List<string>()
        };
    }

    #endregion
}
