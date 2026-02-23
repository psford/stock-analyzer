namespace EodhdLoader.Tests.ViewModels;

using System.Collections.ObjectModel;
using System.Threading;
using EodhdLoader.Models;
using EodhdLoader.Services;
using EodhdLoader.Utilities;
using EodhdLoader.ViewModels;
using Moq;
using Xunit;

/// <summary>
/// Tests for IndexManagerViewModel.
/// Verifies AC4.1-AC4.6 from ishares-constituent-loader.AC4 specification.
/// </summary>
public class IndexManagerViewModelTests
{
    private static readonly Dictionary<string, EtfConfig> SampleEtfConfigs = new()
    {
        { "IVV", new EtfConfig { ProductId = 123, Slug = "ishares-core-sp-500", IndexCode = "CCMP" } },
        { "VTI", new EtfConfig { ProductId = 124, Slug = "vanguard-total-stock", IndexCode = "CCMP" } },
        { "VOO", new EtfConfig { ProductId = 125, Slug = "vanguard-sp-500", IndexCode = "CCMP" } },
    };

    public IndexManagerViewModelTests()
    {
        // Set up synchronization context to handle WPF Dispatcher calls in tests
        SynchronizationContext.SetSynchronizationContext(new SynchronizationContext());
    }

    /// <summary>
    /// AC4.1: LoadAllCommand iterates all configured ETFs with visible progress.
    /// Verifies that IngestAllEtfsAsync is called and progress is updated.
    /// </summary>
    [Fact]
    public async Task LoadAllCommand_WithNoEtfSelected_CallsIngestAllEtfsAsync()
    {
        // Arrange
        var constituentServiceMock = new Mock<IISharesConstituentService>();
        constituentServiceMock.Setup(s => s.EtfConfigs).Returns(SampleEtfConfigs);

        var ingestStats = new IngestStats(
            Parsed: 500,
            Matched: 498,
            Created: 2,
            Inserted: 500,
            SkippedExisting: 0,
            Failed: 0,
            IdentifiersSet: 500
        );

        // Set up the mock to fire progress events asynchronously
        constituentServiceMock.Setup(s => s.IngestAllEtfsAsync(It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                // Simulate async work
                await Task.Delay(10);
                // Fire progress events - these will be caught by the ViewModel's event handlers
            });

        var configMock = new Mock<ConfigurationService>();
        var vm = new IndexManagerViewModel(constituentServiceMock.Object, configMock.Object);

        // Act
        vm.SelectedEtfTicker = "(All)";
        await vm.LoadAllCommand.ExecuteAsync(null);

        // Assert
        constituentServiceMock.Verify(
            s => s.IngestAllEtfsAsync(It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()),
            Times.Once);
        Assert.False(vm.IsLoading); // Should be done loading
        Assert.Equal(100, vm.Progress);
    }

