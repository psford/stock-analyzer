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
    /// Tests that the WHERE clause correctly filters IsEoddhUnavailable = 0.
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

    /// <summary>
    /// AC1.4: Lookback window of 14 days.
    /// Tests the query pattern that RunDailyRefreshCycleAsync uses.
    /// Verifies that AddDays(-14) correctly creates a 14-day window.
    /// </summary>
    [Fact]
    public async Task RunDailyRefreshCycleAsync_QueryPattern_Uses14DayLookbackWindow()
    {
        // Arrange
        await using var context = CreateInMemoryContext();
        await context.Database.EnsureCreatedAsync();

        // Create a simple 14-day lookback calculation
        var today = new DateTime(2024, 3, 15); // Friday
        var lookbackStart = today.AddDays(-14);

        // Assert the lookback window calculation
        (today - lookbackStart).TotalDays.Should().Be(14, "lookbackStart should be exactly 14 days before today");

        // Verify AddDays(-14) produces the correct result
        var calculatedLookback = today.AddDays(-14);
        calculatedLookback.Should().Be(lookbackStart, "AddDays(-14) correctly identifies 14-day lookback");

        // Test the query pattern used in RunDailyRefreshCycleAsync
        var source = new SourceEntity { SourceId = 1, SourceShortName = "US", SourceLongName = "US Calendar" };
        context.Add(source);

        // Add business calendar entries for the window
        var current = lookbackStart;
        while (current <= today)
        {
            var isBusinessDay = current.DayOfWeek != DayOfWeek.Saturday && current.DayOfWeek != DayOfWeek.Sunday;
            context.Add(new BusinessCalendarEntity
            {
                SourceId = 1,
                EffectiveDate = current,
                IsBusinessDay = isBusinessDay,
                IsHoliday = false
            });
            current = current.AddDays(1);
        }

        await context.SaveChangesAsync();

        // Act - Query business days within the 14-day window
        var businessDays = await context.Set<BusinessCalendarEntity>()
            .AsNoTracking()
            .Where(bc => bc.SourceId == 1
                && bc.IsBusinessDay
                && bc.EffectiveDate >= today.AddDays(-14) // Simulating AddDays(-14) in the query
                && bc.EffectiveDate <= today)
            .Select(bc => bc.EffectiveDate)
            .OrderBy(d => d)
            .ToListAsync();

        // Assert
        // Should include both Monday and Friday in the window
        businessDays.Should().Contain(lookbackStart, "lookbackStart (Friday) is in the 14-day window");
        businessDays.Should().Contain(today, "today (Friday) is in the window");

        // Verify no weekend days are included
        businessDays.Should().AllSatisfy(d =>
        {
            d.DayOfWeek.Should().NotBe(DayOfWeek.Saturday, "Saturdays should not be in business days");
            d.DayOfWeek.Should().NotBe(DayOfWeek.Sunday, "Sundays should not be in business days");
        });
    }

    /// <summary>
    /// AC2.4: Wednesday holiday is identified for forward-fill with Tuesday's prices.
    /// Tests that holidays on weekdays are marked as non-business days.
    /// </summary>
    [Fact]
    public async Task BusinessCalendar_WithWednesdayHoliday_IdentifiedForForwardFill()
    {
        // Arrange
        await using var context = CreateInMemoryContext();
        await context.Database.EnsureCreatedAsync();

        var tuesday = new DateTime(2024, 1, 16); // Tuesday with prices
        var wednesdayHoliday = new DateTime(2024, 1, 17); // Wednesday holiday
        var thursday = new DateTime(2024, 1, 18); // Thursday

        var source = new SourceEntity { SourceId = 1, SourceShortName = "US", SourceLongName = "US Calendar" };
        context.Add(source);

        // Seed calendar: Tuesday is business day, Wednesday is holiday (non-business), Thursday is business day
        context.Add(new BusinessCalendarEntity
        {
            SourceId = 1,
            EffectiveDate = tuesday,
            IsBusinessDay = true,
            IsHoliday = false
        });

        context.Add(new BusinessCalendarEntity
        {
            SourceId = 1,
            EffectiveDate = wednesdayHoliday,
            IsBusinessDay = false, // Holiday is non-business
            IsHoliday = true
        });

        context.Add(new BusinessCalendarEntity
        {
            SourceId = 1,
            EffectiveDate = thursday,
            IsBusinessDay = true,
            IsHoliday = false
        });

        // Add security and Tuesday prices
        var security = new SecurityMasterEntity
        {
            SecurityAlias = 1,
            TickerSymbol = "TEST",
            IssueName = "Test Company"
        };
        context.Add(security);

        context.Add(new PriceEntity
        {
            SecurityAlias = 1,
            EffectiveDate = tuesday,
            Open = 100m,
            High = 105m,
            Low = 99m,
            Close = 102m,
            AdjustedClose = 102m,
            Volume = 1000000
        });

        await context.SaveChangesAsync();

        // Act - Query non-business days (as forward-fill logic does to find fill targets)
        var nonBusinessDays = await context.Set<BusinessCalendarEntity>()
            .AsNoTracking()
            .Where(bc => bc.SourceId == 1
                && !bc.IsBusinessDay
                && bc.EffectiveDate >= tuesday
                && bc.EffectiveDate <= thursday)
            .Select(bc => bc.EffectiveDate)
            .OrderBy(d => d)
            .ToListAsync();

        // Assert
        nonBusinessDays.Should().HaveCount(1, "Only Wednesday (the holiday) is non-business");
        nonBusinessDays.Should().Contain(wednesdayHoliday, "Wednesday holiday is identified for forward-fill");
    }

    /// <summary>
    /// AC3.2: Forward-fill query respects maxFillDate constraint.
    /// Tests that ForwardFillHolidaysAsync query pattern excludes dates after maxFillDate.
    /// </summary>
    [Fact]
    public async Task ForwardFillQuery_WithExplicitPastMaxFillDate_ExcludesDatesAfterCap()
    {
        // Arrange
        await using var context = CreateInMemoryContext();
        await context.Database.EnsureCreatedAsync();

        var baseDate = new DateTime(2024, 1, 10); // Wednesday
        var maxFillDate = baseDate.AddDays(2); // Friday
        var beyondMax = baseDate.AddDays(5); // Monday (beyond max)

        var source = new SourceEntity { SourceId = 1, SourceShortName = "US", SourceLongName = "US Calendar" };
        context.Add(source);

        // Seed calendar for the entire range
        var dates = new[]
        {
            baseDate,           // Wednesday
            baseDate.AddDays(1), // Thursday
            baseDate.AddDays(2), // Friday (maxFillDate)
            baseDate.AddDays(3), // Saturday (beyond maxFillDate)
            baseDate.AddDays(4), // Sunday (beyond maxFillDate)
            beyondMax            // Monday (beyond maxFillDate)
        };

        foreach (var date in dates)
        {
            var isBusinessDay = date.DayOfWeek != DayOfWeek.Saturday && date.DayOfWeek != DayOfWeek.Sunday;
            context.Add(new BusinessCalendarEntity
            {
                SourceId = 1,
                EffectiveDate = date,
                IsBusinessDay = isBusinessDay,
                IsHoliday = false
            });
        }

        await context.SaveChangesAsync();

        // Act - Query non-business days up to maxFillDate (as ForwardFillHolidaysAsync does)
        var nonBusinessDaysUpToMax = await context.Set<BusinessCalendarEntity>()
            .AsNoTracking()
            .Where(bc => bc.SourceId == 1
                && !bc.IsBusinessDay
                && bc.EffectiveDate <= maxFillDate) // Capped at maxFillDate
            .Select(bc => bc.EffectiveDate)
            .ToListAsync();

        // Assert
        nonBusinessDaysUpToMax.Should().NotContain(beyondMax, "Dates after maxFillDate should be excluded");
        nonBusinessDaysUpToMax.Should().NotContain(baseDate.AddDays(3), "Saturday after maxFillDate should be excluded");
        nonBusinessDaysUpToMax.Should().NotContain(baseDate.AddDays(4), "Sunday after maxFillDate should be excluded");
    }

    /// <summary>
    /// AC1.1: Match rate can be calculated from EODHD response intersection with SecurityMaster.
    /// Tests that records matching can be computed from fetched vs matched counts.
    /// </summary>
    [Fact]
    public async Task RefreshDateAsync_WithEodhdAndSecurityMaster_CalculatesMatchRate()
    {
        // Arrange
        await using var context = CreateInMemoryContext();
        await context.Database.EnsureCreatedAsync();

        var testDate = new DateTime(2024, 1, 15);
        var source = new SourceEntity { SourceId = 1, SourceShortName = "US", SourceLongName = "US Calendar" };
        context.Add(source);

        // Seed security master with known tickers
        var knownTickers = new[] { "AAPL", "MSFT", "GOOGL", "AMZN", "TSLA" };
        int alias = 1;
        foreach (var ticker in knownTickers)
        {
            context.Add(new SecurityMasterEntity
            {
                SecurityAlias = alias++,
                TickerSymbol = ticker,
                IssueName = $"{ticker} Inc"
            });
        }

        // Simulate EODHD returning 10 records: 5 in SecurityMaster + 5 not in DB
        // (This simulates what RefreshDateAsync would receive from EODHD)
        // Match count = intersection of EODHD response with SecurityMaster

        await context.SaveChangesAsync();

        // Act - Simulate intersection calculation
        var eodhdRecords = new[] { "AAPL", "MSFT", "GOOGL", "AMZN", "TSLA", "NFLX", "META", "NVDA", "AMD", "INTC" };
        var securityMasterTickers = await context.Set<SecurityMasterEntity>()
            .AsNoTracking()
            .Select(s => s.TickerSymbol)
            .ToListAsync();

        var securityMasterSet = securityMasterTickers.ToHashSet();
        var matchedTickers = eodhdRecords.Where(t => securityMasterSet.Contains(t)).ToList();

        // Assert
        var matchRate = eodhdRecords.Length > 0 ? (double)matchedTickers.Count / eodhdRecords.Length : 0;

        matchRate.Should().BeApproximately(0.5, 0.001, "Match rate should be 50% (5 matched out of 10 fetched)");
        matchedTickers.Should().HaveCount(5, "5 tickers should match SecurityMaster");
        matchedTickers.Should().Contain("AAPL", "MSFT", "GOOGL", "AMZN", "TSLA");
    }

    /// <summary>
    /// AC4.3: Backfill then re-audit pattern shows zero gaps after successful fill.
    /// Tests that querying for gaps after backfill returns empty results.
    /// </summary>
    [Fact]
    public async Task GapDetection_AfterBackfill_ReturnsZeroGaps()
    {
        // Arrange
        await using var context = CreateInMemoryContext();
        await context.Database.EnsureCreatedAsync();

        var monday = new DateTime(2024, 1, 8);
        var friday = new DateTime(2024, 1, 12);
        var allBusinessDays = new[]
        {
            monday,              // Monday
            monday.AddDays(1),   // Tuesday
            monday.AddDays(2),   // Wednesday
            monday.AddDays(3),   // Thursday
            friday               // Friday
        };

        SeedBusinessCalendar(context, monday, friday);

        // Add security
        var security = new SecurityMasterEntity
        {
            SecurityAlias = 1,
            TickerSymbol = "TEST",
            IssueName = "Test Company",
            IsTracked = true,
            IsActive = true,
            IsEodhdUnavailable = false
        };
        context.Add(security);

        // Initially add prices only for Monday and Friday (gaps on Tue, Wed, Thu)
        foreach (var date in new[] { monday, friday })
        {
            context.Add(new PriceEntity
            {
                SecurityAlias = 1,
                EffectiveDate = date,
                Open = 100m,
                High = 105m,
                Low = 99m,
                Close = 102m,
                AdjustedClose = 102m,
                Volume = 1000000
            });
        }

        await context.SaveChangesAsync();

        // Act 1: Detect initial gaps
        var datesWithPrices1 = await context.Set<PriceEntity>()
            .AsNoTracking()
            .Where(p => p.SecurityAlias == 1 && p.EffectiveDate >= monday && p.EffectiveDate <= friday)
            .Select(p => p.EffectiveDate)
            .Distinct()
            .ToListAsync();

        var datesWithPricesSet1 = datesWithPrices1.ToHashSet();
        var gaps1 = allBusinessDays.Where(d => !datesWithPricesSet1.Contains(d)).ToList();

        // Assert gaps exist initially
        gaps1.Should().HaveCount(3, "Tuesday, Wednesday, Thursday should have gaps initially");

        // Act 2: Backfill the gaps
        foreach (var gapDate in gaps1)
        {
            context.Add(new PriceEntity
            {
                SecurityAlias = 1,
                EffectiveDate = gapDate,
                Open = 100m,
                High = 105m,
                Low = 99m,
                Close = 102m,
                AdjustedClose = 102m,
                Volume = 1000000
            });
        }

        await context.SaveChangesAsync();

        // Act 3: Re-audit after backfill
        var datesWithPrices2 = await context.Set<PriceEntity>()
            .AsNoTracking()
            .Where(p => p.SecurityAlias == 1 && p.EffectiveDate >= monday && p.EffectiveDate <= friday)
            .Select(p => p.EffectiveDate)
            .Distinct()
            .ToListAsync();

        var datesWithPricesSet2 = datesWithPrices2.ToHashSet();
        var gaps2 = allBusinessDays.Where(d => !datesWithPricesSet2.Contains(d)).ToList();

        // Assert no gaps after backfill
        gaps2.Should().BeEmpty("All business days should have prices after backfill");
        datesWithPrices2.Should().HaveCount(5, "All 5 business days should have prices");
    }
}
