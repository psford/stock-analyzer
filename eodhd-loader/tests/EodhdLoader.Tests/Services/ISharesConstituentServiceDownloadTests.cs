namespace EodhdLoader.Tests.Services;

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Moq.Protected;
using Xunit;
using EodhdLoader.Services;
using StockAnalyzer.Core.Data;
using Microsoft.EntityFrameworkCore;
using EodhdLoader.Models;

/// <summary>
/// Tests for ISharesConstituentService.DownloadAsync method.
/// Covers acceptance criteria AC1.1-AC1.5 for JSON download functionality.
/// </summary>
public class ISharesConstituentServiceDownloadTests
{
    /// <summary>
    /// Helper: Creates an ISharesConstituentService with a mocked HttpMessageHandler and InMemory DbContext.
    /// </summary>
    private static (ISharesConstituentService Service, Mock<HttpMessageHandler> MockHandler) BuildServiceWithMockedHttp()
    {
        var mockHandler = new Mock<HttpMessageHandler>();
        var httpClient = new HttpClient(mockHandler.Object);

        var factory = new Mock<IHttpClientFactory>();
        factory
            .Setup(x => x.CreateClient(It.IsAny<string>()))
            .Returns(httpClient);

        var options = new DbContextOptionsBuilder<StockAnalyzerDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        var dbContext = new StockAnalyzerDbContext(options);
        var service = new ISharesConstituentService(factory.Object, dbContext);

        return (service, mockHandler);
    }

