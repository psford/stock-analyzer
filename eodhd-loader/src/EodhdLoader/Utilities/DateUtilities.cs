namespace EodhdLoader.Utilities;

/// <summary>
/// Shared date/time utility functions for consistent business day calculations.
/// </summary>
public static class DateUtilities
{
    /// <summary>
    /// Calculates the last business day of the previous month.
    /// Uses UTC date to avoid timezone inconsistencies.
    /// </summary>
    /// <returns>The last business day (Mon-Fri) of the previous month</returns>
    public static DateTime GetLastMonthEnd()
    {
        var today = DateTime.UtcNow.Date;
        var firstOfMonth = new DateTime(today.Year, today.Month, 1);
        var lastDayOfMonth = firstOfMonth.AddDays(-1); // Last day of previous month

        // Adjust to last business day (if weekend)
        while (lastDayOfMonth.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            lastDayOfMonth = lastDayOfMonth.AddDays(-1);
        }

        return lastDayOfMonth;
    }
}
