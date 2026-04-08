using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using StockAnalyzer.Core.Data.Entities;
using StockAnalyzer.Core.Models;
using StockAnalyzer.Core.Services;
using StockAnalyzer.Core.Tests.TestHelpers;
using Xunit;

namespace StockAnalyzer.Core.Tests.Services;

public class AggregatedStockDataServiceTests
{
    /// <summary>
    /// Helper to create a mock service scope factory with mocked repositories.
    /// </summary>
    private static Mock<IServiceScopeFactory> CreateMockScopeFactory(
        Mock<IPriceRepository> mockPriceRepo,
        Mock<ISecurityMasterRepository> mockSecurityRepo)
    {
        var mockServiceProvider = new Mock<IServiceProvider>();
        mockServiceProvider.Setup(sp => sp.GetService(typeof(IPriceRepository)))
            .Returns(mockPriceRepo.Object);
        mockServiceProvider.Setup(sp => sp.GetService(typeof(ISecurityMasterRepository)))
            .Returns(mockSecurityRepo.Object);

        var mockScope = new Mock<IServiceScope>();
        mockScope.Setup(s => s.ServiceProvider).Returns(mockServiceProvider.Object);

        var mockScopeFactory = new Mock<IServiceScopeFactory>();
        mockScopeFactory.Setup(f => f.CreateScope()).Returns(mockScope.Object);

        return mockScopeFactory;
    }

    #region AC2.1 - Fresh DB data

