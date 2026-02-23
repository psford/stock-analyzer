namespace EodhdLoader.Tests.Integration;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using EodhdLoader.Models;
using EodhdLoader.Services;
using EodhdLoader.ViewModels;
using Microsoft.EntityFrameworkCore;
using Moq;
using StockAnalyzer.Core.Data;
using Xunit;

/// <summary>
/// Tests for rate limiting enforcement in iShares constituent loading.
/// Verifies AC6.1: Rate limiting enforces minimum 2s between iShares HTTP requests.
/// Test 1: Exercises IngestAllEtfsAsync on a real ISharesConstituentService instance.
/// Test 2: Exercises CheckAndLoadConstituentsAsync on a real CrawlerViewModel instance.
/// Both tests are intentionally slow (~6s each) because they verify real timing behavior.
/// </summary>
public class RateLimitingTests
{
    /// <summary>
    /// AC6.1 Test 1: IngestAllEtfsAsync enforces 2s delays between consecutive HTTP requests.
    /// Creates a real ISharesConstituentService with mocked HttpMessageHandler.
    /// Limits EtfConfigs to 3 entries via reflection. Calls IngestAllEtfsAsync().
    /// Records HTTP request timestamps and asserts >= 1.9s gap between consecutive requests.
    /// </summary>
    [Fact]
    [Trait("Category", "Slow")]
    public async Task IngestAllEtfsAsync_EnforcesMinimum2sDelayBetweenRequests()
    {
        // Arrange: Create real service with mocked HTTP handler that records timestamps
        var requestTimestamps = new List<DateTime>();
        var mockHandler = new TimestampRecordingHandler(requestTimestamps);
        using var httpClient = new HttpClient(mockHandler);
        using var dbContext = CreateInMemoryDbContext();

        var service = new ISharesConstituentService(httpClient, dbContext);

        // Limit EtfConfigs to exactly 3 entries via reflection (avoids 277-ETF test duration)
        var configsField = typeof(ISharesConstituentService)
            .GetField("_etfConfigs", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var fullConfigs = (Dictionary<string, EtfConfig>)configsField.GetValue(service)!;

        Assert.True(fullConfigs.Count >= 3, $"Need at least 3 ETF configs to test rate limiting, found {fullConfigs.Count}");

        var limitedConfigs = new Dictionary<string, EtfConfig>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in fullConfigs.Take(3))
            limitedConfigs[kvp.Key] = kvp.Value;
        configsField.SetValue(service, limitedConfigs);

        // Act: Call the real production method
        requestTimestamps.Clear();
        await service.IngestAllEtfsAsync();

        // Assert: Should have made HTTP requests for all 3 ETFs
        Assert.True(requestTimestamps.Count >= 3,
            $"Expected >= 3 HTTP requests, got {requestTimestamps.Count}");

        // Verify gaps between consecutive requests are >= 1.9s (0.1s tolerance for jitter)
        for (int i = 1; i < requestTimestamps.Count; i++)
        {
            var gap = (requestTimestamps[i] - requestTimestamps[i - 1]).TotalSeconds;
            Assert.True(gap >= 1.9,
                $"Gap between request {i - 1} and {i} was {gap:F2}s, expected >= 1.9s for rate limiting");
        }
    }

    /// <summary>
    /// AC6.1 Test 2: CheckAndLoadConstituentsAsync enforces 2s delays between ETF loads.
    /// Creates a CrawlerViewModel (bypassing WPF constructor), sets up mocked dependencies,
    /// calls CheckAndLoadConstituentsAsync directly (internal via InternalsVisibleTo),
    /// and verifies IngestEtfAsync call timestamps have >= 1.9s gaps.
    /// </summary>
    [Fact]
    [Trait("Category", "Slow")]
    public async Task CheckAndLoadConstituentsAsync_EnforcesMinimum2sDelayBetweenRequests()
    {
        // Arrange: Create CrawlerViewModel bypassing constructor (avoids DispatcherTimer dependency)
        var vm = (CrawlerViewModel)RuntimeHelpers.GetUninitializedObject(typeof(CrawlerViewModel));

        // Set up required fields via reflection
        var constituentField = typeof(CrawlerViewModel)
            .GetField("_constituentService", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var ctsField = typeof(CrawlerViewModel)
            .GetField("_cts", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var activityLogField = typeof(CrawlerViewModel)
            .GetField("_activityLog", BindingFlags.Instance | BindingFlags.NonPublic)!;

        // Initialize ActivityLog (needed by AddActivity)
        activityLogField.SetValue(vm, new ObservableCollection<CrawlActivity>());

        // Set up CancellationTokenSource
        using var cts = new CancellationTokenSource();
        ctsField.SetValue(vm, cts);

        // Set up mocked service that records IngestEtfAsync call timestamps
        var callTimestamps = new List<DateTime>();
        var mockService = new Mock<IISharesConstituentService>();

        mockService
            .Setup(s => s.GetStaleEtfsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<(string, string)>
            {
                ("IVV", "SP500"),
                ("IJH", "SP400"),
                ("IJR", "SP600")
            });

        mockService
            .Setup(s => s.IngestEtfAsync(It.IsAny<string>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .Returns<string, DateTime?, CancellationToken>((ticker, date, ct) =>
            {
                callTimestamps.Add(DateTime.UtcNow);
                return Task.FromResult(new IngestStats(
                    Parsed: 10, Matched: 8, Created: 2, Inserted: 10,
                    SkippedExisting: 0, Failed: 0, IdentifiersSet: 8));
            });

        constituentField.SetValue(vm, mockService.Object);

        // Act: Call the real production method (internal via InternalsVisibleTo)
        await vm.CheckAndLoadConstituentsAsync();

        // Assert: Should have called IngestEtfAsync for all 3 stale ETFs
        Assert.Equal(3, callTimestamps.Count);

        // Verify gaps between consecutive calls are >= 1.9s (0.1s tolerance for jitter)
        for (int i = 1; i < callTimestamps.Count; i++)
        {
            var gap = (callTimestamps[i] - callTimestamps[i - 1]).TotalSeconds;
            Assert.True(gap >= 1.9,
                $"Gap between call {i - 1} and {i} was {gap:F2}s, expected >= 1.9s for rate limiting");
        }

        // Verify total time is reasonable (2 gaps * 2s = ~4s)
        var totalTime = (callTimestamps.Last() - callTimestamps.First()).TotalSeconds;
        Assert.True(totalTime >= 3.8, $"Total time was {totalTime:F2}s, expected >= 3.8s");
    }

    /// <summary>
    /// Supplementary: Verify the rate limiting constant has the expected value.
    /// </summary>
    [Fact]
    public void RequestDelayMs_IsSetToExpectedValue()
    {
        Assert.Equal(2000, ISharesConstituentService.RequestDelayMs);
    }

    private static StockAnalyzerDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<StockAnalyzerDbContext>()
            .UseInMemoryDatabase(databaseName: $"rate_limit_test_{Guid.NewGuid()}")
            .Options;

        var context = new StockAnalyzerDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    /// <summary>
    /// HttpMessageHandler that records request timestamps and returns empty JSON.
    /// </summary>
    private class TimestampRecordingHandler : HttpMessageHandler
    {
        private readonly List<DateTime> _timestamps;

        public TimestampRecordingHandler(List<DateTime> timestamps)
        {
            _timestamps = timestamps;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _timestamps.Add(DateTime.UtcNow);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    @"{""aaData"": []}",
                    System.Text.Encoding.UTF8,
                    "application/json")
            });
        }
    }
}
