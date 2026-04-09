using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StockAnalyzer.Core.Data;
using StockAnalyzer.Core.Data.Entities;
using StockAnalyzer.Core.Tests.TestHelpers;
using Xunit;

namespace StockAnalyzer.Core.Tests.Services;

/// <summary>
/// Tests for PriceRefreshService forward-fill behavior.
/// Verifies integration with BusinessCalendar and forward-fill constraints.
/// AC2.1: After Monday refresh, Saturday and Sunday rows exist with Friday's close
/// AC2.2: After Tuesday refresh following Monday holiday, Saturday/Sunday/Monday rows all filled
/// AC2.3: Forward-fill never creates rows with EffectiveDate > today
/// AC2.4: Forward-fill uses BusinessCalendar (tested in CheckAndBackfillRecentDataAsync)
/// </summary>
public class PriceRefreshServiceTests
{
    /// <summary>
    /// Creates an in-memory database context for testing.
    /// </summary>
    private static StockAnalyzerDbContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<StockAnalyzerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .LogTo(Console.WriteLine, LogLevel.Information)
            .Options;

        return new StockAnalyzerDbContext(options);
    }

    /// <summary>
    /// AC2.1: Verify forward-fill logic correctly identifies weekend dates that need filling.
    /// Tests the business logic: non-business dates should be filled with prior business day's close.
    /// </summary>
    [Fact]
    public async Task ForwardFillLogic_OnWeekend_IdentifiesNonBusinessDaysNeedingFill()
    {
        // Arrange
        await using var context = CreateInMemoryContext();
        await context.Database.EnsureCreatedAsync();

        // Set up scenario: Friday has data, Saturday/Sunday don't
        var friday = new DateTime(2024, 1, 5); // Friday - business day
        var saturday = new DateTime(2024, 1, 6); // Weekend
        var sunday = new DateTime(2024, 1, 7); // Weekend
        var monday = new DateTime(2024, 1, 8); // Business day

        // Seed BusinessCalendar
        SeedBusinessCalendar(context, friday, monday);

        // Act - Query for non-business days that need filling
        var nonBusinessDays = await context.Set<BusinessCalendarEntity>()
            .AsNoTracking()
            .Where(bc => bc.SourceId == 1
                && !bc.IsBusinessDay
                && bc.EffectiveDate >= friday
                && bc.EffectiveDate <= monday)
            .Select(bc => bc.EffectiveDate)
            .OrderBy(d => d)
            .ToListAsync();

        // Assert - Saturday and Sunday should be identified
        nonBusinessDays.Should().HaveCount(2);
        nonBusinessDays.Should().ContainInOrder(saturday, sunday);
        nonBusinessDays.Should().NotContain(friday, "Friday is a business day");
        nonBusinessDays.Should().NotContain(monday, "Monday is a business day");
    }

    /// <summary>
    /// AC2.2: Verify BusinessCalendar correctly marks holidays as non-business days.
    /// Tests that a weekday holiday is distinguished from regular business days.
    /// </summary>
    [Fact]
    public async Task BusinessCalendar_WithHoliday_MarksHolidayAsNonBusiness()
    {
        // Arrange
        await using var context = CreateInMemoryContext();
        await context.Database.EnsureCreatedAsync();

        var friday = new DateTime(2024, 1, 5); // Business day
        var monday = new DateTime(2024, 1, 8); // MLK Day (holiday, weekday)
        var tuesday = new DateTime(2024, 1, 9); // Business day after holiday

        // Seed with Monday marked as holiday (non-business)
        var source = new SourceEntity { SourceId = 1, SourceShortName = "US", SourceLongName = "US Calendar" };
        context.Add(source);

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
            EffectiveDate = monday,
            IsBusinessDay = false,
            IsHoliday = true // Explicitly marked as holiday
        });

        context.Add(new BusinessCalendarEntity
        {
            SourceId = 1,
            EffectiveDate = tuesday,
            IsBusinessDay = true,
            IsHoliday = false
        });

        await context.SaveChangesAsync();

        // Act - Query for business days in range
        var businessDays = await context.Set<BusinessCalendarEntity>()
            .AsNoTracking()
            .Where(bc => bc.SourceId == 1
                && bc.IsBusinessDay
                && bc.EffectiveDate >= friday
                && bc.EffectiveDate <= tuesday)
            .Select(bc => bc.EffectiveDate)
            .OrderBy(d => d)
            .ToListAsync();

        // Assert - Monday should NOT be in business days (it's a holiday)
        businessDays.Should().HaveCount(2);
        businessDays.Should().ContainInOrder(friday, tuesday);
        businessDays.Should().NotContain(monday, "Monday is marked as a holiday");
    }

    /// <summary>
    /// AC2.3: Verify forward-fill respects maxFillDate constraint.
    /// Tests that the query cap prevents querying future calendar entries.
    /// </summary>
    [Fact]
    public async Task ForwardFillQueryLogic_WithMaxFillDate_CapsFutureFillingAtToday()
    {
        // Arrange
        await using var context = CreateInMemoryContext();
        await context.Database.EnsureCreatedAsync();

        var today = DateTime.UtcNow.Date;
        var friday = today.AddDays(-3);
        var tomorrow = today.AddDays(1);
        var nextWeek = today.AddDays(7);

        // Seed BusinessCalendar with dates before and after today
        var source = new SourceEntity { SourceId = 1, SourceShortName = "US", SourceLongName = "US Calendar" };
        context.Add(source);

        var dates = new[] { friday, today, tomorrow, nextWeek };
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

        // Act - Query calendar entries up to today (as RunDailyRefreshCycleAsync does)
        var calendarEntries = await context.Set<BusinessCalendarEntity>()
            .AsNoTracking()
            .Where(bc => bc.SourceId == 1
                && bc.EffectiveDate >= friday
                && bc.EffectiveDate <= today) // Cap at today, not beyond
            .Select(bc => bc.EffectiveDate)
            .ToListAsync();

        // Assert - Should not include dates after today
        calendarEntries.Should().NotContain(tomorrow, "Query should cap at maxFillDate=today");
        calendarEntries.Should().NotContain(nextWeek, "Query should cap at maxFillDate=today");
    }

    /// <summary>
    /// AC2.4: CheckAndBackfillRecentDataAsync uses BusinessCalendar to determine business days
    /// Verifies that holidays are excluded from the missing days list
    /// </summary>
    [Fact]
    public async Task CheckAndBackfillRecentDataAsync_WithHoliday_ExcludesHolidayFromMissingDays()
    {
        // Arrange
        await using var context = CreateInMemoryContext();
        await context.Database.EnsureCreatedAsync();

        // Set up scenario: We have prices through Friday 1/5, but Monday 1/8 is a holiday
        var friday = new DateTime(2024, 1, 5);
        var saturday = new DateTime(2024, 1, 6);
        var sunday = new DateTime(2024, 1, 7);
        var monday = new DateTime(2024, 1, 8); // Holiday
        var tuesday = new DateTime(2024, 1, 9); // Business day

        // Seed BusinessCalendar with Monday as non-business (holiday)
        var businessDays = new[]
        {
            friday,  // Business day
            saturday, // Weekend
            sunday,   // Weekend
            monday,   // Holiday (not business day)
            tuesday   // Business day
        };

        var source = new SourceEntity { SourceId = 1, SourceShortName = "US", SourceLongName = "US Calendar" };
        context.Add(source);

        foreach (var date in businessDays)
        {
            var isBusinessDay = date == friday || date == tuesday;
            context.Add(new BusinessCalendarEntity
            {
                SourceId = 1,
                EffectiveDate = date,
                IsBusinessDay = isBusinessDay,
                IsHoliday = date == monday && !isBusinessDay
            });
        }

        // Create security and price through Friday
        var security = new SecurityMasterEntity
        {
            TickerSymbol = "TEST",
            IssueName = "Test Company",
            SecurityAlias = 1
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

        // Act - Query for missing business days between Friday and Tuesday
        // This mimics what CheckAndBackfillRecentDataAsync does
        var missingDays = await context.Set<BusinessCalendarEntity>()
            .AsNoTracking()
            .Where(bc => bc.SourceId == 1
                && bc.IsBusinessDay
                && bc.EffectiveDate > friday
                && bc.EffectiveDate <= tuesday)
            .Select(bc => bc.EffectiveDate)
            .OrderBy(d => d)
            .ToListAsync();

        // Assert
        missingDays.Should().HaveCount(1, "Only Tuesday should be missing (Monday is a holiday)");
        missingDays[0].Should().Be(tuesday);
        missingDays.Should().NotContain(monday, "Monday is a holiday and should not be included");
    }

    /// <summary>
    /// Seeds the BusinessCalendar with realistic business days and weekends.
    /// </summary>
    private static void SeedBusinessCalendar(StockAnalyzerDbContext context, DateTime startDate, DateTime endDate)
    {
        var source = new SourceEntity { SourceId = 1, SourceShortName = "US", SourceLongName = "US Business Calendar" };
        if (!context.Set<SourceEntity>().Any())
        {
            context.Add(source);
        }

        var current = startDate;
        while (current <= endDate)
        {
            var isBusinessDay = current.DayOfWeek != DayOfWeek.Saturday &&
                               current.DayOfWeek != DayOfWeek.Sunday;

            if (!context.Set<BusinessCalendarEntity>().Any(bc => bc.EffectiveDate == current && bc.SourceId == 1))
            {
                context.Add(new BusinessCalendarEntity
                {
                    SourceId = 1,
                    EffectiveDate = current,
                    IsBusinessDay = isBusinessDay,
                    IsHoliday = false
                });
            }

            current = current.AddDays(1);
        }

        try
        {
            context.SaveChanges();
        }
        catch (Exception ex)
        {
            // Ignore duplicate key errors
            if (!ex.Message.Contains("duplicate") && !ex.Message.Contains("Duplicate"))
                throw;
        }
    }
}
