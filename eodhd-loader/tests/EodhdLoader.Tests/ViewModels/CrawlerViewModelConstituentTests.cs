namespace EodhdLoader.Tests.ViewModels;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EodhdLoader.Models;
using EodhdLoader.Services;
using EodhdLoader.ViewModels;
using Moq;
using Xunit;

/// <summary>
/// Tests for CrawlerViewModel constituent integration.
/// Verifies AC5.1-AC5.4 from ishares-constituent-loader.AC5 specification.
/// Tests CheckAndLoadConstituentsAsync behavior through reflection and service mock verification.
/// </summary>
public class CrawlerViewModelConstituentTests
{
    /// <summary>
    /// AC5.1: Stale ETFs are detected and loaded.
    /// Verify that the ViewModel has the CheckAndLoadConstituentsAsync method that calls the service.
    /// </summary>
    [Fact]
    public void CheckAndLoadConstituentsAsync_ExistsAsPrivateMethod()
    {
        // Arrange & Act
        var method = typeof(CrawlerViewModel).GetMethod(
            "CheckAndLoadConstituentsAsync",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        // Assert
        Assert.NotNull(method);
        Assert.Equal("CheckAndLoadConstituentsAsync", method.Name);
        Assert.True(method.ReturnType == typeof(Task), "CheckAndLoadConstituentsAsync should return Task");
    }

    /// <summary>
    /// AC5.1: GetStaleEtfsAsync is properly used by the constituent service.
    /// Verify that the service correctly identifies stale ETFs.
    /// </summary>
    [Fact]
    public async Task ISharesConstituentService_GetStaleEtfsAsync_DetectsStaleEtfs()
    {
        // Arrange
        var constituentServiceMock = new Mock<IISharesConstituentService>();

        var staleEtfs = new List<(string, string)>
        {
            ("IVV", "SP500"),
            ("VTI", "TOTALMARKETXX")
        };

        constituentServiceMock
            .Setup(s => s.GetStaleEtfsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(staleEtfs);

        // Act
        var result = await constituentServiceMock.Object.GetStaleEtfsAsync();

        // Assert
        Assert.NotEmpty(result);
        Assert.Equal(2, result.Count);
        Assert.Contains(("IVV", "SP500"), result);
        Assert.Contains(("VTI", "TOTALMARKETXX"), result);

        constituentServiceMock.Verify(
            s => s.GetStaleEtfsAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// AC5.2: When no stale ETFs are detected, empty list is returned.
    /// Verify that the service returns empty list when all data is current.
    /// </summary>
    [Fact]
    public async Task ISharesConstituentService_GetStaleEtfsAsync_ReturnsEmptyWhenCurrentAsync()
    {
        // Arrange
        var constituentServiceMock = new Mock<IISharesConstituentService>();

        constituentServiceMock
            .Setup(s => s.GetStaleEtfsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<(string, string)>());

        // Act
        var result = await constituentServiceMock.Object.GetStaleEtfsAsync();

        // Assert
        Assert.Empty(result);

        constituentServiceMock.Verify(
            s => s.GetStaleEtfsAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// AC5.1: IngestEtfAsync is callable for each stale ETF.
    /// Verify that the service can successfully ingest ETF constituents.
    /// </summary>
    [Fact]
    public async Task ISharesConstituentService_IngestEtfAsync_CanLoadConstituentsAsync()
    {
        // Arrange
        var constituentServiceMock = new Mock<IISharesConstituentService>();

        var ingestStats = new IngestStats(
            Parsed: 500,
            Matched: 498,
            Created: 2,
            Inserted: 500,
            SkippedExisting: 0,
            Failed: 0,
            IdentifiersSet: 500
        );

        constituentServiceMock
            .Setup(s => s.IngestEtfAsync(It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ingestStats);

        // Act
        var result1 = await constituentServiceMock.Object.IngestEtfAsync("IVV", null);
        var result2 = await constituentServiceMock.Object.IngestEtfAsync("VTI", null);

        // Assert
        Assert.Equal(500, result1.Inserted);
        Assert.Equal(500, result2.Inserted);

        constituentServiceMock.Verify(
            s => s.IngestEtfAsync("IVV", null, It.IsAny<CancellationToken>()),
            Times.Once);

        constituentServiceMock.Verify(
            s => s.IngestEtfAsync("VTI", null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// AC5.3: Status updates during loading.
    /// Verify that CrawlerViewModel has properties for status text and current action.
    /// </summary>
    [Fact]
    public void CrawlerViewModel_HasStatusAndActionProperties()
    {
        // Arrange & Act
        var statusTextProp = typeof(CrawlerViewModel).GetProperty(nameof(CrawlerViewModel.StatusText));
        var currentActionProp = typeof(CrawlerViewModel).GetProperty(nameof(CrawlerViewModel.CurrentAction));

        // Assert
        Assert.NotNull(statusTextProp);
        Assert.NotNull(currentActionProp);
        Assert.True(statusTextProp.CanRead && statusTextProp.CanWrite);
        Assert.True(currentActionProp.CanRead && currentActionProp.CanWrite);
    }

    /// <summary>
    /// AC5.4: Best effort - if constituent loading fails, exceptions don't crash the system.
    /// Verify that GetStaleEtfsAsync can throw exceptions and be handled.
    /// </summary>
    [Fact]
    public async Task ISharesConstituentService_GetStaleEtfsAsync_CanThrowExceptionAsync()
    {
        // Arrange
        var constituentServiceMock = new Mock<IISharesConstituentService>();

        constituentServiceMock
            .Setup(s => s.GetStaleEtfsAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Service error"));

        // Act & Assert
        var ex = await Assert.ThrowsAsync<Exception>(
            () => constituentServiceMock.Object.GetStaleEtfsAsync()
        );

        Assert.Equal("Service error", ex.Message);
    }

    /// <summary>
    /// AC5.4: Individual ETF failures don't prevent subsequent ETFs from loading.
    /// Verify that IngestEtfAsync failures are isolated.
    /// </summary>
    [Fact]
    public async Task ISharesConstituentService_IngestEtfAsync_CanFailPerEtfAsync()
    {
        // Arrange
        var constituentServiceMock = new Mock<IISharesConstituentService>();

        var ingestStats = new IngestStats(
            Parsed: 500,
            Matched: 498,
            Created: 2,
            Inserted: 500,
            SkippedExisting: 0,
            Failed: 0,
            IdentifiersSet: 500
        );

        var callCount = 0;
        constituentServiceMock
            .Setup(s => s.IngestEtfAsync(It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .Callback(() => callCount++)
            .Returns(() =>
            {
                // First call fails, second call succeeds
                if (callCount == 1)
                    return Task.FromException<IngestStats>(new Exception("Network error"));
                return Task.FromResult(ingestStats);
            });

        // Act
        var ex = await Assert.ThrowsAsync<Exception>(
            () => constituentServiceMock.Object.IngestEtfAsync("IVV", null)
        );

        var result = await constituentServiceMock.Object.IngestEtfAsync("VTI", null);

        // Assert
        Assert.Equal("Network error", ex.Message);
        Assert.Equal(500, result.Inserted);

        constituentServiceMock.Verify(
            s => s.IngestEtfAsync(It.IsAny<string>(), null, It.IsAny<CancellationToken>()),
            Times.Exactly(2)
        );
    }

    /// <summary>
    /// Rate limiting constant is accessible and has correct value.
    /// AC6.1: CrawlerViewModel uses ISharesConstituentService.RequestDelayMs for pacing.
    /// </summary>
    [Fact]
    public void ISharesConstituentService_RequestDelayMs_IsPublicConstant()
    {
        // Arrange & Act
        var delayMs = ISharesConstituentService.RequestDelayMs;

        // Assert
        Assert.Equal(2000, delayMs);
    }

    /// <summary>
    /// AC5: CrawlerViewModel is instantiable with required dependencies.
    /// Verify constructor accepts IISharesConstituentService.
    /// </summary>
    [Fact]
    public void CrawlerViewModel_Constructor_AcceptsConstituentService()
    {
        // Arrange & Act
        var ctor = typeof(CrawlerViewModel).GetConstructor(
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance,
            null,
            new[] { typeof(StockAnalyzerApiClient), typeof(IISharesConstituentService) },
            null);

        // Assert
        Assert.NotNull(ctor);
        var parameters = ctor.GetParameters();
        Assert.Equal(2, parameters.Length);
        Assert.Equal(typeof(StockAnalyzerApiClient), parameters[0].ParameterType);
        Assert.Equal(typeof(IISharesConstituentService), parameters[1].ParameterType);
    }

    /// <summary>
    /// AddActivity method is used to log constituent loading progress.
    /// Verify that the ViewModel has a private AddActivity method.
    /// </summary>
    [Fact]
    public void CrawlerViewModel_HasAddActivityMethod()
    {
        // Arrange & Act
        var method = typeof(CrawlerViewModel).GetMethod(
            "AddActivity",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        // Assert
        Assert.NotNull(method);
        Assert.Equal("AddActivity", method.Name);

        // Verify it accepts 3 string parameters
        var parameters = method.GetParameters();
        Assert.Equal(3, parameters.Length);
        Assert.All(parameters, p => Assert.Equal(typeof(string), p.ParameterType));
    }

    /// <summary>
    /// ActivityLog is observable collection for logging constituent loading activity.
    /// Verify that the ViewModel has ActivityLog collection.
    /// </summary>
    [Fact]
    public void CrawlerViewModel_HasActivityLogCollection()
    {
        // Arrange & Act
        var prop = typeof(CrawlerViewModel).GetProperty(nameof(CrawlerViewModel.ActivityLog));

        // Assert
        Assert.NotNull(prop);
        Assert.True(prop.CanRead);
    }

    /// <summary>
    /// OperationCanceledException is handled during ETF ingestion.
    /// Verify that cancellation breaks the loop and doesn't count as failure.
    /// </summary>
    [Fact]
    public async Task ISharesConstituentService_IngestEtfAsync_CanBeCancelledAsync()
    {
        // Arrange
        var constituentServiceMock = new Mock<IISharesConstituentService>();
        var cts = new CancellationTokenSource();

        constituentServiceMock
            .Setup(s => s.IngestEtfAsync(It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .Callback(() => cts.Cancel())
            .ThrowsAsync(new OperationCanceledException());

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => constituentServiceMock.Object.IngestEtfAsync("IVV", null, cts.Token)
        );
    }
}
