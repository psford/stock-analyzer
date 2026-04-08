using FluentAssertions;
using StockAnalyzer.Api;
using Xunit;

namespace StockAnalyzer.Core.Tests;

/// <summary>
/// Tests that validate the real endpoints.json file against the actual development environment contract.
/// Unlike EndpointRegistryTests which uses a test fixture, this test class points at the real endpoints.json
/// and verifies that ValidateAll() passes when all required environment variables are set with stub values.
/// This ensures the real endpoints.json is valid and complete.
/// </summary>
public class EndpointRegistryRealContractTests : IDisposable
{
    private readonly string? _originalDotnetEnv;
    private readonly string? _originalAspnetcoreEnv;
    private readonly Dictionary<string, string?> _originalEnvVars = new();

    public EndpointRegistryRealContractTests()
    {
        // Save original environment values for restoration
        _originalDotnetEnv = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
        _originalAspnetcoreEnv = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");

        // Save original values for all dev "source": "env" keys
        var devEnvVarNames = new[]
        {
            "WSL_SQL_CONNECTION",
            "TWELVEDATA_API_KEY",
            "FMP_API_KEY",
            "FINNHUB_API_KEY",
            "EODHD_API_KEY",
            "MARKETAUX_API_TOKEN"
        };
        foreach (var varName in devEnvVarNames)
        {
            _originalEnvVars[varName] = Environment.GetEnvironmentVariable(varName);
        }

        // Point to the real endpoints.json (OverrideFilePath = null uses the default location)
        EndpointRegistry.OverrideFilePath = null;
        Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", "Development");

        // Set all development environment "source": "env" keys with stub values
        Environment.SetEnvironmentVariable("WSL_SQL_CONNECTION", "Server=localhost;Database=stub;User Id=stub;Password=stub;");
        Environment.SetEnvironmentVariable("TWELVEDATA_API_KEY", "stub-twelvedata-key");
        Environment.SetEnvironmentVariable("FMP_API_KEY", "stub-fmp-key");
        Environment.SetEnvironmentVariable("FINNHUB_API_KEY", "stub-finnhub-key");
        Environment.SetEnvironmentVariable("EODHD_API_KEY", "stub-eodhd-key");
        Environment.SetEnvironmentVariable("MARKETAUX_API_TOKEN", "stub-marketaux-token");

        EndpointRegistry.Reset();
    }

    public void Dispose()
    {
        // Reset EndpointRegistry to pristine state
        EndpointRegistry.OverrideFilePath = null;
        EndpointRegistry.Reset();

        // Restore original environment variables
        Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", _originalDotnetEnv);
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", _originalAspnetcoreEnv);

        // Restore original test environment variables
        foreach (var kvp in _originalEnvVars)
        {
            Environment.SetEnvironmentVariable(kvp.Key, kvp.Value);
        }
    }

    /// <summary>
    /// Validates that the real endpoints.json file can be loaded and all endpoints can be resolved
    /// in Development environment with all required environment variables set to stub values.
    /// </summary>
    [Fact]
    public void ValidateAll_RealEndpointsWithDevEnvironment_DoesNotThrow()
    {
        // Act & Assert
        var action = () => EndpointRegistry.ValidateAll();
        action.Should().NotThrow();
    }

    /// <summary>
    /// Documents the expected endpoint and environment variable contract for the real endpoints.json.
    /// This test serves as executable documentation of what endpoints are required in Development.
    /// </summary>
    [Fact]
    public void ResolveAll_DevelopmentEnvironmentEndpoints_CanResolveExpectedKeys()
    {
        // This test documents the expected contract. If endpoints.json changes, this test
        // may need updating. All of these should resolve without throwing.

        // Database endpoint
        var database = EndpointRegistry.Resolve("database");
        database.Should().Contain("Server=localhost");

        // API compound endpoints with literal baseUrl and env apiKey
        var twelveDataBaseUrl = EndpointRegistry.Resolve("twelveData.baseUrl");
        twelveDataBaseUrl.Should().Be("https://api.twelvedata.com");

        var twelveDataApiKey = EndpointRegistry.Resolve("twelveData.apiKey");
        twelveDataApiKey.Should().Be("stub-twelvedata-key");

        var fmpBaseUrl = EndpointRegistry.Resolve("fmp.baseUrl");
        fmpBaseUrl.Should().Be("https://financialmodelingprep.com/stable");

        var fmpApiKey = EndpointRegistry.Resolve("fmp.apiKey");
        fmpApiKey.Should().Be("stub-fmp-key");

        var finnhubBaseUrl = EndpointRegistry.Resolve("finnhub.baseUrl");
        finnhubBaseUrl.Should().Be("https://finnhub.io/api/v1");

        var finnhubApiKey = EndpointRegistry.Resolve("finnhub.apiKey");
        finnhubApiKey.Should().Be("stub-finnhub-key");

        var eodhdBaseUrl = EndpointRegistry.Resolve("eodhd.baseUrl");
        eodhdBaseUrl.Should().Be("https://eodhd.com/api");

        var eodhdApiKey = EndpointRegistry.Resolve("eodhd.apiKey");
        eodhdApiKey.Should().Be("stub-eodhd-key");

        var marketauxBaseUrl = EndpointRegistry.Resolve("marketaux.baseUrl");
        marketauxBaseUrl.Should().Be("https://api.marketaux.com/v1");

        var marketauxApiToken = EndpointRegistry.Resolve("marketaux.apiKey");
        marketauxApiToken.Should().Be("stub-marketaux-token");

        // Literal endpoints (public APIs, no auth needed)
        var wikiSummaryUrl = EndpointRegistry.Resolve("wikipedia.summaryUrl");
        wikiSummaryUrl.Should().Contain("en.wikipedia.org");

        var wikiSearchUrl = EndpointRegistry.Resolve("wikipedia.searchUrl");
        wikiSearchUrl.Should().Contain("en.wikipedia.org");
    }
}