    /// <summary>
    /// Helper: Creates a minimal valid iShares JSON response.
    /// </summary>
    private static string CreateValidISharesJson()
    {
        return @"{
  ""aaData"": [
    [""AAPL"", ""Apple Inc."", ""Information Technology"", ""Equity"", {""display"": ""$1,234.56"", ""raw"": 1234.56}, {""display"": ""2.34%"", ""raw"": 2.34}, null, {""display"": ""123.45"", ""raw"": 123.45}, ""037833100"", ""US0378331005"", ""2588173"", ""$10.01"", ""UNITED STATES"", ""NASDAQ"", ""USD""],
    [""MSFT"", ""Microsoft Corporation"", ""Information Technology"", ""Equity"", {""display"": ""$5,000.00"", ""raw"": 5000.00}, {""display"": ""3.50%"", ""raw"": 3.50}, null, {""display"": ""50.00"", ""raw"": 50.00}, ""594918104"", ""US5949181045"", ""2588173"", ""$100.00"", ""UNITED STATES"", ""NASDAQ"", ""USD""]
  ]
}";
    }

    /// <summary>
    /// AC1.1: Service downloads holdings JSON for a valid ETF ticker and as-of date.
    /// </summary>
    [Fact]
    public async Task DownloadAsync_WithValidTicker_ReturnsValidJson()
    {
        // Arrange
        var (service, mockHandler) = BuildServiceWithMockedHttp();
        var validJson = CreateValidISharesJson();

        mockHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(validJson)
            });

        // Act
        var result = await service.DownloadAsync("IVV", new DateTime(2025, 01, 20));

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Value.TryGetProperty("aaData", out var aaData));
        Assert.Equal(2, aaData.GetArrayLength());
    }

    /// <summary>
    /// AC1.2: BOM-prefixed responses are handled without parse errors.
    /// </summary>
    [Fact]
    public async Task DownloadAsync_WithBomPrefix_ParsesSuccessfully()
    {
        // Arrange
        var (service, mockHandler) = BuildServiceWithMockedHttp();
        var validJson = CreateValidISharesJson();
        var bomPrefixedJson = "\uFEFF" + validJson;

        mockHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(bomPrefixedJson)
            });

        // Act
        var result = await service.DownloadAsync("IVV");

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Value.TryGetProperty("aaData", out var aaData));
        Assert.Equal(2, aaData.GetArrayLength());
    }

    /// <summary>
    /// AC1.3: Unknown ETF ticker returns null, no exception thrown.
    /// </summary>
    [Fact]
    public async Task DownloadAsync_WithUnknownTicker_ReturnsNull()
    {
        // Arrange
        var (service, _) = BuildServiceWithMockedHttp();

        // Act
        var result = await service.DownloadAsync("ZZZZZ");

        // Assert
        Assert.Null(result);
    }

    /// <summary>
    /// AC1.4: Network timeout after 60s returns null with logged error, no exception propagated.
    /// </summary>
    [Fact]
    public async Task DownloadAsync_WithTimeout_ReturnsNullAndLogsError()
    {
        // Arrange
        var (service, mockHandler) = BuildServiceWithMockedHttp();
        var loggedMessages = new List<string>();

        service.LogMessage += (msg) => loggedMessages.Add(msg);

        mockHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new TaskCanceledException());

        // Act
        var result = await service.DownloadAsync("IVV");

        // Assert
        Assert.Null(result);
        Assert.Contains(loggedMessages, msg => msg.Contains("timeout", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// AC1.5: Weekend as-of date is adjusted to last business day (Friday).
    /// Verifies the HTTP request URL contains the adjusted date.
    /// </summary>
    [Fact]
    public async Task DownloadAsync_WithSaturdayDate_AdjustsToFriday()
    {
        // Arrange
        var (service, mockHandler) = BuildServiceWithMockedHttp();
        var validJson = CreateValidISharesJson();
        var capturedRequest = new HttpRequestMessage();

        mockHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, ct) =>
            {
                capturedRequest = req;
            })
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(validJson)
            });

        // Saturday, January 25, 2025
        var saturdayDate = new DateTime(2025, 01, 25);

        // Act
        var result = await service.DownloadAsync("IVV", saturdayDate);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(capturedRequest.RequestUri);

        // The URL should contain asOfDate=20250124 (Friday)
        var url = capturedRequest.RequestUri.ToString();
        Assert.Contains("asOfDate=20250124", url);
    }

    /// <summary>
    /// AC1.4: HTTP error (non-200 status) returns null with logged error.
    /// </summary>
    [Fact]
    public async Task DownloadAsync_WithHttpError_ReturnsNullAndLogsError()
    {
        // Arrange
        var (service, mockHandler) = BuildServiceWithMockedHttp();
        var loggedMessages = new List<string>();

        service.LogMessage += (msg) => loggedMessages.Add(msg);

        mockHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.InternalServerError
            });

        // Act
        var result = await service.DownloadAsync("IVV");

        // Assert
        Assert.Null(result);
        Assert.Contains(loggedMessages, msg => msg.Contains("HTTP", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// AC1.4: Malformed JSON returns null with logged error, no exception propagated.
    /// </summary>
    [Fact]
    public async Task DownloadAsync_WithMalformedJson_ReturnsNullAndLogsError()
    {
        // Arrange
        var (service, mockHandler) = BuildServiceWithMockedHttp();
        var loggedMessages = new List<string>();

        service.LogMessage += (msg) => loggedMessages.Add(msg);

        mockHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent("not valid json")
            });

        // Act
        var result = await service.DownloadAsync("IVV");

        // Assert
        Assert.Null(result);
        Assert.Contains(loggedMessages, msg => msg.Contains("parse", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// AC1.5: Verify Sunday also adjusts to Friday (not Saturday).
    /// </summary>
    [Fact]
    public async Task DownloadAsync_WithSundayDate_AdjustsToFriday()
    {
        // Arrange
        var (service, mockHandler) = BuildServiceWithMockedHttp();
        var validJson = CreateValidISharesJson();
        var capturedRequest = new HttpRequestMessage();

        mockHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, ct) =>
            {
                capturedRequest = req;
            })
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(validJson)
            });

        // Sunday, January 26, 2025
        var sundayDate = new DateTime(2025, 01, 26);

        // Act
        var result = await service.DownloadAsync("IVV", sundayDate);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(capturedRequest.RequestUri);

        // The URL should contain asOfDate=20250124 (Friday)
        var url = capturedRequest.RequestUri.ToString();
        Assert.Contains("asOfDate=20250124", url);
    }

    /// <summary>
    /// AC1.5: Verify weekday dates are not adjusted.
    /// </summary>
    [Fact]
    public async Task DownloadAsync_WithWeekdayDate_NotAdjusted()
    {
        // Arrange
        var (service, mockHandler) = BuildServiceWithMockedHttp();
        var validJson = CreateValidISharesJson();
        var capturedRequest = new HttpRequestMessage();

        mockHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, ct) =>
            {
                capturedRequest = req;
            })
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(validJson)
            });

        // Monday, January 20, 2025
        var mondayDate = new DateTime(2025, 01, 20);

        // Act
        var result = await service.DownloadAsync("IVV", mondayDate);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(capturedRequest.RequestUri);

        // The URL should contain asOfDate=20250120 (Monday, unchanged)
        var url = capturedRequest.RequestUri.ToString();
        Assert.Contains("asOfDate=20250120", url);
    }

    /// <summary>
    /// EtfConfigs property contains loaded ETF configurations.
    /// </summary>
    [Fact]
    public void EtfConfigs_ContainsLoadedConfigurations()
    {
        // Arrange
        var (service, _) = BuildServiceWithMockedHttp();

        // Act
        var configs = service.EtfConfigs;

        // Assert
        Assert.NotNull(configs);
        Assert.NotEmpty(configs);
        Assert.True(configs.ContainsKey("IVV"));
    }

    /// <summary>
    /// EtfConfigs property includes ProductId and Slug for URL construction.
    /// </summary>
    [Fact]
    public void EtfConfigs_IncludesRequiredFields()
    {
        // Arrange
        var (service, _) = BuildServiceWithMockedHttp();

        // Act
        var configs = service.EtfConfigs;
        var ivvConfig = configs["IVV"];

        // Assert
        Assert.NotNull(ivvConfig);
        Assert.True(ivvConfig.ProductId > 0);
        Assert.NotEmpty(ivvConfig.Slug);
        Assert.NotEmpty(ivvConfig.IndexCode);
    }
}
