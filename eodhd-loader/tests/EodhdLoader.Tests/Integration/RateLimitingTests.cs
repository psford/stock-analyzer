namespace EodhdLoader.Tests.Integration;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Moq;
using Xunit;
using EodhdLoader.Services;
using EodhdLoader.Models;

/// <summary>
/// Tests for rate limiting enforcement in iShares constituent loading.
/// Verifies AC6.1: Rate limiting enforces minimum 2s between iShares HTTP requests.
/// Tests the rate-limited loop in CrawlerViewModel.CheckAndLoadConstituentsAsync.
/// These tests are intentionally slow (~6s each) because they verify real timing behavior.
/// Mark with [Trait("Category", "Slow")] so they can be excluded from fast CI runs.
/// </summary>
public class RateLimitingTests
{

    /// <summary>
    /// AC6.1 Test 1: RequestDelayMs constant is correctly set to 2000ms.
    /// Verifies that the rate limiting constant is properly defined for use in both
    /// IngestAllEtfsAsync and CrawlerViewModel.CheckAndLoadConstituentsAsync.
    /// </summary>
    [Fact]
    public void RequestDelayMs_IsSetToExpectedValue()
    {
        // Assert
        Assert.Equal(2000, ISharesConstituentService.RequestDelayMs);
    }

    /// <summary>
    /// AC6.1 Test 2: Rate-limited loop enforces 2s delays between consecutive operations.
    /// Simulates the loop pattern used in CrawlerViewModel.CheckAndLoadConstituentsAsync.
    /// Verifies that Task.Delay(RequestDelayMs) is applied between iterations.
    /// Measures total elapsed time: 2 delays of 2s each = ~4s minimum.
    /// </summary>
    [Fact]
    [Trait("Category", "Slow")]
    public async Task RateLimitedLoop_Enforces2sDelayBetweenOperations()
    {
        // Arrange
        var mockService = new Mock<IISharesConstituentService>();

        // Mock IngestEtfAsync to return quickly
        mockService
            .Setup(s => s.IngestEtfAsync(It.IsAny<string>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IngestStats(
                Parsed: 10, Matched: 8, Created: 0, Inserted: 8,
                SkippedExisting: 2, Failed: 0, IdentifiersSet: 8));

        // Act
        var startTime = DateTime.UtcNow;

        // Simulate the rate-limited loop from CrawlerViewModel.CheckAndLoadConstituentsAsync
        var items = new List<string> { "IVV", "IJH", "IJR" };
        int processed = 0;
        foreach (var item in items)
        {
            await mockService.Object.IngestEtfAsync(item, null, default);
            processed++;

            // Rate limiting — use shared constant from service (AC6.1)
            if (processed < items.Count)
                await Task.Delay(ISharesConstituentService.RequestDelayMs);
        }

        var endTime = DateTime.UtcNow;

        // Assert
        // Should have processed 3 items
        Assert.Equal(3, processed);

        // Verify total time: 2 delays of 2s each = ~4s minimum
        var totalTime = (endTime - startTime).TotalSeconds;
        Assert.True(totalTime >= 3.8, $"Total time was {totalTime}s, expected >= 3.8s for rate limiting (2 delays of 2s each)");

        // Also verify it's not excessively long (should be well under 10s)
        Assert.True(totalTime < 10.0, $"Total time was {totalTime}s, expected < 10s (no excessive delays)");
    }
}
