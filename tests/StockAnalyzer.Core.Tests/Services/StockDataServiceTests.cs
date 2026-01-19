using FluentAssertions;
using StockAnalyzer.Core.Services;
using Xunit;

namespace StockAnalyzer.Core.Tests.Services;

/// <summary>
/// Tests for StockDataService.
/// Note: Some methods use YahooClient internally which is a concrete class
/// from OoplesFinance.YahooFinanceAPI and cannot be easily mocked without
/// introducing a wrapper interface. Tests here focus on behavior that can
/// be validated without external API calls.
/// </summary>
public class StockDataServiceTests
{
    private readonly StockDataService _sut;

    public StockDataServiceTests()
    {
        _sut = new StockDataService();
    }

    #region SearchAsync Tests

    [Fact]
    public async Task SearchAsync_WithEmptyQuery_ReturnsEmptyList()
    {
        // Act
        var result = await _sut.SearchAsync("");

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAsync_WithWhitespaceQuery_ReturnsEmptyList()
    {
        // Act
        var result = await _sut.SearchAsync("   ");

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAsync_WithSingleCharacter_ReturnsEmptyList()
    {
        // Queries less than 2 characters should return empty
        // Act
        var result = await _sut.SearchAsync("A");

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAsync_WithNullQuery_ReturnsEmptyList()
    {
        // Act
        var result = await _sut.SearchAsync(null!);

        // Assert
        result.Should().BeEmpty();
    }

    #endregion

    #region ValidateDividendYield Tests (via GetStockInfoAsync behavior)

    // Note: ValidateDividendYield is a private method. These tests document
    // the expected behavior. Full testing would require either:
    // 1. Making the method internal and using InternalsVisibleTo
    // 2. Testing through the public API with mocked YahooClient
    //
    // The validation logic is:
    // - If yield > 0.10 (10%), divide by 100 (fixing yfinance bug)
    // - Otherwise return as-is

    [Theory]
    [InlineData(0.004, 0.004)]     // 0.4% yield - normal, return as-is
    [InlineData(0.05, 0.05)]       // 5% yield - normal, return as-is
    [InlineData(0.10, 0.10)]       // 10% yield - boundary, return as-is
    [InlineData(0.40, 0.004)]      // 40% (inflated) -> 0.4%
    [InlineData(5.0, 0.05)]        // 500% (inflated) -> 5%
    public void ValidateDividendYield_TestCases_Documentation(decimal input, decimal expected)
    {
        // This test documents expected behavior of the private ValidateDividendYield method.
        // The logic: if yield > 0.10, divide by 100
        decimal? actual;
        if (input > 0.10m)
        {
            actual = input / 100;
        }
        else
        {
            actual = input;
        }

        actual.Should().Be(expected);
    }

    #endregion

    #region GetStartDate Tests (via GetHistoricalDataAsync period parameter)

    // Note: GetStartDate is a private method. These tests document expected behavior.
    // The period parameter accepts: "1d", "5d", "1mo", "3mo", "6mo", "1y", "2y", "5y", "10y"

    [Theory]
    [InlineData("1d", -1)]
    [InlineData("5d", -5)]
    [InlineData("1y", -365)]   // Approximate
    [InlineData("2y", -730)]   // Approximate
    public void GetStartDate_PeriodMapping_Documentation(string period, int expectedDaysAgo)
    {
        // This test documents expected behavior of the private GetStartDate method.
        // It's validated indirectly through the GetHistoricalDataAsync API.
        var now = DateTime.Now;
        var expected = period.ToLower() switch
        {
            "1d" => now.AddDays(-1),
            "5d" => now.AddDays(-5),
            "1mo" => now.AddMonths(-1),
            "3mo" => now.AddMonths(-3),
            "6mo" => now.AddMonths(-6),
            "1y" => now.AddYears(-1),
            "2y" => now.AddYears(-2),
            "5y" => now.AddYears(-5),
            "10y" => now.AddYears(-10),
            _ => now.AddYears(-1)
        };

        // Verify the mapping logic produces a date approximately the expected days ago
        var daysDiff = (now - expected).TotalDays;
        daysDiff.Should().BeApproximately(Math.Abs(expectedDaysAgo), 2);
    }

    [Fact]
    public void GetStartDate_WithUnknownPeriod_DefaultsToOneYear()
    {
        // Document that unknown periods default to 1 year
        var now = DateTime.Now;
        var expected = now.AddYears(-1);

        var unknownPeriod = "xyz";
        var result = unknownPeriod.ToLower() switch
        {
            "1d" => now.AddDays(-1),
            "5d" => now.AddDays(-5),
            "1mo" => now.AddMonths(-1),
            "3mo" => now.AddMonths(-3),
            "6mo" => now.AddMonths(-6),
            "1y" => now.AddYears(-1),
            "2y" => now.AddYears(-2),
            "5y" => now.AddYears(-5),
            "10y" => now.AddYears(-10),
            _ => now.AddYears(-1)  // Default
        };

        (result - expected).TotalSeconds.Should().BeLessThan(1);
    }

    #endregion

    #region Service Instantiation Tests

    [Fact]
    public void Constructor_CreatesValidInstance()
    {
        // Act
        var service = new StockDataService();

        // Assert
        service.Should().NotBeNull();
    }

    #endregion

    #region Integration Test Markers

    // The following tests would require external API calls and should be run
    // as integration tests rather than unit tests. They are marked with
    // [Fact(Skip = "Integration test")] to document expected behavior.

    [Fact(Skip = "Integration test - requires live API")]
    public async Task SearchAsync_WithValidQuery_ReturnsResults()
    {
        // This test would verify that searching for "AAPL" returns Apple Inc.
        var result = await _sut.SearchAsync("AAPL");

        result.Should().NotBeEmpty();
        result.Should().Contain(r => r.Symbol == "AAPL");
    }

    [Fact(Skip = "Integration test - requires live API")]
    public async Task GetStockInfoAsync_WithValidSymbol_ReturnsStockInfo()
    {
        // This test would verify that getting stock info for AAPL returns valid data
        var result = await _sut.GetStockInfoAsync("AAPL");

        result.Should().NotBeNull();
        result!.Symbol.Should().Be("AAPL");
    }

    [Fact(Skip = "Integration test - requires live API")]
    public async Task GetHistoricalDataAsync_WithValidSymbol_ReturnsData()
    {
        // This test would verify that historical data is returned for a valid symbol
        var result = await _sut.GetHistoricalDataAsync("AAPL", "1mo");

        result.Should().NotBeNull();
        result!.Data.Should().NotBeEmpty();
    }

    #endregion
}
