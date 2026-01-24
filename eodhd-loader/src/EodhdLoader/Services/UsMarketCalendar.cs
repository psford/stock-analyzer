namespace EodhdLoader.Services;

/// <summary>
/// US stock market calendar for NYSE/NASDAQ holidays.
/// Used to identify non-trading days that need forward-filled price data.
/// </summary>
public static class UsMarketCalendar
{
    /// <summary>
    /// Gets all US market holidays for a given year.
    /// </summary>
    public static IEnumerable<MarketHoliday> GetHolidays(int year)
    {
        // Fixed holidays
        yield return new MarketHoliday("New Year's Day", new DateOnly(year, 1, 1));

        if (year >= 2021)
        {
            yield return new MarketHoliday("Juneteenth", new DateOnly(year, 6, 19));
        }

        yield return new MarketHoliday("Independence Day", new DateOnly(year, 7, 4));
        yield return new MarketHoliday("Christmas", new DateOnly(year, 12, 25));

        // Floating holidays
        yield return new MarketHoliday("MLK Day", GetNthWeekday(year, 1, DayOfWeek.Monday, 3));
        yield return new MarketHoliday("Presidents Day", GetNthWeekday(year, 2, DayOfWeek.Monday, 3));
        yield return new MarketHoliday("Memorial Day", GetLastWeekday(year, 5, DayOfWeek.Monday));
        yield return new MarketHoliday("Labor Day", GetNthWeekday(year, 9, DayOfWeek.Monday, 1));
        yield return new MarketHoliday("Thanksgiving", GetNthWeekday(year, 11, DayOfWeek.Thursday, 4));

        // Easter-based
        var easter = CalculateEaster(year);
        yield return new MarketHoliday("Good Friday", easter.AddDays(-2));
    }

    /// <summary>
    /// Gets all market holidays between two dates (inclusive).
    /// </summary>
    public static IEnumerable<MarketHoliday> GetHolidaysBetween(DateOnly startDate, DateOnly endDate)
    {
        for (int year = startDate.Year; year <= endDate.Year; year++)
        {
            foreach (var holiday in GetHolidays(year))
            {
                if (holiday.Date >= startDate && holiday.Date <= endDate)
                {
                    yield return holiday;
                }
            }
        }
    }

    /// <summary>
    /// Checks if a date is a US market holiday.
    /// </summary>
    public static bool IsMarketHoliday(DateOnly date)
    {
        return GetHolidays(date.Year).Any(h => h.Date == date || h.ObservedDate == date);
    }

    /// <summary>
    /// Checks if a date is a trading day (not weekend, not holiday).
    /// </summary>
    public static bool IsTradingDay(DateOnly date)
    {
        // Weekends
        if (date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday)
            return false;

        // Check holidays
        return !IsMarketHoliday(date);
    }

    /// <summary>
    /// Gets the previous trading day before the given date.
    /// </summary>
    public static DateOnly GetPreviousTradingDay(DateOnly date)
    {
        var previous = date.AddDays(-1);
        while (!IsTradingDay(previous))
        {
            previous = previous.AddDays(-1);
        }
        return previous;
    }

    /// <summary>
    /// Gets the next trading day after the given date.
    /// </summary>
    public static DateOnly GetNextTradingDay(DateOnly date)
    {
        var next = date.AddDays(1);
        while (!IsTradingDay(next))
        {
            next = next.AddDays(1);
        }
        return next;
    }

    /// <summary>
    /// Gets the nth occurrence of a weekday in a month.
    /// </summary>
    private static DateOnly GetNthWeekday(int year, int month, DayOfWeek weekday, int n)
    {
        var first = new DateOnly(year, month, 1);
        var daysUntil = ((int)weekday - (int)first.DayOfWeek + 7) % 7;
        var firstOccurrence = first.AddDays(daysUntil);
        return firstOccurrence.AddDays(7 * (n - 1));
    }

    /// <summary>
    /// Gets the last occurrence of a weekday in a month.
    /// </summary>
    private static DateOnly GetLastWeekday(int year, int month, DayOfWeek weekday)
    {
        var last = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
        var daysBack = ((int)last.DayOfWeek - (int)weekday + 7) % 7;
        return last.AddDays(-daysBack);
    }

    /// <summary>
    /// Calculates Easter Sunday using the Anonymous Gregorian algorithm.
    /// </summary>
    private static DateOnly CalculateEaster(int year)
    {
        int a = year % 19;
        int b = year / 100;
        int c = year % 100;
        int d = b / 4;
        int e = b % 4;
        int f = (b + 8) / 25;
        int g = (b - f + 1) / 3;
        int h = (19 * a + b - d - g + 15) % 30;
        int i = c / 4;
        int k = c % 4;
        int l = (32 + 2 * e + 2 * i - h - k) % 7;
        int m = (a + 11 * h + 22 * l) / 451;
        int month = (h + l - 7 * m + 114) / 31;
        int day = ((h + l - 7 * m + 114) % 31) + 1;
        return new DateOnly(year, month, day);
    }
}

/// <summary>
/// Represents a US market holiday.
/// </summary>
public record MarketHoliday
{
    public string Name { get; }
    public DateOnly Date { get; }
    public DateOnly ObservedDate { get; }
    public bool IsWeekday { get; }

    public MarketHoliday(string name, DateOnly date)
    {
        Name = name;
        Date = date;
        IsWeekday = date.DayOfWeek != DayOfWeek.Saturday && date.DayOfWeek != DayOfWeek.Sunday;

        // Calculate observed date (when holiday falls on weekend)
        ObservedDate = date.DayOfWeek switch
        {
            DayOfWeek.Saturday => date.AddDays(-1), // Observed Friday
            DayOfWeek.Sunday => date.AddDays(1),    // Observed Monday
            _ => date
        };
    }

    public override string ToString() => $"{Name} ({Date:yyyy-MM-dd}, {Date.DayOfWeek})";
}
