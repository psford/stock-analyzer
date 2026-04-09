using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;

namespace StockAnalyzer.Core.Tests.TestHelpers;

/// <summary>
/// Mock implementation of IConfiguration for testing.
/// </summary>
public class MockConfiguration : IConfiguration
{
    public string? this[string key]
    {
        get => null;
        set { }
    }

    public IEnumerable<IConfigurationSection> GetChildren() => Enumerable.Empty<IConfigurationSection>();
    public IChangeToken GetReloadToken() => new NullChangeToken();
    public IConfigurationSection GetSection(string key) => new NullConfigurationSection();
}

/// <summary>
/// Null implementation of IConfigurationSection for testing.
/// </summary>
public class NullConfigurationSection : IConfigurationSection
{
    public string Key => string.Empty;
    public string Path => string.Empty;
    public string? Value { get; set; }
    public string? this[string key] { get => null; set { } }

    public IEnumerable<IConfigurationSection> GetChildren() => Enumerable.Empty<IConfigurationSection>();
    public IChangeToken GetReloadToken() => new NullChangeToken();
    public IConfigurationSection GetSection(string key) => new NullConfigurationSection();
}

/// <summary>
/// Null implementation of IChangeToken for testing.
/// </summary>
public class NullChangeToken : IChangeToken
{
    public bool HasChanged => false;
    public bool ActiveChangeCallbacks => false;

    public IDisposable RegisterChangeCallback(Action<object?> callback, object? state)
        => new NullDisposable();
}

/// <summary>
/// Null implementation of IDisposable for testing.
/// </summary>
public class NullDisposable : IDisposable
{
    public void Dispose() { }
}
