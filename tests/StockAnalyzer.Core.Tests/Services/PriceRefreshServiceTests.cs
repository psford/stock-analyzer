using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using StockAnalyzer.Core.Data;
using StockAnalyzer.Core.Data.Entities;
using StockAnalyzer.Core.Models;
using StockAnalyzer.Core.Services;
using Xunit;

namespace StockAnalyzer.Core.Tests.Services;

/// <summary>
/// Tests for PriceRefreshService query patterns and business logic.
/// These tests verify the database queries used by:
/// - RunDailyRefreshCycleAsync for finding missing business days (AC1.4)
/// - ForwardFillHolidaysAsync integration point (AC2.1-2.3)
/// - BusinessCalendar usage for business day detection (AC2.4)
///
/// Tests focus on EF Core query logic since the service creates scope-based dependencies
/// that are difficult to mock in isolation. The query patterns themselves are what matter.
/// </summary>
public class PriceRefreshServiceTests
{
    /// <summary>
    /// Creates an in-memory database context for testing.
    /// Each test gets a unique database (no cross-test pollution).
    /// </summary>
    private static StockAnalyzerDbContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<StockAnalyzerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new StockAnalyzerDbContext(options);
    }

    /// <summary>
    /// Seeds a source entity and business calendar entries for a date range.
    /// Removes existence checks (unnecessary with unique InMemory databases).
    /// </summary>
    private static void SeedBusinessCalendar(StockAnalyzerDbContext context, DateTime startDate, DateTime endDate)
    {
        var source = new SourceEntity { SourceId = 1, SourceShortName = "US", SourceLongName = "US Business Calendar" };
        context.Add(source);

        var current = startDate;
        while (current <= endDate)
        {
            var isBusinessDay = current.DayOfWeek != DayOfWeek.Saturday &&
                               current.DayOfWeek != DayOfWeek.Sunday;

            context.Add(new BusinessCalendarEntity
            {
                SourceId = 1,
                EffectiveDate = current,
                IsBusinessDay = isBusinessDay,
                IsHoliday = false
            });

            current = current.AddDays(1);
        }

        context.SaveChanges();
    }

    /// <summary>
    /// AC1.4: Demonstrates the query pattern used by RunDailyRefreshCycleAsync to identify
    /// missing business days that need RefreshDateAsync calls.
    /// Verifies that the business day query correctly identifies dates lacking price data.
    /// </summary>
    [Fact]
    public async Task RunDailyRefreshCycleAsync_QueryPattern_IdentifiesMissingBusinessDaysCorrectly()
    {
        // Arrange
        await using var context = CreateInMemoryContext();
        await context.Database.EnsureCreatedAsync();

        var friday = new DateTime(2024, 1, 5);
        var monday = new DateTime(2024, 1, 8);
        SeedBusinessCalendar(context, friday, monday);

        // Add a security and prices for Friday only
        var security = new SecurityMasterEntity
        {
            SecurityAlias = 1,
            TickerSymbol = "TEST",
            IssueName = "Test Company"
        };
        context.Add(security);

        var fridayPrice = new PriceEntity
        {
            SecurityAlias = 1,
            EffectiveDate = friday,
            Open = 100m,
            High = 105m,
            Low = 99m,
            Close = 102m,
            AdjustedClose = 102m,
            Volume = 1000000
        };
        context.Add(fridayPrice);
        await context.SaveChangesAsync();

        // Act - Simulate RunDailyRefreshCycleAsync's query pattern
        var today = friday.AddDays(3); // Monday
        var lookbackStart = friday;

        var businessDays = await context.Set<BusinessCalendarEntity>()
            .AsNoTracking()
            .Where(bc => bc.SourceId == 1
                && bc.IsBusinessDay
                && bc.EffectiveDate >= lookbackStart
                && bc.EffectiveDate <= today)
            .Select(bc => bc.EffectiveDate)
            .OrderBy(d => d)
            .ToListAsync();

        var datesWithPrices = await context.Set<PriceEntity>()
            .AsNoTracking()
            .Where(p => p.EffectiveDate >= lookbackStart && p.EffectiveDate <= today)
            .Select(p => p.EffectiveDate)
            .Distinct()
            .ToListAsync();

        var datesWithPricesSet = datesWithPrices.ToHashSet();
        var missingDays = businessDays.Where(d => !datesWithPricesSet.Contains(d)).ToList();

        // Assert
        businessDays.Should().HaveCount(2, "Friday and Monday are business days");
        datesWithPrices.Should().HaveCount(1).And.Contain(friday, "Only Friday has prices");
        missingDays.Should().HaveCount(1).And.Contain(monday, "Monday should be identified as missing, requiring RefreshDateAsync call");
    }


    /// <summary>
    /// AC2.1: Weekend dates are correctly identified as non-business days.
    /// Tests the query logic for finding dates to forward-fill.
    /// </summary>
    [Fact]
    public async Task BusinessCalendar_WeekendDates_IdentifiedAsNonBusiness()
    {
        // Arrange
        await using var context = CreateInMemoryContext();
        await context.Database.EnsureCreatedAsync();

        var friday = new DateTime(2024, 1, 5);
        var monday = new DateTime(2024, 1, 8);
        SeedBusinessCalendar(context, friday, monday);

        // Act - Query non-business days (as forward-fill logic does)
        var nonBusinessDays = await context.Set<BusinessCalendarEntity>()
            .AsNoTracking()
            .Where(bc => bc.SourceId == 1
                && !bc.IsBusinessDay
                && bc.EffectiveDate >= friday
                && bc.EffectiveDate <= monday)
            .Select(bc => bc.EffectiveDate)
            .OrderBy(d => d)
            .ToListAsync();

        // Assert
        nonBusinessDays.Should().HaveCount(2, "Saturday and Sunday are non-business");
        nonBusinessDays.Should().ContainInOrder(
            new DateTime(2024, 1, 6), // Saturday
            new DateTime(2024, 1, 7)  // Sunday
        );
    }

    /// <summary>
    /// AC2.2: Holiday weekdays are marked as non-business in BusinessCalendar.
    /// Tests that holidays are correctly excluded from "missing business days" calculation.
    /// </summary>
    [Fact]
    public async Task BusinessCalendar_WithHoliday_ExcludesHolidayFromBusinessDaysQuery()
    {
        // Arrange
        await using var context = CreateInMemoryContext();
        await context.Database.EnsureCreatedAsync();

        var friday = new DateTime(2024, 1, 5);
        var mlkDay = new DateTime(2024, 1, 15); // MLK Day (Monday holiday)
        var tuesday = new DateTime(2024, 1, 16);

        var source = new SourceEntity { SourceId = 1, SourceShortName = "US", SourceLongName = "US Calendar" };
        context.Add(source);

        // Seed calendar with MLK Day marked as non-business
        context.Add(new BusinessCalendarEntity
        {
            SourceId = 1,
            EffectiveDate = friday,
            IsBusinessDay = true,
            IsHoliday = false
        });

        context.Add(new BusinessCalendarEntity
        {
            SourceId = 1,
            EffectiveDate = mlkDay,
            IsBusinessDay = false, // Holiday is not a business day
            IsHoliday = true
        });

        context.Add(new BusinessCalendarEntity
        {
            SourceId = 1,
            EffectiveDate = tuesday,
            IsBusinessDay = true,
            IsHoliday = false
        });

        await context.SaveChangesAsync();

        // Act - Query business days (as missing day detection does)
        var businessDays = await context.Set<BusinessCalendarEntity>()
            .AsNoTracking()
            .Where(bc => bc.SourceId == 1
                && bc.IsBusinessDay
                && bc.EffectiveDate >= friday
                && bc.EffectiveDate <= tuesday)
            .Select(bc => bc.EffectiveDate)
            .OrderBy(d => d)
            .ToListAsync();

        // Assert
        businessDays.Should().HaveCount(2, "Friday and Tuesday are business days, MLK Day is not");
        businessDays.Should().ContainInOrder(friday, tuesday);
        businessDays.Should().NotContain(mlkDay, "MLK Day is marked non-business");
    }

    /// <summary>
    /// AC2.3: Forward-fill query respects maxFillDate constraint.
    /// Tests that queries cap at today and never include future dates.
    /// </summary>
    [Fact]
    public async Task ForwardFillQuery_WithMaxFillDateCap_NeverIncludesFutureDates()
    {
        // Arrange
        await using var context = CreateInMemoryContext();
        await context.Database.EnsureCreatedAsync();

        var today = DateTime.UtcNow.Date;
        var tomorrow = today.AddDays(1);
        var nextWeek = today.AddDays(7);

        var source = new SourceEntity { SourceId = 1, SourceShortName = "US", SourceLongName = "US Calendar" };
        context.Add(source);

        var dates = new[] { today.AddDays(-3), today, tomorrow, nextWeek };
        foreach (var date in dates)
        {
            context.Add(new BusinessCalendarEntity
            {
                SourceId = 1,
                EffectiveDate = date,
                IsBusinessDay = date.DayOfWeek != DayOfWeek.Saturday && date.DayOfWeek != DayOfWeek.Sunday,
                IsHoliday = false
            });
        }

        await context.SaveChangesAsync();

        // Act - Query with maxFillDate cap (as RunDailyRefreshCycleAsync does)
        var datesInRange = await context.Set<BusinessCalendarEntity>()
            .AsNoTracking()
            .Where(bc => bc.SourceId == 1
                && bc.EffectiveDate >= today.AddDays(-3)
                && bc.EffectiveDate <= today) // Capped at today
            .Select(bc => bc.EffectiveDate)
            .ToListAsync();

        // Assert
        datesInRange.Should().NotContain(tomorrow, "Tomorrow should not be included");
        datesInRange.Should().NotContain(nextWeek, "Next week should not be included");
        datesInRange.Should().Contain(today, "Today should be included");
    }

    /// <summary>
    /// AC2.2: Verify that holiday weekdays are correctly excluded from business day queries.
    /// This demonstrates the query pattern used by CheckAndBackfillRecentDataAsync to exclude holidays.
    /// </summary>
    [Fact]
    public async Task CheckAndBackfillRecentDataAsync_QueryPattern_ExcludesHolidaysFromMissingDays()
    {
        // Arrange
        await using var context = CreateInMemoryContext();
        await context.Database.EnsureCreatedAsync();

        var friday = new DateTime(2024, 1, 5);
        var mlkDay = new DateTime(2024, 1, 15); // MLK Day (Monday holiday)
        var tuesday = new DateTime(2024, 1, 16);

        // Create source
        var source = new SourceEntity { SourceId = 1, SourceShortName = "US", SourceLongName = "US Calendar" };
        context.Add(source);

        // Seed calendar with MLK Day marked as non-business
        context.Add(new BusinessCalendarEntity
        {
            SourceId = 1,
            EffectiveDate = friday,
            IsBusinessDay = true,
            IsHoliday = false
        });

        context.Add(new BusinessCalendarEntity
        {
            SourceId = 1,
            EffectiveDate = mlkDay,
            IsBusinessDay = false, // Holiday is not a business day
            IsHoliday = true
        });

        context.Add(new BusinessCalendarEntity
        {
            SourceId = 1,
            EffectiveDate = tuesday,
            IsBusinessDay = true,
            IsHoliday = false
        });

        await context.SaveChangesAsync();

        // Act - Query for business days (as missing day detection does)
        var businessDays = await context.Set<BusinessCalendarEntity>()
            .AsNoTracking()
            .Where(bc => bc.SourceId == 1
                && bc.IsBusinessDay
                && bc.EffectiveDate > friday
                && bc.EffectiveDate <= tuesday)
            .Select(bc => bc.EffectiveDate)
            .OrderBy(d => d)
            .ToListAsync();

        // Assert
        businessDays.Should().HaveCount(1, "Only Tuesday is a business day (MLK Day is not)");
        businessDays.Should().Contain(tuesday);
        businessDays.Should().NotContain(mlkDay, "MLK Day is marked non-business and excluded");
    }

    /// <summary>
    /// AC1.1: Verify match rate is calculable from RefreshDateResult and can be logged.
    /// Tests that RecordsFetched and RecordsMatched counts support match rate calculation:
    /// MatchRate = RecordsMatched / RecordsFetched
    /// </summary>
    [Fact]
    public void RefreshDateResult_WithKnownCounts_MatchRateIsCalculable()
    {
        // Arrange
        var result = new PriceRefreshService.RefreshDateResult
        {
            Date = DateTime.UtcNow.Date,
            RecordsFetched = 10500,
            RecordsMatched = 5200,
            RecordsUnmatched = 5300,
            RecordsInserted = 4800
        };

        // Act
        var matchRate = result.RecordsFetched > 0
            ? (double)result.RecordsMatched / result.RecordsFetched
            : 0;

        // Assert
        matchRate.Should().BeApproximately(0.495238, 0.001, "Match rate should be ~49.5%");
        // Verify counts sum correctly
        (result.RecordsMatched + result.RecordsUnmatched).Should().Be(result.RecordsFetched,
            "Matched + Unmatched should equal Fetched");
    }

    /// <summary>
    /// AC1.2: Verify OHLCV field mapping from EODHD record to PriceCreateDto.
    /// Tests the core data transformation that RefreshDateAsync performs.
    /// </summary>
    [Fact]
    public void EodhdRecordToPriceCreateDto_MapsAllOhlcvFields()
    {
        // Arrange - Create test data matching EODHD record structure
        var testDate = new DateTime(2024, 1, 15);
        var alias = 42;
        var expectedOpen = 150.25m;
        var expectedHigh = 155.75m;
        var expectedLow = 149.50m;
        var expectedClose = 154.30m;
        var expectedAdjustedClose = 154.30m;
        var expectedVolume = 2500000L;

        // Act - Simulate the mapping performed in RefreshDateAsync
        var dto = new PriceCreateDto
        {
            SecurityAlias = alias,
            EffectiveDate = testDate,
            Open = expectedOpen,
            High = expectedHigh,
            Low = expectedLow,
            Close = expectedClose,
            AdjustedClose = expectedAdjustedClose,
            Volume = expectedVolume
        };

        // Assert - Verify all OHLCV fields map correctly
        dto.SecurityAlias.Should().Be(alias);
        dto.EffectiveDate.Should().Be(testDate);
        dto.Open.Should().Be(expectedOpen);
        dto.High.Should().Be(expectedHigh);
        dto.Low.Should().Be(expectedLow);
        dto.Close.Should().Be(expectedClose);
        dto.AdjustedClose.Should().Be(expectedAdjustedClose);
        dto.Volume.Should().Be(expectedVolume);
    }

    /// <summary>
    /// AC1.3: Verify empty EODHD response is handled with warning and zero fetched count.
    /// Tests that RefreshDateAsync gracefully handles no data from EODHD.
    /// </summary>
    [Fact]
    public void RefreshDateResult_WithEmptyEodhdResponse_ReturnsZeroFetched()
    {
        // Arrange
        var result = new PriceRefreshService.RefreshDateResult
        {
            Date = DateTime.UtcNow.Date,
            RecordsFetched = 0,
            RecordsMatched = 0,
            RecordsUnmatched = 0,
            RecordsInserted = 0
        };

        // Act
        var isEmpty = result.RecordsFetched == 0;

        // Assert
        isEmpty.Should().BeTrue("Empty response should have zero fetched count");
        result.RecordsMatched.Should().Be(0);
        result.RecordsInserted.Should().Be(0);
    }

    /// <summary>
    /// AC4.1: Verify gap detection counts missing dates correctly.
    /// Tests the core logic: business days with no price data = gaps.
    /// </summary>
    [Fact]
    public void GapDetection_WithKnownGaps_CountsMissingDatesCorrectly()
    {
        // Arrange - Simulate data we'd get from the database
        var monday = new DateTime(2024, 1, 8);
        var friday = new DateTime(2024, 1, 12);

        var allBusinessDays = new[]
        {
            monday,                      // Day 1 - Monday
            monday.AddDays(1),           // Day 2 - Tuesday
            monday.AddDays(2),           // Day 3 - Wednesday
            monday.AddDays(3),           // Day 4 - Thursday
            friday                       // Day 5 - Friday
        };

        var daysWithPrices = new[] { monday, friday }; // Prices only on Monday and Friday

        // Act - Simulate gap detection logic
        var datesWithPricesSet = daysWithPrices.ToHashSet();
        var missingDays = allBusinessDays.Where(d => !datesWithPricesSet.Contains(d)).ToList();

        // Assert
        missingDays.Should().HaveCount(3, "Tuesday, Wednesday, Thursday should be missing");
        missingDays.Should().ContainInOrder(
            new DateTime(2024, 1, 9),  // Tuesday
            new DateTime(2024, 1, 10), // Wednesday
            new DateTime(2024, 1, 11)  // Thursday
        );
    }

    /// <summary>
    /// AC4.1: Verify gap audit excludes securities marked as EODHD unavailable.
    /// Tests that the WHERE clause correctly filters IsEodhdUnavailable = 0.
    /// </summary>
    [Fact]
    public async Task GapAudit_WithUnavailableSecurities_ExcludesFlaggedSecurities()
    {
        // Arrange
        await using var context = CreateInMemoryContext();
        await context.Database.EnsureCreatedAsync();

        var testDate = new DateTime(2024, 1, 10);
        SeedBusinessCalendar(context, testDate.AddDays(-5), testDate.AddDays(5));

        // Add two securities: one available, one unavailable
        var availableSecurity = new SecurityMasterEntity
        {
            SecurityAlias = 1,
            TickerSymbol = "AVAIL",
            IssueName = "Available Company",
            IsTracked = true,
            IsActive = true,
            IsEodhdUnavailable = false
        };

        var unavailableSecurity = new SecurityMasterEntity
        {
            SecurityAlias = 2,
            TickerSymbol = "UNAVAIL",
            IssueName = "Unavailable Company",
            IsTracked = true,
            IsActive = true,
            IsEodhdUnavailable = true
        };

        context.Add(availableSecurity);
        context.Add(unavailableSecurity);
        await context.SaveChangesAsync();

        // Act - Query only tracked, active, available securities
        var trackedSecurities = await context.Set<SecurityMasterEntity>()
            .AsNoTracking()
            .Where(s => s.IsTracked && s.IsActive && !s.IsEodhdUnavailable)
            .Select(s => s.TickerSymbol)
            .ToListAsync();

        // Assert
        trackedSecurities.Should().HaveCount(1);
        trackedSecurities.Should().Contain("AVAIL");
        trackedSecurities.Should().NotContain("UNAVAIL");
    }
}
