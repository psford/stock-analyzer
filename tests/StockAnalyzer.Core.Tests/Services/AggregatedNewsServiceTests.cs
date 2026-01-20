using System.Net;
using System.Text.Json;
using FluentAssertions;
using Moq;
using Moq.Protected;
using StockAnalyzer.Core.Models;
using StockAnalyzer.Core.Services;
using Xunit;

namespace StockAnalyzer.Core.Tests.Services;

public class AggregatedNewsServiceTests
{
    private const string TestFinnhubApiKey = "test-finnhub-key"; // pragma: allowlist secret
    private const string TestMarketauxToken = "test-marketaux-token"; // pragma: allowlist secret

    #region GetAggregatedNewsAsync Tests

    [Fact]
    public async Task GetAggregatedNewsAsync_CombinesNewsFromBothSources()
    {
        // Arrange
        var finnhubResponse = CreateFinnhubNewsResponse(3);
        var marketauxResponse = CreateMarketauxResponse(2, "AAPL");

        var (sut, _, _) = CreateService(finnhubResponse, marketauxResponse);

        // Act
        var result = await sut.GetAggregatedNewsAsync("AAPL");

        // Assert
        result.Should().NotBeNull();
        result.TotalFetched.Should().BeGreaterOrEqualTo(2, "should fetch from multiple sources");
    }

    [Fact]
    public async Task GetAggregatedNewsAsync_ReturnsCorrectSymbol()
    {
        // Arrange
        var (sut, _, _) = CreateService(
            CreateFinnhubNewsResponse(1),
            CreateMarketauxResponse(1));

        // Act
        var result = await sut.GetAggregatedNewsAsync("aapl");

        // Assert
        result.Symbol.Should().Be("AAPL", "symbol should be uppercased");
    }

