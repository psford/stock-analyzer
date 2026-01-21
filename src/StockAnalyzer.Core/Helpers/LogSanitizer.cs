namespace StockAnalyzer.Core.Helpers;

/// <summary>
/// Provides sanitization for values that will be written to logs.
/// Prevents log forging attacks by removing control characters and newlines.
/// </summary>
public static class LogSanitizer
{
    /// <summary>
    /// Sanitizes a string value for safe logging by removing control characters.
    /// This prevents log injection/forging attacks where malicious input could
    /// create fake log entries by injecting newlines or other control characters.
    /// </summary>
    /// <param name="value">The value to sanitize</param>
    /// <returns>A sanitized string safe for logging</returns>
    public static string Sanitize(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        // Replace control characters (including newlines, carriage returns, tabs)
        // with a safe representation
        var sanitized = new char[value.Length];
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            // Allow printable ASCII and common extended characters
            // Block control characters (0x00-0x1F and 0x7F-0x9F)
            if (char.IsControl(c))
            {
                sanitized[i] = '_';
            }
            else
            {
                sanitized[i] = c;
            }
        }

        return new string(sanitized);
    }
}
