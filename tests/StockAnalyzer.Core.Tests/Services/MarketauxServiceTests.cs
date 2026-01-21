using System.Net;
using System.Text.Json;
using FluentAssertions;
using Moq;
using Moq.Protected;
using StockAnalyzer.Core.Services;
using Xunit;

namespace StockAnalyzer.Core.Tests.Services;

public class MarketauxServiceTests
{
    private const string TestApiToken = "test-api-token"; // pragma: allowlist secret

    /// <summary>
    /// Creates a mock HttpClient that returns predefined responses.
    /// Uses a factory lambda to create fresh HttpResponseMessage instances to avoid CA2000 warnings.
    /// </summary>
    private static HttpClient CreateMockHttpClient(HttpStatusCode statusCode, string content)
    {
        var mockHandler = new Mock<HttpMessageHandler>();

        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() => new HttpResponseMessage
            {
                StatusCode = statusCode,
                Content = new StringContent(content)
            });

        return new HttpClient(mockHandler.Object);
    }

    private static string CreateMarketauxResponse(int count, string? symbol = null)
    {
        var articles = Enumerable.Range(0, count).Select(i => new
        {
            uuid = Guid.NewGuid().ToString(),
            title = $"Test Article {i + 1}",
            description = $"Description for article {i + 1}",
            url = $"https://example.com/article{i}",
            image_url = $"https://example.com/image{i}.jpg",
            published_at = DateTime.Now.AddHours(-i).ToString("o"),
            source = $"Test Source {i % 3}",
            entities = symbol != null ? new[]
            {
                new
                {
                    symbol = symbol,
                    name = "Test Company",
                    sentiment_score = 0.5m - (i * 0.1m),
                    industry = "Technology"
                }
            } : null
        }).ToList();

        return JsonSerializer.Serialize(new
        {
            data = articles,
            meta = new { found = count, returned = count, limit = 10, page = 1 }
        });
    }

    #region GetNewsAsync Tests

    [Fact]
    public async Task GetNewsAsync_WithValidResponse_ReturnsNews()
    {
        // Arrange
        var mockResponse = CreateMarketauxResponse(5, "AAPL");
        using var httpClient = CreateMockHttpClient(HttpStatusCode.OK, mockResponse);
        var sut = new MarketauxService(TestApiToken, httpClient);

        // Act
        var result = await sut.GetNewsAsync("AAPL");

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(5);
    }

    [Fact]
    public async Task GetNewsAsync_WithEmptyApiToken_ReturnsEmptyList()
    {
        // Arrange
        using var httpClient = CreateMockHttpClient(HttpStatusCode.OK, "{}");
        var sut = new MarketauxService("", httpClient);

        // Act
        var result = await sut.GetNewsAsync("AAPL");

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetNewsAsync_WithApiError_ReturnsEmptyList()
    {
        // Arrange
        using var httpClient = CreateMockHttpClient(HttpStatusCode.InternalServerError, "Server Error");
        var sut = new MarketauxService(TestApiToken, httpClient);

        // Act
        var result = await sut.GetNewsAsync("AAPL");

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetNewsAsync_WithEmptyDataResponse_ReturnsEmptyList()
    {
        // Arrange
        using var httpClient = CreateMockHttpClient(HttpStatusCode.OK, "{\"data\": null}");
        var sut = new MarketauxService(TestApiToken, httpClient);

        // Act
        var result = await sut.GetNewsAsync("AAPL");

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetNewsAsync_ParsesAllFieldsCorrectly()
    {
        // Arrange
        var singleArticleJson = @"{
            ""data"": [{
                ""uuid"": ""test-uuid"",
                ""title"": ""Breaking News"",
                ""description"": ""Test description"",
                ""url"": ""https://example.com/article"",
                ""image_url"": ""https://example.com/image.jpg"",
                ""published_at"": ""2026-01-15T10:30:00Z"",
                ""source"": ""Reuters"",
                ""entities"": [{
                    ""symbol"": ""AAPL"",
                    ""name"": ""Apple Inc"",
                    ""sentiment_score"": 0.75,
                    ""industry"": ""Technology""
                }]
            }]
        }";

        using var httpClient = CreateMockHttpClient(HttpStatusCode.OK, singleArticleJson);
        var sut = new MarketauxService(TestApiToken, httpClient);

        // Act
        var result = await sut.GetNewsAsync("AAPL");

        // Assert
        result.Should().HaveCount(1);
        var article = result.First();
        article.Headline.Should().Be("Breaking News");
        article.Summary.Should().Be("Test description");
        article.Source.Should().Be("Reuters");
        article.Url.Should().Be("https://example.com/article");
        article.ImageUrl.Should().Be("https://example.com/image.jpg");
        article.RelatedSymbols.Should().Contain("AAPL");
        article.SentimentScore.Should().Be(0.75m);
        article.SourceApi.Should().Be("marketaux");
    }

    [Fact]
    public async Task GetNewsAsync_MapsPositiveSentiment()
    {
        // Arrange
        var jsonWithPositiveSentiment = @"{
            ""data"": [{
                ""title"": ""Good News"",
                ""entities"": [{ ""symbol"": ""AAPL"", ""sentiment_score"": 0.5 }]
            }]
        }";

        using var httpClient = CreateMockHttpClient(HttpStatusCode.OK, jsonWithPositiveSentiment);
        var sut = new MarketauxService(TestApiToken, httpClient);

        // Act
        var result = await sut.GetNewsAsync("AAPL");

        // Assert
        result.First().Sentiment.Should().Be("positive");
    }

    [Fact]
    public async Task GetNewsAsync_MapsNegativeSentiment()
    {
        // Arrange
        var jsonWithNegativeSentiment = @"{
            ""data"": [{
                ""title"": ""Bad News"",
                ""entities"": [{ ""symbol"": ""AAPL"", ""sentiment_score"": -0.5 }]
            }]
        }";

        using var httpClient = CreateMockHttpClient(HttpStatusCode.OK, jsonWithNegativeSentiment);
        var sut = new MarketauxService(TestApiToken, httpClient);

        // Act
        var result = await sut.GetNewsAsync("AAPL");

        // Assert
        result.First().Sentiment.Should().Be("negative");
    }

    [Fact]
    public async Task GetNewsAsync_MapsNeutralSentiment()
    {
        // Arrange
        var jsonWithNeutralSentiment = @"{
            ""data"": [{
                ""title"": ""Neutral News"",
                ""entities"": [{ ""symbol"": ""AAPL"", ""sentiment_score"": 0.1 }]
            }]
        }";

        using var httpClient = CreateMockHttpClient(HttpStatusCode.OK, jsonWithNeutralSentiment);
        var sut = new MarketauxService(TestApiToken, httpClient);

        // Act
        var result = await sut.GetNewsAsync("AAPL");

        // Assert
        result.First().Sentiment.Should().Be("neutral");
    }

    [Fact]
    public async Task GetNewsAsync_RespectsLimitParameter()
    {
        // Arrange
        var mockHandler = new Mock<HttpMessageHandler>();
        string? capturedUrl = null;

        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => capturedUrl = req.RequestUri?.ToString())
            .ReturnsAsync(() => new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(CreateMarketauxResponse(5))
            });

        using var httpClient = new HttpClient(mockHandler.Object);
        var sut = new MarketauxService(TestApiToken, httpClient);

        // Act
        await sut.GetNewsAsync("AAPL", limit: 25);

        // Assert
        capturedUrl.Should().Contain("limit=25");
    }

    [Fact]
    public async Task GetNewsAsync_CapsLimitAt50()
    {
        // Arrange
        var mockHandler = new Mock<HttpMessageHandler>();
        string? capturedUrl = null;

        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => capturedUrl = req.RequestUri?.ToString())
            .ReturnsAsync(() => new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(CreateMarketauxResponse(5))
            });

        using var httpClient = new HttpClient(mockHandler.Object);
        var sut = new MarketauxService(TestApiToken, httpClient);

        // Act
        await sut.GetNewsAsync("AAPL", limit: 100);

        // Assert
        capturedUrl.Should().Contain("limit=50", "limit should be capped at 50");
    }

    [Fact]
    public async Task GetNewsAsync_UppercasesSymbol()
    {
        // Arrange
        var mockHandler = new Mock<HttpMessageHandler>();
        string? capturedUrl = null;

        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => capturedUrl = req.RequestUri?.ToString())
            .ReturnsAsync(() => new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(CreateMarketauxResponse(1))
            });

        using var httpClient = new HttpClient(mockHandler.Object);
        var sut = new MarketauxService(TestApiToken, httpClient);

        // Act
        await sut.GetNewsAsync("aapl");

        // Assert
        capturedUrl.Should().Contain("symbols=AAPL");
    }

    #endregion

    #region GetMarketNewsAsync Tests

    [Fact]
    public async Task GetMarketNewsAsync_WithValidResponse_ReturnsNews()
    {
        // Arrange
        var mockResponse = CreateMarketauxResponse(5);
        using var httpClient = CreateMockHttpClient(HttpStatusCode.OK, mockResponse);
        var sut = new MarketauxService(TestApiToken, httpClient);

        // Act
        var result = await sut.GetMarketNewsAsync();

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(5);
    }

    [Fact]
    public async Task GetMarketNewsAsync_WithEmptyApiToken_ReturnsEmptyList()
    {
        // Arrange
        using var httpClient = CreateMockHttpClient(HttpStatusCode.OK, "{}");
        var sut = new MarketauxService("", httpClient);

        // Act
        var result = await sut.GetMarketNewsAsync();

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetMarketNewsAsync_SetsGeneralCategory()
    {
        // Arrange
        var mockResponse = CreateMarketauxResponse(1);
        using var httpClient = CreateMockHttpClient(HttpStatusCode.OK, mockResponse);
        var sut = new MarketauxService(TestApiToken, httpClient);

        // Act
        var result = await sut.GetMarketNewsAsync();

        // Assert
        result.First().Category.Should().Be("general");
    }

    [Fact]
    public async Task GetMarketNewsAsync_SetsSourceApiToMarketaux()
    {
        // Arrange
        var mockResponse = CreateMarketauxResponse(3);
        using var httpClient = CreateMockHttpClient(HttpStatusCode.OK, mockResponse);
        var sut = new MarketauxService(TestApiToken, httpClient);

        // Act
        var result = await sut.GetMarketNewsAsync();

        // Assert
        result.Should().AllSatisfy(a => a.SourceApi.Should().Be("marketaux"));
    }

    #endregion

    #region Constructor Tests

    [Fact]
    public void Constructor_WithEmptyApiToken_CreatesService()
    {
        // Arrange & Act
        var sut = new MarketauxService("");

        // Assert
        sut.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithCustomHttpClient_UsesProvidedClient()
    {
        // Arrange
        var mockResponse = CreateMarketauxResponse(1);
        using var httpClient = CreateMockHttpClient(HttpStatusCode.OK, mockResponse);

        // Act
        var sut = new MarketauxService(TestApiToken, httpClient);

        // Assert
        sut.Should().NotBeNull();
    }

    #endregion
}