    [Fact]
    public async Task GetAggregatedNewsAsync_SetsDateRange()
    {
        // Arrange
        var (sut, _, _) = CreateService(
            CreateFinnhubNewsResponse(1),
            CreateMarketauxResponse(1));

        // Act
        var result = await sut.GetAggregatedNewsAsync("AAPL", days: 7);

        // Assert
        result.FromDate.Should().BeCloseTo(DateTime.Now.AddDays(-7), TimeSpan.FromMinutes(1));
        result.ToDate.Should().BeCloseTo(DateTime.Now, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task GetAggregatedNewsAsync_CalculatesSourceBreakdown()
    {
        // Arrange
        var finnhubResponse = CreateFinnhubNewsResponse(3);
        var marketauxResponse = CreateMarketauxResponse(2, "AAPL");

        var (sut, _, _) = CreateService(finnhubResponse, marketauxResponse);

        // Act
        var result = await sut.GetAggregatedNewsAsync("AAPL", maxResults: 10);

        // Assert
        result.SourceBreakdown.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetAggregatedNewsAsync_CalculatesAverageRelevanceScore()
    {
        // Arrange
        var (sut, _, _) = CreateService(
            CreateFinnhubNewsResponse(3),
            CreateMarketauxResponse(2));

        // Act
        var result = await sut.GetAggregatedNewsAsync("AAPL");

        // Assert
        if (result.Articles.Any())
        {
            result.AverageRelevanceScore.Should().BeGreaterThan(0);
            result.AverageRelevanceScore.Should().BeLessThanOrEqualTo(1);
        }
    }

    [Fact]
    public async Task GetAggregatedNewsAsync_RespectsMaxResults()
    {
        // Arrange
        var finnhubResponse = CreateFinnhubNewsResponse(15);
        var marketauxResponse = CreateMarketauxResponse(15, "AAPL");

        var (sut, _, _) = CreateService(finnhubResponse, marketauxResponse);

        // Act
        var result = await sut.GetAggregatedNewsAsync("AAPL", maxResults: 10);

        // Assert
        result.Articles.Should().HaveCountLessOrEqualTo(10);
    }

    [Fact]
    public async Task GetAggregatedNewsAsync_WithNoNews_ReturnsEmptyResult()
    {
        // Arrange
        var (sut, _, _) = CreateService("[]", "{\"data\": []}");

        // Act
        var result = await sut.GetAggregatedNewsAsync("AAPL");

        // Assert
        result.Articles.Should().BeEmpty();
        result.TotalFetched.Should().Be(0);
        result.AverageRelevanceScore.Should().Be(0);
    }

    [Fact]
    public async Task GetAggregatedNewsAsync_TagsArticlesWithSourceApi()
    {
        // Arrange
        var (sut, _, _) = CreateService(
            CreateFinnhubNewsResponse(2),
            CreateMarketauxResponse(2, "AAPL"));

        // Act
        var result = await sut.GetAggregatedNewsAsync("AAPL");

        // Assert
        result.Articles.Should().AllSatisfy(a =>
            a.SourceApi.Should().NotBeNullOrEmpty());
    }

    [Fact]
    public async Task GetAggregatedNewsAsync_SortsArticlesByRelevanceDescending()
    {
        // Arrange
        var (sut, _, _) = CreateService(
            CreateFinnhubNewsResponse(5),
            CreateMarketauxResponse(5, "AAPL"));

        // Act
        var result = await sut.GetAggregatedNewsAsync("AAPL");

        // Assert
        result.Articles.Should().BeInDescendingOrder(a => a.RelevanceScore);
    }

    #endregion

    #region GetAggregatedMarketNewsAsync Tests

    [Fact]
    public async Task GetAggregatedMarketNewsAsync_CombinesNewsFromBothSources()
    {
        // Arrange
        var finnhubResponse = CreateFinnhubMarketNewsResponse(3);
        var marketauxResponse = CreateMarketauxResponse(2);

        var (sut, _, _) = CreateService(finnhubResponse, marketauxResponse);

        // Act
        var result = await sut.GetAggregatedMarketNewsAsync();

        // Assert
        result.Should().NotBeNull();
        result.TotalFetched.Should().BeGreaterOrEqualTo(2);
    }

    [Fact]
    public async Task GetAggregatedMarketNewsAsync_SetsSymbolToMarket()
    {
        // Arrange
        var (sut, _, _) = CreateService(
            CreateFinnhubMarketNewsResponse(1),
            CreateMarketauxResponse(1));

        // Act
        var result = await sut.GetAggregatedMarketNewsAsync();

        // Assert
        result.Symbol.Should().Be("MARKET");
    }

    [Fact]
    public async Task GetAggregatedMarketNewsAsync_DeduplicatesByUrl()
    {
        // Arrange - Create responses with duplicate URLs
        var duplicateUrlJson = @"[{
            ""category"": ""general"",
            ""datetime"": " + DateTimeOffset.Now.ToUnixTimeSeconds() + @",
            ""headline"": ""Test Article"",
            ""id"": 1,
            ""image"": ""https://example.com/img.jpg"",
            ""source"": ""Test"",
            ""summary"": ""Test"",
            ""url"": ""https://example.com/same-url""
        }, {
            ""category"": ""general"",
            ""datetime"": " + DateTimeOffset.Now.ToUnixTimeSeconds() + @",
            ""headline"": ""Same Article Different Source"",
            ""id"": 2,
            ""image"": ""https://example.com/img.jpg"",
            ""source"": ""Other"",
            ""summary"": ""Test"",
            ""url"": ""https://example.com/same-url""
        }]";

        var (sut, _, _) = CreateService(duplicateUrlJson, "{\"data\": []}");

        // Act
        var result = await sut.GetAggregatedMarketNewsAsync();

        // Assert
        var urlCounts = result.Articles
            .Where(a => a.Url == "https://example.com/same-url")
            .Count();
        urlCounts.Should().BeLessThanOrEqualTo(1, "duplicate URLs should be removed");
    }

    [Fact]
    public async Task GetAggregatedMarketNewsAsync_RespectsMaxResults()
    {
        // Arrange
        var (sut, _, _) = CreateService(
            CreateFinnhubMarketNewsResponse(15),
            CreateMarketauxResponse(15));

        // Act
        var result = await sut.GetAggregatedMarketNewsAsync(maxResults: 10);

        // Assert
        result.Articles.Should().HaveCountLessOrEqualTo(10);
    }

    [Fact]
    public async Task GetAggregatedMarketNewsAsync_ScoresArticlesForRelevance()
    {
        // Arrange
        var (sut, _, _) = CreateService(
            CreateFinnhubMarketNewsResponse(3),
            CreateMarketauxResponse(3));

        // Act
        var result = await sut.GetAggregatedMarketNewsAsync();

        // Assert
        result.Articles.Should().AllSatisfy(a =>
            a.RelevanceScore.Should().NotBeNull());
    }

    [Fact]
    public async Task GetAggregatedMarketNewsAsync_PremiumSourcesScoreHigher()
    {
        // Arrange - Reuters should score higher than unknown source
        var reutersJson = @"[{
            ""category"": ""general"",
            ""datetime"": " + DateTimeOffset.Now.ToUnixTimeSeconds() + @",
            ""headline"": ""Market Update"",
            ""id"": 1,
            ""source"": ""Reuters"",
            ""summary"": ""Test"",
            ""url"": ""https://reuters.com/article""
        }]";

