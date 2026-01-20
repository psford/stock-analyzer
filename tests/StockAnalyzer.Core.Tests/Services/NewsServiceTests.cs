using System.Net;
using System.Text.Json;
using FluentAssertions;
using Moq;
using Moq.Protected;
using StockAnalyzer.Core.Services;
using Xunit;

namespace StockAnalyzer.Core.Tests.Services;

public class NewsServiceTests
{
    private const string TestApiKey = "test-api-key"; // pragma: allowlist secret

    private static HttpClient CreateMockHttpClient(HttpStatusCode statusCode, string content)
    {
        var mockHandler = new Mock<HttpMessageHandler>();

        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = statusCode,
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
            headline = $"Test Headline {i + 1}",
            id = 1000 + i,
            image = $"https://example.com/image{i}.jpg",
            related = "AAPL",
            source = $"Test Source {i % 3}",
            summary = $"Test summary for article {i + 1}",
            url = $"https://example.com/article{i}"
        }).ToList();

        return JsonSerializer.Serialize(news);
    }

    #region GetCompanyNewsAsync Tests

    [Fact]
    public async Task GetCompanyNewsAsync_WithValidResponse_ReturnsNews()
    {
        // Arrange
        var mockResponse = CreateFinnhubNewsResponse(5);
        var httpClient = CreateMockHttpClient(HttpStatusCode.OK, mockResponse);
        var sut = new NewsService(TestApiKey, httpClient);

        // Act
        var result = await sut.GetCompanyNewsAsync("AAPL");

        // Assert
        result.Should().NotBeNull();
        result.Symbol.Should().Be("AAPL");
        result.Articles.Should().HaveCount(5);
        result.TotalCount.Should().Be(5);
    }

    [Fact]
    public async Task GetCompanyNewsAsync_WithDateRange_ConstructsCorrectSymbol()
    {
        // Arrange
        var mockResponse = CreateFinnhubNewsResponse(3);
        var httpClient = CreateMockHttpClient(HttpStatusCode.OK, mockResponse);
        var sut = new NewsService(TestApiKey, httpClient);

        var fromDate = new DateTime(2026, 1, 1);
        var toDate = new DateTime(2026, 1, 15);

        // Act
        var result = await sut.GetCompanyNewsAsync("msft", fromDate, toDate);

        // Assert
        result.Symbol.Should().Be("MSFT"); // Should be uppercased
        result.FromDate.Should().Be(fromDate);
        result.ToDate.Should().Be(toDate);
    }

    [Fact]
    public async Task GetCompanyNewsAsync_WithApiError_ReturnsEmptyResult()
    {
        // Arrange
        var httpClient = CreateMockHttpClient(HttpStatusCode.InternalServerError, "Server Error");
        var sut = new NewsService(TestApiKey, httpClient);

        // Act
        var result = await sut.GetCompanyNewsAsync("AAPL");

        // Assert
        result.Should().NotBeNull();
        result.Symbol.Should().Be("AAPL");
        result.Articles.Should().BeEmpty();
    }

    [Fact]
    public async Task GetCompanyNewsAsync_WithEmptyResponse_ReturnsEmptyList()
    {
        // Arrange
        var httpClient = CreateMockHttpClient(HttpStatusCode.OK, "[]");
        var sut = new NewsService(TestApiKey, httpClient);

        // Act
        var result = await sut.GetCompanyNewsAsync("AAPL");

        // Assert
        result.Should().NotBeNull();
        result.Articles.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task GetCompanyNewsAsync_ParsesAllFieldsCorrectly()
    {
        // Arrange
        var unixTimestamp = DateTimeOffset.Now.ToUnixTimeSeconds();
        var singleNewsJson = $@"[{{
            ""category"": ""company"",
            ""datetime"": {unixTimestamp},
            ""headline"": ""Breaking News"",
            ""id"": 12345,
            ""image"": ""https://example.com/image.jpg"",
            ""related"": ""AAPL"",
            ""source"": ""Reuters"",
            ""summary"": ""This is a test summary"",
            ""url"": ""https://example.com/article""
        }}]";

        var httpClient = CreateMockHttpClient(HttpStatusCode.OK, singleNewsJson);
        var sut = new NewsService(TestApiKey, httpClient);

        // Act
        var result = await sut.GetCompanyNewsAsync("AAPL");

        // Assert
        result.Articles.Should().HaveCount(1);
        var article = result.Articles.First();
        article.Headline.Should().Be("Breaking News");
        article.Summary.Should().Be("This is a test summary");
        article.Source.Should().Be("Reuters");
        article.Url.Should().Be("https://example.com/article");
        article.ImageUrl.Should().Be("https://example.com/image.jpg");
        article.Category.Should().Be("company");
        article.RelatedSymbols.Should().Contain("AAPL");
    }

    [Fact]
    public async Task GetCompanyNewsAsync_OrdersArticlesByDateDescending()
    {
        // Arrange
        var mockResponse = CreateFinnhubNewsResponse(5);
        var httpClient = CreateMockHttpClient(HttpStatusCode.OK, mockResponse);
        var sut = new NewsService(TestApiKey, httpClient);

        // Act
        var result = await sut.GetCompanyNewsAsync("AAPL");

        // Assert
        result.Articles.Should().BeInDescendingOrder(a => a.PublishedAt);
    }

    #endregion

    #region GetNewsForDateAsync Tests

    [Fact]
    public async Task GetNewsForDateAsync_ReturnsArticlesForDateRange()
    {
        // Arrange
        var mockResponse = CreateFinnhubNewsResponse(10);
        var httpClient = CreateMockHttpClient(HttpStatusCode.OK, mockResponse);
        var sut = new NewsService(TestApiKey, httpClient);

        // Act
        var result = await sut.GetNewsForDateAsync("AAPL", DateTime.Today);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(10);
    }

    [Fact]
    public async Task GetNewsForDateAsync_WithNoNews_ReturnsEmptyList()
    {
        // Arrange
        var httpClient = CreateMockHttpClient(HttpStatusCode.OK, "[]");
        var sut = new NewsService(TestApiKey, httpClient);

        // Act
        var result = await sut.GetNewsForDateAsync("AAPL", DateTime.Today);

        // Assert
        result.Should().BeEmpty();
    }

    #endregion

    #region Constructor Tests

    [Fact]
    public void Constructor_WithEmptyApiKey_CreatesService()
    {
        // Arrange & Act
        var sut = new NewsService("");

        // Assert
        sut.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithCustomHttpClient_UsesProvidedClient()
    {
        // Arrange
        var mockResponse = CreateFinnhubNewsResponse(1);
        var httpClient = CreateMockHttpClient(HttpStatusCode.OK, mockResponse);

        // Act
        var sut = new NewsService(TestApiKey, httpClient);

        // Assert
        sut.Should().NotBeNull();
    }

    #endregion
}
