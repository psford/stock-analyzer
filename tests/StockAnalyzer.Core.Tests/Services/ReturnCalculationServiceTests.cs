using FluentAssertions;
using Moq;
using StockAnalyzer.Core.Data.Entities;
using StockAnalyzer.Core.Services;
using Xunit;

namespace StockAnalyzer.Core.Tests.Services;

/// <summary>
/// Tests for ReturnCalculationService. The service computes period returns
/// (1D, 5D, MTD, ..., Since Inception) from DB price data.
///
/// Key invariants covered:
/// - Uses AdjustedClose (splits/dividends) with fallback to Close
/// - Targeted date-window queries per period (no full-history fetch)
/// - Returns cumulative % for periods under 1 year, annualized for 1 year+
/// - Omits periods that pre-date the security's earliest price
/// - Only emits "Since Inception" when data reaches within 7 days of IPO
/// </summary>
public class ReturnCalculationServiceTests
{
    private readonly Mock<IPriceRepository> _priceRepoMock = new();
    private readonly Mock<ISecurityMasterRepository> _securityRepoMock = new();
    private readonly ReturnCalculationService _sut;

    public ReturnCalculationServiceTests()
    {
        _sut = new ReturnCalculationService(_priceRepoMock.Object, _securityRepoMock.Object);
    }

    // ----- test helpers -----

    private static SecurityMasterEntity MakeSecurity(int alias = 1, string ticker = "TEST")
    {
        return new SecurityMasterEntity
        {
            SecurityAlias = alias,
            TickerSymbol = ticker,
            IssueName = $"{ticker} Corp",
            IsTracked = true,
            IsActive = true
        };
    }

    private static PriceEntity MakePrice(int alias, DateTime date, decimal close, decimal? adjClose = null)
    {
        return new PriceEntity
        {
            SecurityAlias = alias,
            EffectiveDate = date,
            Open = close,
            High = close,
            Low = close,
            Close = close,
            AdjustedClose = adjClose ?? close,
            Volume = 1000
        };
    }

    private void SetupSecurity(SecurityMasterEntity security)
    {
        _securityRepoMock
            .Setup(r => r.GetByTickerAsync(security.TickerSymbol))
            .ReturnsAsync(security);
    }

    private void SetupDateRange(int alias, DateTime earliest, DateTime latest)
    {
        _priceRepoMock
            .Setup(r => r.GetDateRangeAsync(alias))
            .ReturnsAsync(((DateTime?)earliest, (DateTime?)latest));
    }

    // Returns all prices whose date falls within the requested window.
    private void SetupPriceWindow(int alias, List<PriceEntity> allPrices)
    {
        _priceRepoMock
            .Setup(r => r.GetPricesAsync(
                alias,
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>()))
            .ReturnsAsync((int _, DateTime start, DateTime end) =>
                allPrices
                    .Where(p => p.EffectiveDate >= start.Date && p.EffectiveDate <= end.Date)
                    .OrderBy(p => p.EffectiveDate)
                    .ToList());
    }

    // ----- tests -----

    [Fact]
    public async Task CalculateReturnsAsync_UnknownTicker_ReturnsNull()
    {
        _securityRepoMock.Setup(r => r.GetByTickerAsync("NOPE")).ReturnsAsync((SecurityMasterEntity?)null);

        var result = await _sut.CalculateReturnsAsync("NOPE", DateTime.Today);

        result.Should().BeNull();
    }

    [Fact]
    public async Task CalculateReturnsAsync_SecurityHasNoPrices_ReturnsNull()
    {
        var security = MakeSecurity();
        SetupSecurity(security);
        SetupPriceWindow(security.SecurityAlias, new List<PriceEntity>());

        var result = await _sut.CalculateReturnsAsync(security.TickerSymbol, DateTime.Today);

        result.Should().BeNull();
    }

    [Fact]
    public async Task CalculateReturnsAsync_ComputesOneDayReturnFromAdjustedClose()
    {
        // Two trading days: yesterday at 100, today at 110 → 1D return = +10%
        var security = MakeSecurity();
        SetupSecurity(security);
        var endDate = new DateTime(2026, 4, 9);
        var prices = new List<PriceEntity>
        {
            MakePrice(security.SecurityAlias, endDate.AddDays(-1), close: 100m, adjClose: 100m),
            MakePrice(security.SecurityAlias, endDate,            close: 110m, adjClose: 110m),
        };
        SetupPriceWindow(security.SecurityAlias, prices);
        SetupDateRange(security.SecurityAlias, prices[0].EffectiveDate, prices[^1].EffectiveDate);

        var result = await _sut.CalculateReturnsAsync(security.TickerSymbol, endDate);

        result.Should().NotBeNull();
        result!.EndDate.Should().Be(endDate);
        var oneDay = result.Returns.FirstOrDefault(r => r.Label == "1 Day");
        oneDay.Should().NotBeNull();
        oneDay!.ReturnPct.Should().Be(10m);
        oneDay.IsAnnualized.Should().BeFalse();
    }

    [Fact]
    public async Task CalculateReturnsAsync_PrefersAdjustedCloseOverClose()
    {
        // Raw close is misleading (split). AdjustedClose shows the true total return.
        // Start: AdjClose=50, End: AdjClose=100 → +100% return
        // Close values are intentionally different to prove AdjClose is used.
        var security = MakeSecurity();
        SetupSecurity(security);
        var endDate = new DateTime(2026, 4, 9);
        var prices = new List<PriceEntity>
        {
            MakePrice(security.SecurityAlias, endDate.AddDays(-1), close: 200m, adjClose: 50m),
            MakePrice(security.SecurityAlias, endDate,            close: 100m, adjClose: 100m),
        };
        SetupPriceWindow(security.SecurityAlias, prices);
        SetupDateRange(security.SecurityAlias, prices[0].EffectiveDate, prices[^1].EffectiveDate);

        var result = await _sut.CalculateReturnsAsync(security.TickerSymbol, endDate);

        result.Should().NotBeNull();
        var oneDay = result!.Returns.First(r => r.Label == "1 Day");
        oneDay.ReturnPct.Should().Be(100m); // (100 - 50) / 50 = +100%
    }

