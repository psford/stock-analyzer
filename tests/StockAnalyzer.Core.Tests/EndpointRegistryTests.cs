using FluentAssertions;
using StockAnalyzer.Api;
using Xunit;

namespace StockAnalyzer.Core.Tests;

[Collection("EndpointRegistry")]
public class EndpointRegistryTests : IDisposable
{
    private readonly string _fixturesPath = Path.Combine(
        Directory.GetCurrentDirectory(),
        "Fixtures",
        "test-endpoints.json");

    // Store original environment values to restore after tests
    private readonly string? _originalDotnetEnv;
    private readonly string? _originalAspnetcoreEnv;
    private readonly Dictionary<string, string?> _originalTestEnvVars = new();

    public EndpointRegistryTests()
    {
        // Save original environment values
        _originalDotnetEnv = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
        _originalAspnetcoreEnv = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");

        // Save original test-specific environment variables
        var testEnvVarNames = new[] { "TEST_DB_CONNECTION", "TEST_TWELVEDATA_KEY" };
        foreach (var varName in testEnvVarNames)
        {
            _originalTestEnvVars[varName] = Environment.GetEnvironmentVariable(varName);
        }
    }

    public void Dispose()
    {
        // Reset EndpointRegistry state
        EndpointRegistry.OverrideFilePath = null;
        EndpointRegistry.Reset();

        // Restore original environment variables
        Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", _originalDotnetEnv);
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", _originalAspnetcoreEnv);

        // Restore original test environment variables
        foreach (var kvp in _originalTestEnvVars)
        {
            Environment.SetEnvironmentVariable(kvp.Key, kvp.Value);
        }
    }

    [Fact]
    public void Resolve_LiteralSource_ReturnsInlineValue()
    {
        // Arrange
        EndpointRegistry.OverrideFilePath = _fixturesPath;
        Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", "dev");
        EndpointRegistry.Reset();

        // Act
        var result = EndpointRegistry.Resolve("database");

        // Assert
        result.Should().Be("Server=localhost;Database=test_db;Integrated Security=true;");
    }

    [Fact]
    public void Resolve_EnvironmentVariable_ReadsFromEnv()
    {
        // Arrange
        EndpointRegistry.OverrideFilePath = _fixturesPath;
        Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", "test");
        Environment.SetEnvironmentVariable("TEST_DB_CONNECTION", "test-connection-string");
        EndpointRegistry.Reset();

        // Act
        var result = EndpointRegistry.Resolve("database");

        // Assert
        result.Should().Be("test-connection-string");
    }

    [Fact]
    public void Resolve_UnsetEnvironmentVariable_ThrowsDescriptiveError()
    {
        // Arrange
        EndpointRegistry.OverrideFilePath = _fixturesPath;
        Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", "test");
        Environment.SetEnvironmentVariable("TEST_DB_CONNECTION", null);
        EndpointRegistry.Reset();

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(
            () => EndpointRegistry.Resolve("database"));

        ex.Message.Should().Contain("TEST_DB_CONNECTION");
        ex.Message.Should().Contain("not set");
    }

    [Fact]
    public void Resolve_CompoundEndpoint_ReturnsSubEntryValue()
    {
        // Arrange
        EndpointRegistry.OverrideFilePath = _fixturesPath;
        Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", "dev");
        EndpointRegistry.Reset();

        // Act
        var apiKey = EndpointRegistry.Resolve("twelveData.apiKey");
        var baseUrl = EndpointRegistry.Resolve("twelveData.baseUrl");

        // Assert
        apiKey.Should().Be("test-twelve-data-key");
        baseUrl.Should().Be("https://api.twelvedata.com");
    }

    [Fact]
    public void Resolve_CompoundEndpointWithoutSubKey_ThrowsDescriptiveError()
    {
        // Arrange
        EndpointRegistry.OverrideFilePath = _fixturesPath;
        Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", "dev");
        EndpointRegistry.Reset();

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(
            () => EndpointRegistry.Resolve("twelveData"));

        ex.Message.Should().Contain("compound endpoint");
        ex.Message.Should().Contain("twelveData.baseUrl");
        ex.Message.Should().Contain("twelveData.apiKey");
    }

    [Fact]
    public void Resolve_UnknownEndpoint_ThrowsDescriptiveError()
    {
        // Arrange
        EndpointRegistry.OverrideFilePath = _fixturesPath;
        Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", "dev");
        EndpointRegistry.Reset();

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(
            () => EndpointRegistry.Resolve("nonexistent"));

        ex.Message.Should().Contain("Unknown endpoint 'nonexistent'");
        ex.Message.Should().Contain("Available:");
        ex.Message.Should().Contain("database");
        ex.Message.Should().Contain("twelveData");
    }