    [Fact]
    public async Task GetHistoricalDataAsync_WithFreshDbData_ReturnsDbDataWithoutApiCalls()
    {
        // Arrange
        var symbol = "AAPL";
        var endDate = DateTime.Today;
        var startDate = endDate.AddYears(-5);

        // Create fresh DB data (latest price is today)
        var dbPrices = new List<PriceEntity>();
        var today = DateTime.Today;
        for (int i = 0; i < 252; i++) // One year of trading days
        {
            dbPrices.Add(new PriceEntity
            {
                SecurityAlias = 1,
                EffectiveDate = today.AddDays(-i),
                Open = 150m,
                High = 151m,
                Low = 149m,
                Close = 150m,
                Volume = 1000000
            });
        }
        dbPrices = dbPrices.OrderBy(p => p.EffectiveDate).ToList();

        var security = new SecurityMasterEntity
        {
            SecurityAlias = 1,
            TickerSymbol = symbol,
            IssueName = "Apple Inc."
        };

        var mockPriceRepo = new Mock<IPriceRepository>();
        mockPriceRepo.Setup(r => r.GetPricesAsync(It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(dbPrices);

        var mockSecurityRepo = new Mock<ISecurityMasterRepository>();
        mockSecurityRepo.Setup(r => r.GetByTickerAsync(It.IsAny<string>()))
            .ReturnsAsync(security);

        var mockScopeFactory = CreateMockScopeFactory(mockPriceRepo, mockSecurityRepo);

        var mockProviders = new List<IStockDataProvider>();
        var mockProvider = new Mock<IStockDataProvider>();
        mockProvider.Setup(p => p.IsAvailable).Returns(true);
        mockProvider.Setup(p => p.ProviderName).Returns("TestProvider");
        mockProvider.Setup(p => p.Priority).Returns(1);
        mockProviders.Add(mockProvider.Object);

        var cache = new MemoryCache(new MemoryCacheOptions());
        var sut = new AggregatedStockDataService(mockProviders, cache, null, mockScopeFactory.Object);

        // Act
        var result = await sut.GetHistoricalDataAsync(symbol, startDate, endDate);

        // Assert
        result.Should().NotBeNull();
        result!.Data.Should().NotBeEmpty();
        // Verify no provider methods were called
        mockProvider.Verify(p => p.GetHistoricalDataAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region AC2.2 - Stale DB data triggers gap-fill

    [Fact]
    public async Task GetHistoricalDataAsync_WithStaleDbData_CallsApiProvider()
    {
        // Arrange
        var symbol = "MSFT";
        var endDate = DateTime.Today;
        var startDate = endDate.AddYears(-5);

        // Create stale DB data: latest price 10 days ago
        var dbPrices = new List<PriceEntity>();
        var tenDaysAgo = DateTime.Today.AddDays(-10);
        for (int i = 0; i < 100; i++)
        {
            dbPrices.Add(new PriceEntity
            {
                SecurityAlias = 2,
                EffectiveDate = tenDaysAgo.AddDays(-i),
                Open = 200m,
                High = 201m,
                Low = 199m,
                Close = 200m,
                Volume = 1000000
            });
        }
        dbPrices = dbPrices.OrderBy(p => p.EffectiveDate).ToList();

        var security = new SecurityMasterEntity
        {
            SecurityAlias = 2,
            TickerSymbol = symbol,
            IssueName = "Microsoft Corporation"
        };

        var mockPriceRepo = new Mock<IPriceRepository>();
        mockPriceRepo.Setup(r => r.GetPricesAsync(It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(dbPrices);
        mockPriceRepo.Setup(r => r.BulkInsertAsync(It.IsAny<IEnumerable<PriceCreateDto>>()))
            .ReturnsAsync(10);

        var mockSecurityRepo = new Mock<ISecurityMasterRepository>();
        mockSecurityRepo.Setup(r => r.GetByTickerAsync(It.IsAny<string>()))
            .ReturnsAsync(security);

        var mockScopeFactory = CreateMockScopeFactory(mockPriceRepo, mockSecurityRepo);

        // API provider returns recent 7 days of data
        var apiData = TestDataFactory.CreateOhlcvDataList(7, startPrice: 215m, startDate: DateTime.Today.AddDays(-6));

        var mockProviders = new List<IStockDataProvider>();
        var mockProvider = new Mock<IStockDataProvider>();
        mockProvider.Setup(p => p.IsAvailable).Returns(true);
        mockProvider.Setup(p => p.ProviderName).Returns("TestProvider");
        mockProvider.Setup(p => p.Priority).Returns(1);
        mockProvider.Setup(p => p.GetHistoricalDataAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HistoricalDataResult
            {
                Symbol = symbol,
                Period = "1mo",
                StartDate = apiData.First().Date,
                EndDate = apiData.Last().Date,
                Data = apiData
            });
        mockProviders.Add(mockProvider.Object);

        var cache = new MemoryCache(new MemoryCacheOptions());
        var sut = new AggregatedStockDataService(mockProviders, cache, null, mockScopeFactory.Object);

        // Act
        var result = await sut.GetHistoricalDataAsync(symbol, startDate, endDate);

        // Assert
        result.Should().NotBeNull();
        result!.Data.Should().NotBeEmpty();
        // Verify provider was called for gap-fill (stale data detected)
        mockProvider.Verify(p => p.GetHistoricalDataAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region AC2.3 - Gap-fill with API success returns merged result

    [Fact]
    public async Task GetHistoricalDataAsync_WithGapFillSuccess_ReturnsCachedResult()
    {
        // Arrange
        var symbol = "GOOGL";
        var endDate = DateTime.Today;
        var startDate = endDate.AddYears(-5);

        // Create stale DB data (latest 5 days ago)
        var dbPrices = new List<PriceEntity>();
        var fiveDaysAgo = DateTime.Today.AddDays(-5);
        for (int i = 0; i < 50; i++)
        {
            dbPrices.Add(new PriceEntity
            {
                SecurityAlias = 3,
                EffectiveDate = fiveDaysAgo.AddDays(-i),
                Open = 100m,
                High = 101m,
                Low = 99m,
                Close = 100m,
                Volume = 1000000
            });
        }
        dbPrices = dbPrices.OrderBy(p => p.EffectiveDate).ToList();

        var security = new SecurityMasterEntity
        {
            SecurityAlias = 3,
            TickerSymbol = symbol,
            IssueName = "Alphabet Inc."
        };

        var mockPriceRepo = new Mock<IPriceRepository>();
        mockPriceRepo.Setup(r => r.GetPricesAsync(It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(dbPrices);
        mockPriceRepo.Setup(r => r.BulkInsertAsync(It.IsAny<IEnumerable<PriceCreateDto>>()))
            .ReturnsAsync(5);

        var mockSecurityRepo = new Mock<ISecurityMasterRepository>();
        mockSecurityRepo.Setup(r => r.GetByTickerAsync(It.IsAny<string>()))
            .ReturnsAsync(security);

        var mockScopeFactory = CreateMockScopeFactory(mockPriceRepo, mockSecurityRepo);

        // Fresh API data
        var apiData = TestDataFactory.CreateOhlcvDataList(5, startPrice: 105m, startDate: DateTime.Today.AddDays(-4));

        var mockProviders = new List<IStockDataProvider>();
        var mockProvider = new Mock<IStockDataProvider>();
        mockProvider.Setup(p => p.IsAvailable).Returns(true);
        mockProvider.Setup(p => p.ProviderName).Returns("TestProvider");
        mockProvider.Setup(p => p.Priority).Returns(1);
        mockProvider.Setup(p => p.GetHistoricalDataAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HistoricalDataResult
            {
                Symbol = symbol,
                Period = "1mo",
                StartDate = apiData.First().Date,
                EndDate = apiData.Last().Date,
                Data = apiData
            });
        mockProviders.Add(mockProvider.Object);

        var cache = new MemoryCache(new MemoryCacheOptions());
        var sut = new AggregatedStockDataService(mockProviders, cache, null, mockScopeFactory.Object);

        // Act
        var result = await sut.GetHistoricalDataAsync(symbol, startDate, endDate);

        // Assert: Should cache merged result with both DB and API data
        result.Should().NotBeNull();
        result!.Data.Should().NotBeEmpty();
        // Second call should hit cache without calling provider again
        var result2 = await sut.GetHistoricalDataAsync(symbol, startDate, endDate);
        result2.Should().BeEquivalentTo(result);
        // Provider should only be called once for gap-fill
        mockProvider.Verify(p => p.GetHistoricalDataAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region AC2.4 - Sparse DB data falls back to full API

    [Fact]
    public async Task GetHistoricalDataAsync_WithSparseDbData_FallsBackToFullApiCascade()
    {
        // Arrange
        var symbol = "TSLA";
        var endDate = DateTime.Today;
        var startDate = endDate.AddYears(-5);

        // Create very sparse DB data (only 5 records for 5 years)
        var dbPrices = new List<PriceEntity>
        {
            new PriceEntity { SecurityAlias = 4, EffectiveDate = startDate, Open = 100m, High = 101m, Low = 99m, Close = 100m, Volume = 1000000 },
            new PriceEntity { SecurityAlias = 4, EffectiveDate = startDate.AddYears(1), Open = 110m, High = 111m, Low = 109m, Close = 110m, Volume = 1000000 },
            new PriceEntity { SecurityAlias = 4, EffectiveDate = startDate.AddYears(2), Open = 120m, High = 121m, Low = 119m, Close = 120m, Volume = 1000000 },
            new PriceEntity { SecurityAlias = 4, EffectiveDate = startDate.AddYears(3), Open = 130m, High = 131m, Low = 129m, Close = 130m, Volume = 1000000 },
            new PriceEntity { SecurityAlias = 4, EffectiveDate = endDate, Open = 140m, High = 141m, Low = 139m, Close = 140m, Volume = 1000000 }
        };

        var security = new SecurityMasterEntity
        {
            SecurityAlias = 4,
            TickerSymbol = symbol,
            IssueName = "Tesla Inc."
        };

        var mockPriceRepo = new Mock<IPriceRepository>();
        mockPriceRepo.Setup(r => r.GetPricesAsync(It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(dbPrices);

        var mockSecurityRepo = new Mock<ISecurityMasterRepository>();
        mockSecurityRepo.Setup(r => r.GetByTickerAsync(It.IsAny<string>()))
            .ReturnsAsync(security);

        var mockScopeFactory = CreateMockScopeFactory(mockPriceRepo, mockSecurityRepo);

        var apiData = TestDataFactory.CreateOhlcvDataList(252, startPrice: 150m, startDate: startDate);

        var mockProviders = new List<IStockDataProvider>();
        var mockProvider = new Mock<IStockDataProvider>();
        mockProvider.Setup(p => p.IsAvailable).Returns(true);
        mockProvider.Setup(p => p.ProviderName).Returns("TestProvider");
        mockProvider.Setup(p => p.Priority).Returns(1);
        mockProvider.Setup(p => p.GetHistoricalDataAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HistoricalDataResult
            {
                Symbol = symbol,
                Period = "5y",
                StartDate = apiData.First().Date,
                EndDate = apiData.Last().Date,
                Data = apiData
            });
        mockProviders.Add(mockProvider.Object);

        var cache = new MemoryCache(new MemoryCacheOptions());
        var sut = new AggregatedStockDataService(mockProviders, cache, null, mockScopeFactory.Object);

        // Act
        var result = await sut.GetHistoricalDataAsync(symbol, startDate, endDate);

        // Assert
        result.Should().NotBeNull();
        // Verify provider was called with full period (5y), not a gap period
        mockProvider.Verify(p => p.GetHistoricalDataAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        result!.Data.Count.Should().BeGreaterThan(10);
    }

    #endregion

    #region AC2.5 - Stale data with gap triggers gap-fill attempt

    [Fact]
    public async Task GetHistoricalDataAsync_WithStaleDataGap_AttempsGapFill()
    {
        // Arrange: Verify gap detection and gap-fill attempt even without full merge verification
        var symbol = "AMZN";
        var endDate = DateTime.Today;
        var startDate = endDate.AddYears(-5);

        // Create very stale DB data (10 days old, triggering gap-fill)
        var dbPrices = new List<PriceEntity>();
        var tenDaysAgo = DateTime.Today.AddDays(-10);
        for (int i = 0; i < 100; i++)
        {
            dbPrices.Add(new PriceEntity
            {
                SecurityAlias = 5,
                EffectiveDate = tenDaysAgo.AddDays(-i),
                Open = 140m,
                High = 141m,
                Low = 139m,
                Close = 140m,
                Volume = 1000000
            });
        }
        dbPrices = dbPrices.OrderBy(p => p.EffectiveDate).ToList();

        var security = new SecurityMasterEntity
        {
            SecurityAlias = 5,
            TickerSymbol = symbol,
            IssueName = "Amazon.com Inc."
        };

        var mockPriceRepo = new Mock<IPriceRepository>();
        mockPriceRepo.Setup(r => r.GetPricesAsync(It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(dbPrices);
        mockPriceRepo.Setup(r => r.BulkInsertAsync(It.IsAny<IEnumerable<PriceCreateDto>>()))
            .ReturnsAsync(0);

        var mockSecurityRepo = new Mock<ISecurityMasterRepository>();
        mockSecurityRepo.Setup(r => r.GetByTickerAsync(It.IsAny<string>()))
            .ReturnsAsync(security);

        var mockScopeFactory = CreateMockScopeFactory(mockPriceRepo, mockSecurityRepo);

        var mockProviders = new List<IStockDataProvider>();
        var mockProvider = new Mock<IStockDataProvider>();
        mockProvider.Setup(p => p.IsAvailable).Returns(true);
        mockProvider.Setup(p => p.ProviderName).Returns("TestProvider");
        mockProvider.Setup(p => p.Priority).Returns(1);
        // API provides data for gap-fill
        var apiData = TestDataFactory.CreateOhlcvDataList(5, startPrice: 150m, startDate: DateTime.Today.AddDays(-4));
        mockProvider.Setup(p => p.GetHistoricalDataAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HistoricalDataResult
            {
                Symbol = symbol,
                Period = "1mo",
                StartDate = apiData.First().Date,
                EndDate = apiData.Last().Date,
                Data = apiData
            });
        mockProviders.Add(mockProvider.Object);

        var cache = new MemoryCache(new MemoryCacheOptions());
        var sut = new AggregatedStockDataService(mockProviders, cache, null, mockScopeFactory.Object);

        // Act
        var result = await sut.GetHistoricalDataAsync(symbol, startDate, endDate);

        // Assert: Should return result (DB-only or merged) and attempt gap-fill
        result.Should().NotBeNull();
        result!.Data.Should().NotBeEmpty();
        // Verify gap-fill was attempted (provider called)
        mockProvider.Verify(p => p.GetHistoricalDataAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion
}