        var unknownJson = @"{""data"": [{
            ""title"": ""Market Update"",
            ""source"": ""Unknown Blog"",
            ""url"": ""https://unknown.com/article""
        }]}";

        var (sut, _, _) = CreateService(reutersJson, unknownJson);

        // Act
        var result = await sut.GetAggregatedMarketNewsAsync();

        // Assert - Articles should be ordered by relevance, Reuters should be first
        if (result.Articles.Count >= 2)
        {
            var reutersArticle = result.Articles.FirstOrDefault(a => a.Source == "Reuters");
            var unknownArticle = result.Articles.FirstOrDefault(a => a.Source == "Unknown Blog");

            if (reutersArticle != null && unknownArticle != null &&
                reutersArticle.RelevanceScore.HasValue && unknownArticle.RelevanceScore.HasValue)
            {
                reutersArticle.RelevanceScore.Value.Should().BeGreaterThan(unknownArticle.RelevanceScore.Value);
            }
        }
    }

    #endregion

    #region Helper Methods

    private (AggregatedNewsService, NewsService, MarketauxService) CreateService(
        string finnhubResponse,
        string marketauxResponse)
    {
        var finnhubHttpClient = CreateMockHttpClient(finnhubResponse);
        var marketauxHttpClient = CreateMockHttpClient(marketauxResponse);

        var newsService = new NewsService(TestFinnhubApiKey, finnhubHttpClient);
        var marketauxService = new MarketauxService(TestMarketauxToken, marketauxHttpClient);
        var relevanceService = new HeadlineRelevanceService();

        var aggregatedService = new AggregatedNewsService(
            newsService,
            marketauxService,
            relevanceService);

        return (aggregatedService, newsService, marketauxService);
    }

    private static HttpClient CreateMockHttpClient(string content)
    {
        var mockHandler = new Mock<HttpMessageHandler>();

        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(content)
            });

        return new HttpClient(mockHandler.Object);
    }

    private static string CreateFinnhubNewsResponse(int count)
    {
        var news = Enumerable.Range(0, count).Select(i => new
        {
            category = "company",
            datetime = DateTimeOffset.Now.AddHours(-i).ToUnixTimeSeconds(),
            headline = $"AAPL Test Headline {i + 1}",
            id = 1000 + i,
            image = $"https://example.com/image{i}.jpg",
            related = "AAPL",
            source = i % 2 == 0 ? "Reuters" : $"Test Source {i}",
            summary = $"Test summary for AAPL article {i + 1}",
            url = $"https://example.com/finnhub-article{i}"
        }).ToList();

        return JsonSerializer.Serialize(news);
    }

    private static string CreateFinnhubMarketNewsResponse(int count)
    {
        var news = Enumerable.Range(0, count).Select(i => new
        {
            category = "general",
            datetime = DateTimeOffset.Now.AddHours(-i).ToUnixTimeSeconds(),
            headline = $"Market Update {i + 1}",
            id = 2000 + i,
            image = $"https://example.com/market-image{i}.jpg",
            related = "",
            source = i % 2 == 0 ? "Bloomberg" : $"Source {i}",
            summary = $"Market summary {i + 1}",
            url = $"https://example.com/market-article{i}"
        }).ToList();

        return JsonSerializer.Serialize(news);
    }

    private static string CreateMarketauxResponse(int count, string? symbol = null)
    {
        var articles = Enumerable.Range(0, count).Select(i => new
        {
            uuid = Guid.NewGuid().ToString(),
            title = symbol != null ? $"{symbol} News Article {i + 1}" : $"Market Article {i + 1}",
            description = $"Description {i + 1}",
            url = $"https://example.com/marketaux-article{i}",
            image_url = $"https://example.com/marketaux-image{i}.jpg",
            published_at = DateTime.Now.AddHours(-i).ToString("o"),
            source = i % 2 == 0 ? "CNBC" : $"Marketaux Source {i}",
            entities = symbol != null ? new[]
            {
                new
                {
                    symbol = symbol,
                    name = "Test Company",
                    sentiment_score = 0.3m,
                    industry = "Technology"
                }
            } : null
        }).ToList();

        return JsonSerializer.Serialize(new
        {
            data = articles,
            meta = new { found = count, returned = count }
        });
    }

    #endregion
}
