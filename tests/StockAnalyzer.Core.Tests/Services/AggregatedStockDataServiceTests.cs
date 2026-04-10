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
    /// Builder fixture for creating test instances with fluent setup.
    /// Reduces boilerplate by allowing tests to override only relevant mocks.
    /// </summary>
    private class AggregatedStockDataServiceTestFixture
    {
        private readonly Mock<IPriceRepository> _mockPriceRepo;
        private readonly Mock<ISecurityMasterRepository> _mockSecurityRepo;
        private readonly List<(IStockDataProvider Instance, Mock<IStockDataProvider> Mock)> _providersWithMocks;
        private IMemoryCache? _cache;

        public AggregatedStockDataServiceTestFixture()
        {
            _mockPriceRepo = new Mock<IPriceRepository>();
            _mockSecurityRepo = new Mock<ISecurityMasterRepository>();
            _providersWithMocks = new List<(IStockDataProvider, Mock<IStockDataProvider>)>();
            _cache = null; // Will create in Build() if not set
        }

        public AggregatedStockDataServiceTestFixture WithDefaultSecuritySetup(string symbol, int securityAlias = 1)
        {
            _mockSecurityRepo.Setup(r => r.GetByTickerAsync(It.IsAny<string>()))
                .ReturnsAsync(new SecurityMasterEntity
                {
                    SecurityAlias = securityAlias,
                    TickerSymbol = symbol,
                    IssueName = $"{symbol} Inc."
                });
            return this;
        }

        public AggregatedStockDataServiceTestFixture WithPriceRepositorySetup(
            List<PriceEntity> prices,
            int? bulkInsertReturnCount = null)
        {
            _mockPriceRepo.Setup(r => r.GetPricesAsync(It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<DateTime>()))
                .ReturnsAsync(prices);

            if (bulkInsertReturnCount.HasValue)
            {
                _mockPriceRepo.Setup(r => r.BulkInsertAsync(It.IsAny<IEnumerable<PriceCreateDto>>()))
                    .ReturnsAsync(bulkInsertReturnCount.Value);
            }

            return this;
        }

        public AggregatedStockDataServiceTestFixture WithPriceRepositoryNoBulkInsert()
        {
            // Don't set up BulkInsertAsync at all, so verify calls can detect it wasn't called
            return this;
        }

        public AggregatedStockDataServiceTestFixture WithProviderReturningData(
            string providerName,
            HistoricalDataResult data,
            int priority = 1)
        {
            var mockProvider = new Mock<IStockDataProvider>();
            mockProvider.Setup(p => p.IsAvailable).Returns(true);
            mockProvider.Setup(p => p.ProviderName).Returns(providerName);
            mockProvider.Setup(p => p.Priority).Returns(priority);
            // Use Callback to match any invocation and return the data
            mockProvider
                .Setup(p => p.GetHistoricalDataAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(data);
            _providersWithMocks.Add((mockProvider.Object, mockProvider));
            return this;
        }

        public AggregatedStockDataServiceTestFixture WithProviderReturningDataForPeriod(
            string providerName,
            string expectedPeriod,
            HistoricalDataResult data,
            int priority = 1)
        {
            var mockProvider = new Mock<IStockDataProvider>();
            mockProvider.Setup(p => p.IsAvailable).Returns(true);
            mockProvider.Setup(p => p.ProviderName).Returns(providerName);
            mockProvider.Setup(p => p.Priority).Returns(priority);
            // Setup to match when period equals expectedPeriod
            mockProvider
                .Setup(p => p.GetHistoricalDataAsync(It.IsAny<string>(), expectedPeriod, It.IsAny<CancellationToken>()))
                .ReturnsAsync(data);
            _providersWithMocks.Add((mockProvider.Object, mockProvider));
            return this;
        }

        public AggregatedStockDataServiceTestFixture WithProviderReturningNull(string providerName, int priority = 1)
        {
            var mockProvider = new Mock<IStockDataProvider>();
            mockProvider.Setup(p => p.IsAvailable).Returns(true);
            mockProvider.Setup(p => p.ProviderName).Returns(providerName);
            mockProvider.Setup(p => p.Priority).Returns(priority);
            // Return null for any invocation
            mockProvider
                .Setup(p => p.GetHistoricalDataAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((HistoricalDataResult?)null);
            _providersWithMocks.Add((mockProvider.Object, mockProvider));
            return this;
        }

        public AggregatedStockDataServiceTestFixture WithCache(IMemoryCache cache)
        {
            _cache = cache;
            return this;
        }

        /// <summary>
        /// Helper to create a mock stock data provider with StockInfo setup.
        /// Reduces boilerplate in compositing tests.
        /// </summary>
        public AggregatedStockDataServiceTestFixture WithProviderReturningStockInfo(
            string providerName,
            StockInfo? stockInfo,
            int priority,
            bool isAvailable = true)
        {
            var mockProvider = new Mock<IStockDataProvider>();
            mockProvider.Setup(p => p.ProviderName).Returns(providerName);
            mockProvider.Setup(p => p.Priority).Returns(priority);
            mockProvider.Setup(p => p.IsAvailable).Returns(isAvailable);
            mockProvider.Setup(p => p.GetStockInfoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(stockInfo);
            _providersWithMocks.Add((mockProvider.Object, mockProvider));
            return this;
        }

        public Mock<IPriceRepository> GetMockPriceRepository() => _mockPriceRepo;
        public Mock<ISecurityMasterRepository> GetMockSecurityRepository() => _mockSecurityRepo;
        public List<IStockDataProvider> GetProviders() => _providersWithMocks.Select(p => p.Instance).ToList();
        public Mock<IStockDataProvider> GetMockProvider(int index) => _providersWithMocks[index].Mock;

        public AggregatedStockDataService Build()
        {
            _cache ??= new MemoryCache(new MemoryCacheOptions());

            var mockServiceProvider = new Mock<IServiceProvider>();
            mockServiceProvider.Setup(sp => sp.GetService(typeof(IPriceRepository)))
                .Returns(_mockPriceRepo.Object);
            mockServiceProvider.Setup(sp => sp.GetService(typeof(ISecurityMasterRepository)))
                .Returns(_mockSecurityRepo.Object);

            var mockScope = new Mock<IServiceScope>();
            mockScope.Setup(s => s.ServiceProvider).Returns(mockServiceProvider.Object);

            var mockScopeFactory = new Mock<IServiceScopeFactory>();
            mockScopeFactory.Setup(f => f.CreateScope()).Returns(mockScope.Object);

            return new AggregatedStockDataService(GetProviders(), _cache, null, mockScopeFactory.Object);
        }
    }

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

    #region AC2.3 - Gap-fill with API success returns merged result and persists data

    [Fact]
    public async Task GetHistoricalDataAsync_WithGapFillSuccess_PersistsMergedDataAndReturnsCachedResult()
    {
        // Arrange
        var symbol = "GOOGL";
        var today = new DateTime(2026, 4, 8);  // Use fixed date for consistency
        var endDate = today;
        var startDate = endDate.AddYears(-5);

        // Create stale DB data (latest 5 days ago, but with enough data to pass sparsity check)
        // For 5 years, need at least 20% of ~1254 trading days = 251 records
        var dbPrices = new List<PriceEntity>();
        var latestDbDate = today.AddDays(-5);  // April 3
        for (int i = 0; i < 300; i++)  // 300 records to safely pass sparsity check
        {
            dbPrices.Add(new PriceEntity
            {
                SecurityAlias = 3,
                EffectiveDate = latestDbDate.AddDays(-i),
                Open = 100m,
                High = 101m,
                Low = 99m,
                Close = 100m,
                Volume = 1000000
            });
        }
        dbPrices = dbPrices.OrderBy(p => p.EffectiveDate).ToList();

        // Fresh API data: gap starts at April 4 (latest + 1 day), so API data starts April 5
        // API will return 5 days of data starting April 5: April 5-9
        var apiStartDate = today.AddDays(-2);  // April 6
        var apiData = new List<OhlcvData>
        {
            new() { Date = apiStartDate, Open = 150m, High = 151m, Low = 149m, Close = 150m, Volume = 1000000 },
            new() { Date = apiStartDate.AddDays(1), Open = 151m, High = 152m, Low = 150m, Close = 151m, Volume = 1000000 },
            new() { Date = apiStartDate.AddDays(2), Open = 152m, High = 153m, Low = 151m, Close = 152m, Volume = 1000000 },
        };

        var fixture = new AggregatedStockDataServiceTestFixture()
            .WithDefaultSecuritySetup(symbol, 3)
            .WithPriceRepositorySetup(dbPrices, bulkInsertReturnCount: apiData.Count)
            .WithProviderReturningData("TestProvider", new HistoricalDataResult
            {
                Symbol = symbol,
                Period = "1mo",
                StartDate = apiData.First().Date,
                EndDate = apiData.Last().Date,
                Data = apiData
            })
            .WithCache(new MemoryCache(new MemoryCacheOptions()));

        var mockPriceRepo = fixture.GetMockPriceRepository();
        var sut = fixture.Build();

        // Act
        var result = await sut.GetHistoricalDataAsync(symbol, startDate, endDate);

        // Assert: Should persist merged data via BulkInsertAsync
        result.Should().NotBeNull();
        result!.Data.Should().NotBeEmpty();
        // Verify BulkInsertAsync was called exactly once to persist gap-fill prices
        mockPriceRepo.Verify(r => r.BulkInsertAsync(It.IsAny<IEnumerable<PriceCreateDto>>()), Times.Once);
        // Merged result should contain more data than DB-only data
        result.Data.Count.Should().BeGreaterThan(dbPrices.Count);

        // Second call should hit cache without calling BulkInsertAsync again
        var result2 = await sut.GetHistoricalDataAsync(symbol, startDate, endDate);
        result2.Should().BeEquivalentTo(result);
        // BulkInsertAsync should still be called only once (not again on cache hit)
        mockPriceRepo.Verify(r => r.BulkInsertAsync(It.IsAny<IEnumerable<PriceCreateDto>>()), Times.Once);
    }

    #endregion

    #region AC2.4 - Sparse DB data falls back to full API

    [Fact]
    public async Task GetHistoricalDataAsync_WithSparseDbData_FallsBackToFullApiCascadeWithFullPeriod()
    {
        // Arrange
        var symbol = "TSLA";
        var endDate = DateTime.Today;
        var startDate = endDate.AddYears(-5);

        // Create very sparse DB data (only 5 records for 5 years, <1% coverage)
        var dbPrices = new List<PriceEntity>
        {
            new PriceEntity { SecurityAlias = 4, EffectiveDate = startDate, Open = 100m, High = 101m, Low = 99m, Close = 100m, Volume = 1000000 },
            new PriceEntity { SecurityAlias = 4, EffectiveDate = startDate.AddYears(1), Open = 110m, High = 111m, Low = 109m, Close = 110m, Volume = 1000000 },
            new PriceEntity { SecurityAlias = 4, EffectiveDate = startDate.AddYears(2), Open = 120m, High = 121m, Low = 119m, Close = 120m, Volume = 1000000 },
            new PriceEntity { SecurityAlias = 4, EffectiveDate = startDate.AddYears(3), Open = 130m, High = 131m, Low = 129m, Close = 130m, Volume = 1000000 },
            new PriceEntity { SecurityAlias = 4, EffectiveDate = endDate, Open = 140m, High = 141m, Low = 139m, Close = 140m, Volume = 1000000 }
        };

        var apiData = TestDataFactory.CreateOhlcvDataList(252, startPrice: 150m, startDate: startDate);

        var fixture = new AggregatedStockDataServiceTestFixture()
            .WithDefaultSecuritySetup(symbol, 4)
            .WithPriceRepositorySetup(dbPrices)
            .WithProviderReturningDataForPeriod("TestProvider", "5y", new HistoricalDataResult
            {
                Symbol = symbol,
                Period = "5y",
                StartDate = apiData.First().Date,
                EndDate = apiData.Last().Date,
                Data = apiData
            })
            .WithCache(new MemoryCache(new MemoryCacheOptions()));

        var mockProvider = fixture.GetMockProvider(0);
        var sut = fixture.Build();

        // Act
        var result = await sut.GetHistoricalDataAsync(symbol, startDate, endDate);

        // Assert: Should fall back to full API cascade with full period (5y), not a gap period
        result.Should().NotBeNull();
        result!.Data.Count.Should().BeGreaterThan(10);
        // Verify provider was called with the full period "5y", confirming full fallback
        mockProvider.Verify(p => p.GetHistoricalDataAsync(symbol, "5y", It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region AC2.5 - When API gap-fill fails, DB-only data is returned (partial is better than nothing)

    [Fact]
    public async Task GetHistoricalDataAsync_WhenApiGapFillFails_ReturnsDbOnlyData()
    {
        // Arrange: API gap-fill provider returns null (failure scenario)
        var symbol = "AMZN";
        var endDate = DateTime.Today;
        var startDate = endDate.AddYears(-1);  // 1 year, not 5 (shorter range to avoid sparse check)

        // Create stale DB data (10 days old, triggering gap-fill attempt)
        // For 1 year (~252 trading days), need at least 20% coverage = 50+ records
        var dbPrices = new List<PriceEntity>();
        var tenDaysAgo = DateTime.Today.AddDays(-10);
        for (int i = 0; i < 200; i++)  // Enough records to pass 20% sparsity threshold
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
        int dbOnlyCount = dbPrices.Count;

        var fixture = new AggregatedStockDataServiceTestFixture()
            .WithDefaultSecuritySetup(symbol, 5)
            .WithPriceRepositorySetup(dbPrices)
            .WithProviderReturningNull("TestProvider")  // API gap-fill fails
            .WithCache(new MemoryCache(new MemoryCacheOptions()));

        var mockPriceRepo = fixture.GetMockPriceRepository();
        var mockProvider = fixture.GetMockProvider(0);
        var sut = fixture.Build();

        // Act
        var result = await sut.GetHistoricalDataAsync(symbol, startDate, endDate);

        // Assert: Should return DB-only data when API gap-fill fails
        result.Should().NotBeNull();
        result!.Data.Should().NotBeEmpty();
        // Result should contain exactly the DB data count (no merge happened)
        result.Data.Count.Should().Be(dbOnlyCount);
        // Verify gap-fill was attempted (provider called)
        mockProvider.Verify(p => p.GetHistoricalDataAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        // Verify BulkInsertAsync was NOT called (no new data to persist)
        mockPriceRepo.Verify(r => r.BulkInsertAsync(It.IsAny<IEnumerable<PriceCreateDto>>()), Times.Never);
    }

    #endregion

    #region Stock Data Compositing Tests (AC1 and AC3)

    #region AC1.1 - TwelveData null, FMP has value

    [Fact]
    public async Task GetStockInfoAsync_WhenTwelveDataNullMarketCapAndFmpHasValue_CompositeContainsFmpMarketCap()
    {
        // Arrange
        var symbol = "AAPL";
        const decimal fmpMarketCap = 2_000_000_000m;

        var fixture = new AggregatedStockDataServiceTestFixture()
            .WithProviderReturningStockInfo(
                "TwelveData",
                new StockInfo
                {
                    Symbol = symbol,
                    ShortName = "Apple",
                    LongName = "Apple Inc.",
                    MarketCap = null,  // TwelveData doesn't have MarketCap
                    CurrentPrice = 150m
                },
                priority: 1)
            .WithProviderReturningStockInfo(
                "FMP",
                new StockInfo
                {
                    Symbol = symbol,
                    ShortName = "Apple Inc",
                    LongName = "Apple Inc.",
                    MarketCap = fmpMarketCap,  // FMP has MarketCap
                    CurrentPrice = 149m
                },
                priority: 2)
            .WithCache(new MemoryCache(new MemoryCacheOptions()));

        var sut = fixture.Build();

        // Act
        var result = await sut.GetStockInfoAsync(symbol);

        // Assert
        result.Should().NotBeNull();
        result!.MarketCap.Should().Be(fmpMarketCap);
        // Identity fields should come from TwelveData (primary provider)
        result.Symbol.Should().Be(symbol);
    }

    #endregion

    #region AC1.2 - TwelveData null PeRatio, FMP has value

    [Fact]
    public async Task GetStockInfoAsync_WhenTwelveDataNullPeRatioAndFmpHasValue_CompositeContainsFmpPeRatio()
    {
        // Arrange
        var symbol = "MSFT";
        const decimal fmpPeRatio = 25.5m;

        var fixture = new AggregatedStockDataServiceTestFixture()
            .WithProviderReturningStockInfo(
                "TwelveData",
                new StockInfo
                {
                    Symbol = symbol,
                    ShortName = "Microsoft",
                    LongName = "Microsoft Corporation",
                    PeRatio = null,  // TwelveData doesn't have PeRatio
                    CurrentPrice = 300m
                },
                priority: 1)
            .WithProviderReturningStockInfo(
                "FMP",
                new StockInfo
                {
                    Symbol = symbol,
                    ShortName = "Microsoft Corporation",
                    LongName = "Microsoft Corporation",
                    PeRatio = fmpPeRatio,  // FMP has PeRatio
                    CurrentPrice = 299m
                },
                priority: 2)
            .WithCache(new MemoryCache(new MemoryCacheOptions()));

        var sut = fixture.Build();

        // Act
        var result = await sut.GetStockInfoAsync(symbol);

        // Assert
        result.Should().NotBeNull();
        result!.PeRatio.Should().Be(fmpPeRatio);
        result.Symbol.Should().Be(symbol);
    }

    #endregion

    #region AC1.3 - Only Yahoo available

    [Fact]
    public async Task GetStockInfoAsync_WhenOnlyYahooAvailable_CompositeContainsYahooValues()
    {
        // Arrange
        var symbol = "GOOGL";
        const decimal yahooMarketCap = 1_500_000_000m;
        const decimal yahooPeRatio = 20.0m;

        var fixture = new AggregatedStockDataServiceTestFixture()
            .WithProviderReturningStockInfo(
                "TwelveData",
                null,
                priority: 1,
                isAvailable: false)
            .WithProviderReturningStockInfo(
                "FMP",
                null,
                priority: 2,
                isAvailable: false)
            .WithProviderReturningStockInfo(
                "Yahoo",
                new StockInfo
                {
                    Symbol = symbol,
                    ShortName = "Alphabet",
                    LongName = "Alphabet Inc.",
                    MarketCap = yahooMarketCap,
                    PeRatio = yahooPeRatio,
                    CurrentPrice = 140m
                },
                priority: 3)
            .WithCache(new MemoryCache(new MemoryCacheOptions()));

        var sut = fixture.Build();

        // Act
        var result = await sut.GetStockInfoAsync(symbol);

        // Assert
        result.Should().NotBeNull();
        result!.MarketCap.Should().Be(yahooMarketCap);
        result.PeRatio.Should().Be(yahooPeRatio);
        result.Symbol.Should().Be(symbol);
    }

    #endregion

    #region AC1.4 - All providers null for MarketCap

    [Fact]
    public async Task GetStockInfoAsync_WhenAllProvidersReturnNullMarketCap_CompositeMarketCapIsNull()
    {
        // Arrange
        var symbol = "TSLA";

        var fixture = new AggregatedStockDataServiceTestFixture()
            .WithProviderReturningStockInfo(
                "TwelveData",
                new StockInfo
                {
                    Symbol = symbol,
                    ShortName = "Tesla",
                    LongName = "Tesla Inc.",
                    MarketCap = null,
                    CurrentPrice = 240m
                },
                priority: 1)
            .WithProviderReturningStockInfo(
                "FMP",
                new StockInfo
                {
                    Symbol = symbol,
                    ShortName = "Tesla Inc",
                    LongName = "Tesla Inc.",
                    MarketCap = null,  // FMP also null
                    CurrentPrice = 239m
                },
                priority: 2)
            .WithCache(new MemoryCache(new MemoryCacheOptions()));

        var sut = fixture.Build();

        // Act
        var result = await sut.GetStockInfoAsync(symbol);

        // Assert
        result.Should().NotBeNull();
        result!.MarketCap.Should().BeNull();
        result.Symbol.Should().Be(symbol);
    }

    #endregion

    #region AC1.5 - Priority wins over value size

    [Fact]
    public async Task GetStockInfoAsync_WhenMultipleProvidersReturnMarketCap_HighestPriorityProviderWins()
    {
        // Arrange
        var symbol = "AMZN";
        const decimal twelveDataMarketCap = 1_000_000_000m;
        const decimal fmpMarketCap = 2_000_000_000m;  // Larger, but FMP has higher priority for MarketCapPe

        var fixture = new AggregatedStockDataServiceTestFixture()
            .WithProviderReturningStockInfo(
                "TwelveData",
                new StockInfo
                {
                    Symbol = symbol,
                    ShortName = "Amazon",
                    LongName = "Amazon.com Inc.",
                    MarketCap = twelveDataMarketCap,
                    CurrentPrice = 175m
                },
                priority: 1)
            .WithProviderReturningStockInfo(
                "FMP",
                new StockInfo
                {
                    Symbol = symbol,
                    ShortName = "Amazon Inc",
                    LongName = "Amazon.com Inc.",
                    MarketCap = fmpMarketCap,
                    CurrentPrice = 174m
                },
                priority: 2)
            .WithCache(new MemoryCache(new MemoryCacheOptions()));

        var sut = fixture.Build();

        // Act
        var result = await sut.GetStockInfoAsync(symbol);

        // Assert
        result.Should().NotBeNull();
        // Per FieldPriorityMatrix, MarketCapPe group has ["FMP", "Yahoo"], so FMP is first
        result!.MarketCap.Should().Be(fmpMarketCap);
        result.Symbol.Should().Be(symbol);
    }

    #endregion

    #region AC3.1 - Per-field compositing across groups

    [Fact]
    public async Task GetStockInfoAsync_WhenProvidersHaveDifferentFields_CompositeIncludesAllFields()
    {
        // Arrange
        var symbol = "META";
        const decimal twelveDataPrice = 300m;
        const decimal fmpFiftyDayAverage = 295m;

        var fixture = new AggregatedStockDataServiceTestFixture()
            .WithProviderReturningStockInfo(
                "TwelveData",
                new StockInfo
                {
                    Symbol = symbol,
                    ShortName = "Meta",
                    LongName = "Meta Platforms Inc.",
                    CurrentPrice = twelveDataPrice,  // TwelveData has price
                    FiftyDayAverage = null           // But no FiftyDayAverage
                },
                priority: 1)
            .WithProviderReturningStockInfo(
                "FMP",
                new StockInfo
                {
                    Symbol = symbol,
                    ShortName = "Meta Inc",
                    LongName = "Meta Platforms Inc.",
                    CurrentPrice = null,                // FMP has no price
                    FiftyDayAverage = fmpFiftyDayAverage  // But has FiftyDayAverage
                },
                priority: 2)
            .WithCache(new MemoryCache(new MemoryCacheOptions()));

        var sut = fixture.Build();

        // Act
        var result = await sut.GetStockInfoAsync(symbol);

        // Assert
        result.Should().NotBeNull();
        result!.CurrentPrice.Should().Be(twelveDataPrice);          // From TwelveData
        result.FiftyDayAverage.Should().Be(fmpFiftyDayAverage);     // From FMP
        result.Symbol.Should().Be(symbol);
    }

    #endregion

    #region AC3.2 - Identity fields from primary provider only

    [Fact]
    public async Task GetStockInfoAsync_WhenIdentityFieldsDifferAcrossProviders_UsesIdentityFromPrimaryProvider()
    {
        // Arrange
        var symbol = "NFLX";
        const string twelveDataShortName = "Netflix Inc";
        const string fmpShortName = "Netflix Inc.";  // Different short name

        var fixture = new AggregatedStockDataServiceTestFixture()
            .WithProviderReturningStockInfo(
                "TwelveData",
                new StockInfo
                {
                    Symbol = symbol,
                    ShortName = twelveDataShortName,
                    LongName = "Netflix Inc",
                    CurrentPrice = 400m
                },
                priority: 1)
            .WithProviderReturningStockInfo(
                "FMP",
                new StockInfo
                {
                    Symbol = symbol,
                    ShortName = fmpShortName,
                    LongName = "Netflix Inc",
                    CurrentPrice = 398m
                },
                priority: 2)
            .WithCache(new MemoryCache(new MemoryCacheOptions()));

        var sut = fixture.Build();

        // Act
        var result = await sut.GetStockInfoAsync(symbol);

        // Assert
        result.Should().NotBeNull();
        // Identity should come from TwelveData (primary provider, first in Price priority)
        result!.ShortName.Should().Be(twelveDataShortName);
        result.Symbol.Should().Be(symbol);
    }

    #endregion

    #region AC3.3 - Provider failure doesn't abort others

    [Fact]
    public async Task GetStockInfoAsync_WhenOneProviderThrows_OtherProvidersFillInWithoutError()
    {
        // Arrange
        var symbol = "IBM";
        const decimal fmpMarketCap = 250_000_000m;

        // We need to manually set up the throwing provider since fixture helper doesn't support throws
        var mockTwelveData = new Mock<IStockDataProvider>();
        mockTwelveData.Setup(p => p.ProviderName).Returns("TwelveData");
        mockTwelveData.Setup(p => p.Priority).Returns(1);
        mockTwelveData.Setup(p => p.IsAvailable).Returns(true);
        mockTwelveData.Setup(p => p.GetStockInfoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Connection timeout"));

        var fixture = new AggregatedStockDataServiceTestFixture()
            .WithProviderReturningStockInfo(
                "FMP",
                new StockInfo
                {
                    Symbol = symbol,
                    ShortName = "IBM",
                    LongName = "International Business Machines",
                    MarketCap = fmpMarketCap,
                    CurrentPrice = 190m
                },
                priority: 2)
            .WithCache(new MemoryCache(new MemoryCacheOptions()));

        // Manually add throwing provider to fixture
        var providers = new List<IStockDataProvider> { mockTwelveData.Object };
        providers.AddRange(fixture.GetProviders());

        var sut = new AggregatedStockDataService(providers, new MemoryCache(new MemoryCacheOptions()), null, null);

        // Act
        var result = await sut.GetStockInfoAsync(symbol);

        // Assert
        result.Should().NotBeNull();
        result!.MarketCap.Should().Be(fmpMarketCap);
        result.Symbol.Should().Be(symbol);
    }

    #endregion

    #region AC3.4 - All providers fail

    [Fact]
    public async Task GetStockInfoAsync_WhenAllProvidersFail_ReturnsNull()
    {
        // Arrange
        var symbol = "GE";

        var mockTwelveData = new Mock<IStockDataProvider>();
        mockTwelveData.Setup(p => p.ProviderName).Returns("TwelveData");
        mockTwelveData.Setup(p => p.Priority).Returns(1);
        mockTwelveData.Setup(p => p.IsAvailable).Returns(true);
        mockTwelveData.Setup(p => p.GetStockInfoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("TwelveData failed"));

        var mockFmp = new Mock<IStockDataProvider>();
        mockFmp.Setup(p => p.ProviderName).Returns("FMP");
        mockFmp.Setup(p => p.Priority).Returns(2);
        mockFmp.Setup(p => p.IsAvailable).Returns(true);
        mockFmp.Setup(p => p.GetStockInfoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("FMP failed"));

        var providers = new[] { mockTwelveData.Object, mockFmp.Object };
        var cache = new MemoryCache(new MemoryCacheOptions());
        var sut = new AggregatedStockDataService(providers, cache, null, null);

        // Act
        var result = await sut.GetStockInfoAsync(symbol);

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region AC3.5 - Single provider is pass-through

    [Fact]
    public async Task GetStockInfoAsync_WhenOnlyOneProviderAvailable_ReturnsThatProviderResultAsIs()
    {
        // Arrange
        var symbol = "INTC";
        var stockInfo = new StockInfo
        {
            Symbol = symbol,
            ShortName = "Intel",
            LongName = "Intel Corporation",
            CurrentPrice = 45m,
            MarketCap = 180_000_000_000m,
            PeRatio = 12.5m
        };

        var fixture = new AggregatedStockDataServiceTestFixture()
            .WithProviderReturningStockInfo(
                "TwelveData",
                stockInfo,
                priority: 1)
            .WithProviderReturningStockInfo(
                "FMP",
                null,
                priority: 2,
                isAvailable: false)
            .WithCache(new MemoryCache(new MemoryCacheOptions()));

        var sut = fixture.Build();

        // Act
        var result = await sut.GetStockInfoAsync(symbol);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEquivalentTo(stockInfo);
    }

    #endregion

    #endregion
}