    [Fact]
    public void Resolve_UnknownCompoundSubKey_ThrowsDescriptiveError()
    {
        // Arrange
        EndpointRegistry.OverrideFilePath = _fixturesPath;
        Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", "dev");
        EndpointRegistry.Reset();

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(
            () => EndpointRegistry.Resolve("twelveData.invalidSubKey"));

        ex.Message.Should().Contain("Unknown endpoint 'twelveData.invalidSubKey'");
        ex.Message.Should().Contain("twelveData.baseUrl");
        ex.Message.Should().Contain("twelveData.apiKey");
    }

    [Fact]
    public void Resolve_DevelopmentEnvironmentNormalizedToDev()
    {
        // Arrange
        EndpointRegistry.OverrideFilePath = _fixturesPath;
        Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", "Development");
        EndpointRegistry.Reset();

        // Act
        var result = EndpointRegistry.Resolve("database");

        // Assert
        result.Should().Be("Server=localhost;Database=test_db;Integrated Security=true;");
    }

    [Fact]
    public void Resolve_ProductionEnvironmentNormalizedToProd()
    {
        // Arrange
        EndpointRegistry.OverrideFilePath = _fixturesPath;
        Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", "Production");
        EndpointRegistry.Reset();

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(
            () => EndpointRegistry.Resolve("database"));

        // Should fail because prod environment doesn't exist in test fixture
        ex.Message.Should().Contain("Unknown environment 'prod'");
    }

    [Fact]
    public void Resolve_SimpleEndpoint_ReturnsValue()
    {
        // Arrange
        EndpointRegistry.OverrideFilePath = _fixturesPath;
        Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", "test");
        EndpointRegistry.Reset();

        // Act
        var result = EndpointRegistry.Resolve("simpleEndpoint");

        // Assert
        result.Should().Be("https://example.com");
    }

    [Fact]
    public void Resolve_AllCompoundSubKeys_ResolveCorrectly()
    {
        // Arrange
        EndpointRegistry.OverrideFilePath = _fixturesPath;
        Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", "dev");
        EndpointRegistry.Reset();

        // Act
        var fmpBaseUrl = EndpointRegistry.Resolve("fmp.baseUrl");
        var fmpApiKey = EndpointRegistry.Resolve("fmp.apiKey");
        var finnhubBaseUrl = EndpointRegistry.Resolve("finnhub.baseUrl");

        // Assert
        fmpBaseUrl.Should().Be("https://financialmodelingprep.com/stable");
        fmpApiKey.Should().Be("test-fmp-key");
        finnhubBaseUrl.Should().Be("https://finnhub.io/api/v1");
    }

    [Fact]
    public void Resolve_NullOrEmptyName_ThrowsArgumentException()
    {
        // Arrange
        EndpointRegistry.OverrideFilePath = _fixturesPath;
        Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", "dev");
        EndpointRegistry.Reset();

        // Act & Assert
        Assert.Throws<ArgumentException>(() => EndpointRegistry.Resolve(null!));
        Assert.Throws<ArgumentException>(() => EndpointRegistry.Resolve(""));
        Assert.Throws<ArgumentException>(() => EndpointRegistry.Resolve("   "));
    }

    [Fact]
    public void ValidateAll_WithAllValidEndpoints_Succeeds()
    {
        // Arrange
        EndpointRegistry.OverrideFilePath = _fixturesPath;
        Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", "dev");
        EndpointRegistry.Reset();

        // Act & Assert
        EndpointRegistry.ValidateAll(); // Should not throw
    }

    [Fact]
    public void ValidateAll_WithMissingEnvVar_ThrowsAggregateException()
    {
        // Arrange
        EndpointRegistry.OverrideFilePath = _fixturesPath;
        Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", "test");
        Environment.SetEnvironmentVariable("TEST_DB_CONNECTION", null);
        Environment.SetEnvironmentVariable("TEST_TWELVEDATA_KEY", null);
        EndpointRegistry.Reset();

        // Act & Assert
        var ex = Assert.Throws<AggregateException>(
            () => EndpointRegistry.ValidateAll());

        ex.Message.Should().Contain("Endpoint validation failed");
        ex.Message.Should().Contain("error(s)");
    }

    [Fact]
    public void Reset_ClearsDocument_ForcesReload()
    {
        // Arrange
        EndpointRegistry.OverrideFilePath = _fixturesPath;
        Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", "dev");
        EndpointRegistry.Reset();
        var firstResult = EndpointRegistry.Resolve("database");

        // Act
        EndpointRegistry.Reset();
        var secondResult = EndpointRegistry.Resolve("database");

        // Assert
        firstResult.Should().Be(secondResult);
        firstResult.Should().Be("Server=localhost;Database=test_db;Integrated Security=true;");
    }
}