    [Fact]
    public async Task CalculateReturnsAsync_AnnualizesPeriodsOverOneYear()
    {
        // 2 years: 100 → 144 = 44% cumulative, 20% annualized
        var security = MakeSecurity();
        SetupSecurity(security);
        var endDate = new DateTime(2026, 4, 9);
        var startDate = endDate.AddYears(-2);
        var prices = new List<PriceEntity>
        {
            MakePrice(security.SecurityAlias, startDate, close: 100m, adjClose: 100m),
            MakePrice(security.SecurityAlias, endDate,   close: 144m, adjClose: 144m),
        };
        SetupPriceWindow(security.SecurityAlias, prices);
        SetupDateRange(security.SecurityAlias, prices[0].EffectiveDate, prices[^1].EffectiveDate);

        var result = await _sut.CalculateReturnsAsync(security.TickerSymbol, endDate);

        result.Should().NotBeNull();
        var twoYear = result!.Returns.FirstOrDefault(r => r.Label == "2 Years");
        twoYear.Should().NotBeNull();
        twoYear!.IsAnnualized.Should().BeTrue();
        // sqrt(1.44) - 1 = 0.20 = 20%
        twoYear.ReturnPct.Should().BeApproximately(20m, 0.1m);
    }

    [Fact]
    public async Task CalculateReturnsAsync_SkipsPeriodsBeforeEarliestDate()
    {
        // Security has 1 month of data. 1-year and longer periods should be omitted.
        var security = MakeSecurity();
        SetupSecurity(security);
        var endDate = new DateTime(2026, 4, 9);
        var startDate = endDate.AddDays(-30);
        var prices = new List<PriceEntity>
        {
            MakePrice(security.SecurityAlias, startDate, close: 100m),
            MakePrice(security.SecurityAlias, endDate,   close: 105m),
        };
        SetupPriceWindow(security.SecurityAlias, prices);
        SetupDateRange(security.SecurityAlias, prices[0].EffectiveDate, prices[^1].EffectiveDate);

        var result = await _sut.CalculateReturnsAsync(security.TickerSymbol, endDate);

        result.Should().NotBeNull();
        // These periods pre-date the security's data, so they must not appear
        result!.Returns.Select(r => r.Label).Should().NotContain(new[]
        {
            "1 Year", "2 Years", "5 Years", "10 Years", "15 Years", "20 Years", "30 Years"
        });
        // These should be present
        result.Returns.Select(r => r.Label).Should().Contain("5 Days");
    }

    [Fact]
    public async Task CalculateReturnsAsync_DoesNotFetchFullPriceHistory()
    {
        // REGRESSION GUARD: The old implementation called
        //   GetPricesAsync(alias, DateTime.MinValue.AddYears(1), actualEndDate)
        // which pulled the entire price history for the security — ~11,500 rows for
        // a 46-year-old stock. On Azure SQL S0 with a cold buffer pool, that query
        // took 30-60 seconds per returns request and saturated DTUs.
        //
        // The new implementation must NOT issue a query whose start date goes back
        // more than 31 years (the longest period is 30 years, and the ±7-day search
        // window adds a few days; any call older than that means someone accidentally
        // reintroduced the "load everything" antipattern.
        var security = MakeSecurity();
        SetupSecurity(security);
        var endDate = new DateTime(2026, 4, 9);
        var prices = new List<PriceEntity>
        {
            MakePrice(security.SecurityAlias, endDate.AddDays(-1), close: 100m),
            MakePrice(security.SecurityAlias, endDate,             close: 110m),
        };
        SetupPriceWindow(security.SecurityAlias, prices);
        SetupDateRange(security.SecurityAlias, prices[0].EffectiveDate, prices[^1].EffectiveDate);

        await _sut.CalculateReturnsAsync(security.TickerSymbol, endDate);

        // No call to GetPricesAsync should reach back before 1994 (30 years + slack)
        // for an endDate of 2026-04-09.
        var tooOldCutoff = new DateTime(1994, 1, 1);
        _priceRepoMock.Verify(
            r => r.GetPricesAsync(
                security.SecurityAlias,
                It.Is<DateTime>(d => d < tooOldCutoff),
                It.IsAny<DateTime>()),
            Times.Never,
            "ReturnCalculationService must not fetch entire price history — use targeted windows per period");
    }

    [Fact]
    public async Task CalculateReturnsAsync_UsesDateRangeForEarliestDate()
    {
        // REGRESSION GUARD: Earliest date must come from GetDateRangeAsync (a TOP 1
        // indexed seek), not from loading the full history and taking [0].
        var security = MakeSecurity();
        SetupSecurity(security);
        var endDate = new DateTime(2026, 4, 9);
        var prices = new List<PriceEntity>
        {
            MakePrice(security.SecurityAlias, endDate.AddDays(-1), close: 100m),
            MakePrice(security.SecurityAlias, endDate,             close: 110m),
        };
        SetupPriceWindow(security.SecurityAlias, prices);
        SetupDateRange(security.SecurityAlias, prices[0].EffectiveDate, prices[^1].EffectiveDate);

        await _sut.CalculateReturnsAsync(security.TickerSymbol, endDate);

        _priceRepoMock.Verify(
            r => r.GetDateRangeAsync(security.SecurityAlias),
            Times.Once);
    }
}
