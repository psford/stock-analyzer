using Microsoft.Extensions.Logging;

namespace StockAnalyzer.Core.Tests.TestHelpers;

/// <summary>
/// Minimal logger implementation for testing (no-op).
/// Used across test files for repository and service testing.
/// </summary>
public class NoopLogger<T> : ILogger<T>
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => false;
    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        // No-op
    }
}