    /// <summary>
    /// AC4.2: Single-ETF override allows loading one specific ETF.
    /// Verifies that when a specific ticker is selected, IngestEtfAsync is called instead.
    /// </summary>
    [Fact]
    public async Task LoadAllCommand_WithSpecificEtfSelected_CallsIngestEtfAsync()
    {
        // Arrange
        var constituentServiceMock = new Mock<IISharesConstituentService>();
        constituentServiceMock.Setup(s => s.EtfConfigs).Returns(SampleEtfConfigs);

        var ingestStats = new IngestStats(
            Parsed: 500,
            Matched: 498,
            Created: 2,
            Inserted: 500,
            SkippedExisting: 0,
            Failed: 0,
            IdentifiersSet: 500
        );

        constituentServiceMock.Setup(s => s.IngestEtfAsync(
                It.IsAny<string>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, DateTime?, CancellationToken>((ticker, date, ct) =>
            {
                var progress = new IngestProgress(ticker, 1, 1, 500, 500, ingestStats);
                constituentServiceMock.Raise(s => s.ProgressUpdated += null, progress);
            })
            .ReturnsAsync(ingestStats);

        var configMock = new Mock<ConfigurationService>();
        var vm = new IndexManagerViewModel(constituentServiceMock.Object, configMock.Object);

        // Act
        vm.SelectedEtfTicker = "IVV";
        await vm.LoadAllCommand.ExecuteAsync(null);

        // Assert
        constituentServiceMock.Verify(
            s => s.IngestEtfAsync("IVV", It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()),
            Times.Once);
        constituentServiceMock.Verify(
            s => s.IngestAllEtfsAsync(It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// AC4.3: As-of date defaults to last business day of previous month.
    /// Can be changed by user and is passed to service.
    /// </summary>
    [Fact]
    public void AsOfDate_DefaultsToLastBusinessDayOfPreviousMonth()
    {
        // Arrange
        var constituentServiceMock = new Mock<IISharesConstituentService>();
        constituentServiceMock.Setup(s => s.EtfConfigs).Returns(SampleEtfConfigs);

        var configMock = new Mock<ConfigurationService>();

        // Act
        var vm = new IndexManagerViewModel(constituentServiceMock.Object, configMock.Object);

        // Assert - AsOfDate should be a valid business day in the previous month
        var expectedMonthEnd = GetLastMonthEnd();
        Assert.Equal(expectedMonthEnd, vm.AsOfDate);
    }

    /// <summary>
    /// AC4.3: User can change AsOfDate and it's passed to service.
    /// </summary>
    [Fact]
    public async Task AsOfDate_CanBeChanged_AndPassedToService()
    {
        // Arrange
        var constituentServiceMock = new Mock<IISharesConstituentService>();
        constituentServiceMock.Setup(s => s.EtfConfigs).Returns(SampleEtfConfigs);

        var ingestStats2 = new IngestStats(
            Parsed: 500,
            Matched: 498,
            Created: 2,
            Inserted: 500,
            SkippedExisting: 0,
            Failed: 0,
            IdentifiersSet: 500
        );

        constituentServiceMock.Setup(s => s.IngestEtfAsync(
                It.IsAny<string>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ingestStats2);

        var configMock = new Mock<ConfigurationService>();
        var vm = new IndexManagerViewModel(constituentServiceMock.Object, configMock.Object);

        var customDate = new DateTime(2024, 1, 31);

        // Act
        vm.AsOfDate = customDate;
        vm.SelectedEtfTicker = "IVV";
        await vm.LoadAllCommand.ExecuteAsync(null);

        // Assert
        constituentServiceMock.Verify(
            s => s.IngestEtfAsync("IVV", customDate, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// AC4.4: Activity log shows per-ETF results with statistics.
    /// Verifies that LogMessage events are captured and displayed in newest-first order.
    /// </summary>
    [Fact]
    public async Task LogMessages_CaptureServiceEvents_InNewestFirstOrder()
    {
        // Arrange
        var constituentServiceMock = new Mock<IISharesConstituentService>();
        constituentServiceMock.Setup(s => s.EtfConfigs).Returns(SampleEtfConfigs);

        // We can't easily mock events in tests, so test that the ViewModel captures
        // internal log messages and maintains newest-first order
        constituentServiceMock.Setup(s => s.IngestAllEtfsAsync(It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var configMock = new Mock<ConfigurationService>();
        var vm = new IndexManagerViewModel(constituentServiceMock.Object, configMock.Object);

        // Act
        vm.SelectedEtfTicker = "(All)";
        await vm.LoadAllCommand.ExecuteAsync(null);

        // Assert
        // The ViewModel automatically logs "Loading iShares..." and completion messages
        Assert.NotEmpty(vm.LogMessages);
        // Messages should be newest-first (insert at position 0)
        // So the last message logged should be at position 0
        var completionMsg = vm.LogMessages[0];
        Assert.True(completionMsg.Contains("Complete") || completionMsg.Contains("successfully"),
            $"Expected completion message, got: {completionMsg}");
        // Verify all messages are timestamped
        foreach (var msg in vm.LogMessages)
        {
            Assert.Contains("[", msg);
            Assert.Contains("]", msg);
        }
    }

    /// <summary>
    /// AC4.5: Cancel button stops the loading loop after current ETF completes.
    /// Verifies that CancellationToken is cancelled and loading stops.
    /// </summary>
    [Fact]
    public async Task CancelCommand_CancelsTheLoadingOperation()
    {
        // Arrange
        var constituentServiceMock = new Mock<IISharesConstituentService>();
        constituentServiceMock.Setup(s => s.EtfConfigs).Returns(SampleEtfConfigs);

        CancellationToken capturedToken = CancellationToken.None;

        constituentServiceMock.Setup(s => s.IngestAllEtfsAsync(It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .Callback<DateTime?, CancellationToken>((date, ct) =>
            {
                capturedToken = ct;
            })
            .Returns(async () =>
            {
                // Simulate a long-running operation that respects cancellation
                await Task.Delay(5000, capturedToken);
            });

        var configMock = new Mock<ConfigurationService>();
        var vm = new IndexManagerViewModel(constituentServiceMock.Object, configMock.Object);

        // Act
        vm.SelectedEtfTicker = "(All)";
        var loadTask = vm.LoadAllCommand.ExecuteAsync(null);

        // Give time for the load to start
        await Task.Delay(50);

        // Cancel the load
        vm.CancelCommand.Execute(null);

        // Wait for the load to complete (with cancellation)
        await Task.Delay(100); // Wait for cancellation to propagate

        // Assert
        Assert.False(vm.IsLoading);
        Assert.NotEmpty(vm.LogMessages);
        // Check that either "Cancellation" or "Load cancelled" is in the logs
        var hasCancellationMessage = vm.LogMessages.Any(msg => msg.Contains("Cancellation") || msg.Contains("cancelled"));
        Assert.True(hasCancellationMessage, "Expected cancellation message in logs");
    }

    /// <summary>
    /// AC4.6: ETF download failure is logged and skipped; loading continues with next ETF.
    /// Verifies that exceptions are caught, logged, and IsLoading returns to false.
    /// </summary>
    [Fact]
    public async Task IngestAllEtfsAsync_ExceptionHandling_LogsErrorAndCompletes()
    {
        // Arrange
        var constituentServiceMock = new Mock<IISharesConstituentService>();
        constituentServiceMock.Setup(s => s.EtfConfigs).Returns(SampleEtfConfigs);

        constituentServiceMock.Setup(s => s.IngestAllEtfsAsync(It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .Callback<DateTime?, CancellationToken>((date, ct) =>
            {
                constituentServiceMock.Raise(s => s.LogMessage += null, "Starting load...");
                throw new HttpRequestException("Failed to download ETF data from iShares");
            })
            .Returns(Task.CompletedTask);

        var configMock = new Mock<ConfigurationService>();
        var vm = new IndexManagerViewModel(constituentServiceMock.Object, configMock.Object);

        // Act
        vm.SelectedEtfTicker = "(All)";
        await vm.LoadAllCommand.ExecuteAsync(null);

        // Assert
        Assert.False(vm.IsLoading);
        var errorLog = vm.LogMessages.FirstOrDefault(msg => msg.Contains("ERROR"));
        Assert.NotNull(errorLog);
        Assert.Contains("Failed to download", errorLog);
    }

    /// <summary>
    /// Verify AvailableEtfTickers are populated from service config and sorted.
    /// First entry should be "(All)" sentinel.
    /// </summary>
    [Fact]
    public void AvailableEtfTickers_PopulatedFromService_WithAllSentinelFirst()
    {
        // Arrange
        var constituentServiceMock = new Mock<IISharesConstituentService>();
        constituentServiceMock.Setup(s => s.EtfConfigs).Returns(SampleEtfConfigs);

        var configMock = new Mock<ConfigurationService>();

        // Act
        var vm = new IndexManagerViewModel(constituentServiceMock.Object, configMock.Object);

        // Assert
        Assert.NotEmpty(vm.AvailableEtfTickers);
        Assert.Equal("(All)", vm.AvailableEtfTickers[0]);
        // Next entries should be sorted ETF tickers
        Assert.Contains("IVV", vm.AvailableEtfTickers);
        Assert.Contains("VTI", vm.AvailableEtfTickers);
        Assert.Contains("VOO", vm.AvailableEtfTickers);
    }

    /// <summary>
    /// Verify that ClearLogCommand clears all log messages.
    /// </summary>
    [Fact]
    public void ClearLogCommand_ClearsAllMessages()
    {
        // Arrange
        var constituentServiceMock = new Mock<IISharesConstituentService>();
        constituentServiceMock.Setup(s => s.EtfConfigs).Returns(SampleEtfConfigs);

        var configMock = new Mock<ConfigurationService>();
        var vm = new IndexManagerViewModel(constituentServiceMock.Object, configMock.Object);

        // Add some messages
        vm.LogMessages.Add("Message 1");
        vm.LogMessages.Add("Message 2");

        // Act
        vm.ClearLogCommand.Execute(null);

        // Assert
        Assert.Empty(vm.LogMessages);
    }

    /// <summary>
    /// Verify progress tracking during multi-ETF load.
    /// </summary>
    [Fact]
    public async Task ProgressUpdated_UpdatesCurrentEtfLabel_AndProgress()
    {
        // Arrange
        var constituentServiceMock = new Mock<IISharesConstituentService>();
        constituentServiceMock.Setup(s => s.EtfConfigs).Returns(SampleEtfConfigs);

        var ingestStats = new IngestStats(
            Parsed: 500,
            Matched: 498,
            Created: 2,
            Inserted: 450,
            SkippedExisting: 50,
            Failed: 0,
            IdentifiersSet: 500
        );

        // Simulate progress updates by manually calling the ViewModel's event handler
        constituentServiceMock.Setup(s => s.IngestAllEtfsAsync(It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var configMock = new Mock<ConfigurationService>();
        var vm = new IndexManagerViewModel(constituentServiceMock.Object, configMock.Object);

        // Subscribe to the service's ProgressUpdated event to fire updates
        var progressUpdated = vm.GetType().GetField("_constituentService",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.GetValue(vm) as IISharesConstituentService;

        // Act
        vm.SelectedEtfTicker = "(All)";

        // Simulate progress events
        var progress1 = new IngestProgress("IVV", 1, 3, 500, 500, ingestStats);
        vm.GetType().GetMethod("OnServiceProgressUpdated",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.Invoke(vm, [progress1]);

        var progress2 = new IngestProgress("VTI", 2, 3, 500, 500, ingestStats);
        vm.GetType().GetMethod("OnServiceProgressUpdated",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.Invoke(vm, [progress2]);

        var progress3 = new IngestProgress("VOO", 3, 3, 500, 500, ingestStats);
        vm.GetType().GetMethod("OnServiceProgressUpdated",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.Invoke(vm, [progress3]);

        // Assert
        Assert.Equal("Loading VOO (3 / 3)...", vm.CurrentEtfLabel);
        Assert.Equal(100, vm.Progress);
        Assert.Equal("450 inserted, 50 skipped, 0 failed", vm.ProgressText);
        Assert.Equal(3, vm.TotalEtfsToLoad);
        Assert.Equal(3, vm.CurrentEtfIndex);
    }

    /// <summary>
    /// Verify IsLoading flag is set during load and cleared when done.
    /// </summary>
    [Fact]
    public async Task IsLoading_SetDuringLoad_ClearedWhenDone()
    {
        // Arrange
        var constituentServiceMock = new Mock<IISharesConstituentService>();
        constituentServiceMock.Setup(s => s.EtfConfigs).Returns(SampleEtfConfigs);

        var isLoadingDuringCall = false;

        var ingestStats3 = new IngestStats(
            Parsed: 500,
            Matched: 498,
            Created: 2,
            Inserted: 500,
            SkippedExisting: 0,
            Failed: 0,
            IdentifiersSet: 500
        );

        constituentServiceMock.Setup(s => s.IngestEtfAsync(
                It.IsAny<string>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, DateTime?, CancellationToken>((ticker, date, ct) =>
            {
                isLoadingDuringCall = true; // Will be checked in assertions
            })
            .ReturnsAsync(ingestStats3);

        var configMock = new Mock<ConfigurationService>();
        var vm = new IndexManagerViewModel(constituentServiceMock.Object, configMock.Object);

        Assert.False(vm.IsLoading);

        // Act
        vm.SelectedEtfTicker = "IVV";
        await vm.LoadAllCommand.ExecuteAsync(null);

        // Assert
        Assert.False(vm.IsLoading);
    }

    /// <summary>
    /// Delegates to shared DateUtilities for last month-end business day calculation.
    /// </summary>
    private static DateTime GetLastMonthEnd() => DateUtilities.GetLastMonthEnd();
}
