namespace StockAnalyzer.Core.Data.Entities;

/// <summary>
/// EF Core entity for the BusinessCalendar table.
/// Stores business day information for each source (calendar).
/// 1 = yes (is business day), 0 = no (weekend or holiday).
/// Stored in the 'data' schema to separate domain data from operational tables.
/// </summary>
public class BusinessCalendarEntity
{
    /// <summary>
    /// Foreign key to the Source table.
    /// </summary>
    public int SourceId { get; set; }

    /// <summary>
    /// The calendar date.
    /// </summary>
    public DateTime EffectiveDate { get; set; }

    /// <summary>
    /// Whether this date is a business day (1) or not (0).
    /// 0 = weekend or holiday, 1 = trading day.
    /// </summary>
    public bool IsBusinessDay { get; set; }

    /// <summary>
    /// Whether this date is a market holiday (weekday non-business day).
    /// Distinguishes holidays from weekends for reporting purposes.
    /// </summary>
    public bool IsHoliday { get; set; }

    /// <summary>
    /// Whether this date is the last calendar day of the month.
    /// </summary>
    public bool IsMonthEnd { get; set; }

    /// <summary>
    /// Whether this date is the last business day of the month.
    /// </summary>
    public bool IsLastBusinessDayMonthEnd { get; set; }

    /// <summary>
    /// Navigation property to the Source.
    /// </summary>
    public SourceEntity? Source { get; set; }
}
