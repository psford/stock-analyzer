namespace StockAnalyzer.Core.Services;

/// <summary>
/// Tracks API rate limits per provider with automatic reset.
/// Thread-safe for concurrent access.
/// </summary>
public class RateLimitTracker
{
    private readonly int _maxPerMinute;
    private readonly int _maxPerDay;
    private int _minuteCount;
    private int _dayCount;
    private DateTime _minuteResetTime;
    private DateTime _dayResetTime;
    private readonly object _lock = new();

    public RateLimitTracker(int maxPerMinute, int maxPerDay)
    {
        _maxPerMinute = maxPerMinute;
        _maxPerDay = maxPerDay;
        _minuteResetTime = DateTime.UtcNow.AddMinutes(1);
        _dayResetTime = DateTime.UtcNow.Date.AddDays(1);
    }

    /// <summary>
    /// Check if a call can be made without exceeding limits.
    /// </summary>
    public bool CanMakeCall()
    {
        lock (_lock)
        {
            ResetCountersIfNeeded();
            return _minuteCount < _maxPerMinute && _dayCount < _maxPerDay;
        }
    }

    /// <summary>
    /// Record that a call was made.
    /// </summary>
    public void RecordCall()
    {
        lock (_lock)
        {
            ResetCountersIfNeeded();
            _minuteCount++;
            _dayCount++;
        }
    }

    /// <summary>
    /// Get remaining calls (minute, day).
    /// </summary>
    public (int MinuteRemaining, int DayRemaining) GetRemaining()
    {
        lock (_lock)
        {
            ResetCountersIfNeeded();
            return (_maxPerMinute - _minuteCount, _maxPerDay - _dayCount);
        }
    }

    /// <summary>
    /// Get current usage statistics.
    /// </summary>
    public (int MinuteUsed, int DayUsed, int MaxPerMinute, int MaxPerDay) GetStats()
    {
        lock (_lock)
        {
            ResetCountersIfNeeded();
            return (_minuteCount, _dayCount, _maxPerMinute, _maxPerDay);
        }
    }

    private void ResetCountersIfNeeded()
    {
        var now = DateTime.UtcNow;

        if (now >= _minuteResetTime)
        {
            _minuteCount = 0;
            _minuteResetTime = now.AddMinutes(1);
        }

        if (now >= _dayResetTime)
        {
            _dayCount = 0;
            _dayResetTime = now.Date.AddDays(1);
        }
    }
}
