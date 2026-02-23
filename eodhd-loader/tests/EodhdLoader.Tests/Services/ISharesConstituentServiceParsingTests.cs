namespace EodhdLoader.Tests.Services;

using System.Text.Json;
using EodhdLoader.Models;
using EodhdLoader.Services;
using Xunit;

/// <summary>
/// Tests for ISharesConstituentService holdings parsing.
/// Verifies AC2: Holdings Parsing (Format A/B detection, filtering, edge cases).
/// </summary>
public class ISharesConstituentServiceParsingTests
{
    /// <summary>
    /// AC2.1: Parse Format A JSON (IVV-style, 17 cols) with correct weights and all fields.
    /// </summary>
    [Fact]
    public void ParseFormatA_ReturnsAllEquityHoldings_WithCorrectWeights()
    {
        // Arrange
        var formatAJson = LoadTestFixture("format_a_sample.json");
        using var doc = JsonDocument.Parse(formatAJson);
        var data = doc.RootElement.Clone();

        // Act
        var (holdings, skippedRows) = ISharesConstituentService.ParseHoldings(data);

        // Assert
        // Format A has 3 equity rows (AAPL, MSFT, JPM) + 1 Cash (filtered) + 1 DUMMY (included despite missing values)
        // So we expect 4 equity holdings (3 normal + 1 with missing weight/market value)
        Assert.Equal(4, holdings.Count);

        // Verify first holding (AAPL)
        var aapl = holdings[0];
        Assert.Equal("AAPL", aapl.Ticker);
        Assert.Equal("Apple Inc", aapl.Name);
        Assert.Equal("Information Technology", aapl.Sector);
        Assert.Equal(0.0765m, aapl.Weight); // 7.65 / 100
        Assert.Equal(183450123456m, aapl.MarketValue);
        Assert.Equal(1000000m, aapl.Shares);
        Assert.Equal("037833100", aapl.Cusip);
        Assert.Equal("US0378331005", aapl.Isin);
        Assert.Equal("2046251", aapl.Sedol);

        // Verify second holding (MSFT)
        var msft = holdings[1];
        Assert.Equal("MSFT", msft.Ticker);
        Assert.Equal("Microsoft Corporation", msft.Name);
        Assert.Equal(0.065m, msft.Weight); // 6.5 / 100
        Assert.Equal(172100000000m, msft.MarketValue);

        // Verify third holding (JPM)
        var jpm = holdings[2];
        Assert.Equal("JPM", jpm.Ticker);
        Assert.Equal("JPMorgan Chase & Co", jpm.Name);
        Assert.Equal("Financials", jpm.Sector);
        Assert.Equal(0.0325m, jpm.Weight); // 3.25 / 100
    }

    /// <summary>
    /// AC2.2: Parse Format B JSON (IJK-style, 19 cols) with weight from column 17, sector from column 3.
    /// </summary>
    [Fact]
    public void ParseFormatB_ReturnsAllEquityHoldings_WithCorrectColumns()
    {
        // Arrange
        var formatBJson = LoadTestFixture("format_b_sample.json");
        using var doc = JsonDocument.Parse(formatBJson);
        var data = doc.RootElement.Clone();

        // Act
        var (holdings, skippedRows) = ISharesConstituentService.ParseHoldings(data);

        // Assert
        // Format B has 3 equity rows (AAPL, MSFT, JPM) + 1 Futures (filtered) + 1 DUMMY (included)
        // So we expect 4 equity holdings
        Assert.Equal(4, holdings.Count);

        // Verify first holding (AAPL)
        var aapl = holdings[0];
        Assert.Equal("AAPL", aapl.Ticker);
        Assert.Equal("Apple Inc", aapl.Name);
        Assert.Equal("Information Technology", aapl.Sector); // From column 3 in Format B
        Assert.Equal(0.0765m, aapl.Weight); // From column 17 in Format B: "7.65" / 100
        Assert.Equal(183450123456m, aapl.MarketValue); // From column 5

        // Verify second holding (MSFT)
        var msft = holdings[1];
        Assert.Equal("MSFT", msft.Ticker);
        Assert.Equal("Microsoft Corporation", msft.Name);
        Assert.Equal("Information Technology", msft.Sector);
        Assert.Equal(0.065m, msft.Weight);

        // Verify third holding (JPM)
        var jpm = holdings[2];
        Assert.Equal("JPM", jpm.Ticker);
        Assert.Equal("Financials", jpm.Sector);
        Assert.Equal(0.0325m, jpm.Weight);
    }

    /// <summary>
    /// AC2.3: Non-equity rows (Cash, Futures, Money Market) are filtered out.
    /// </summary>
    [Fact]
    public void ParseFormatA_FiltersNonEquityRows()
    {
        // Arrange
        var formatAJson = LoadTestFixture("format_a_sample.json");
        using var doc = JsonDocument.Parse(formatAJson);
        var data = doc.RootElement.Clone();

        // Act
        var (holdings, skippedRows) = ISharesConstituentService.ParseHoldings(data);

        // Assert
        // CASH row (with asset_class="Cash") should NOT be in holdings
        var tickers = holdings.Select(h => h.Ticker).ToList();
        Assert.DoesNotContain("CASH", tickers);
    }

    /// <summary>
    /// AC2.3: Format B filters Futures rows.
    /// </summary>
    [Fact]
    public void ParseFormatB_FiltersNonEquityRows()
    {
        // Arrange
        var formatBJson = LoadTestFixture("format_b_sample.json");
        using var doc = JsonDocument.Parse(formatBJson);
        var data = doc.RootElement.Clone();

        // Act
        var (holdings, skippedRows) = ISharesConstituentService.ParseHoldings(data);

        // Assert
        // FUT1 row (with asset_class="Futures") should NOT be in holdings
        var tickers = holdings.Select(h => h.Ticker).ToList();
        Assert.DoesNotContain("FUT1", tickers);
    }

    /// <summary>
    /// AC2.4: Malformed JSON returns empty list, no exception thrown.
    /// </summary>
    [Fact]
    public void ParseMalformedJson_ReturnsEmptyList()
    {
        // Arrange: No aaData key
        using var doc = JsonDocument.Parse("{}");
        var data = doc.RootElement.Clone();

        // Act
        var (holdings, skippedRows) = ISharesConstituentService.ParseHoldings(data);

        // Assert
        Assert.Empty(holdings);
    }

    /// <summary>
    /// AC2.4: Empty aaData array returns empty list.
    /// </summary>
    [Fact]
    public void ParseEmptyAaData_ReturnsEmptyList()
    {
        // Arrange
        using var doc = JsonDocument.Parse("{\"aaData\": []}");
        var data = doc.RootElement.Clone();

        // Act
        var (holdings, skippedRows) = ISharesConstituentService.ParseHoldings(data);

        // Assert
        Assert.Empty(holdings);
    }

    /// <summary>
    /// AC2.5: Holdings with missing weight or market value are included with null values.
    /// </summary>
    [Fact]
    public void ParseHoldingsWithMissingValues_IncludesThemWithNullProperties()
    {
        // Arrange
        var formatAJson = LoadTestFixture("format_a_sample.json");
        using var doc = JsonDocument.Parse(formatAJson);
        var data = doc.RootElement.Clone();

        // Act
        var (holdings, skippedRows) = ISharesConstituentService.ParseHoldings(data);

        // Assert
        // DUMMY holding should be present but with null weight and market value
        var dummy = holdings.FirstOrDefault(h => h.Ticker == "DUMMY");
        Assert.NotNull(dummy);
        Assert.Null(dummy!.Weight);
        Assert.Null(dummy!.MarketValue);
        Assert.Equal("US1234567890", dummy!.Isin); // Should still have other values
    }

    /// <summary>
    /// AC2.5: Format B holdings with missing values are included.
    /// </summary>
    [Fact]
    public void ParseFormatBHoldingsWithMissingValues_IncludesThemWithNullProperties()
    {
        // Arrange
        var formatBJson = LoadTestFixture("format_b_sample.json");
        using var doc = JsonDocument.Parse(formatBJson);
        var data = doc.RootElement.Clone();

        // Act
        var (holdings, skippedRows) = ISharesConstituentService.ParseHoldings(data);

        // Assert
        // DUMMY holding should be present but with null weight and market value
        var dummy = holdings.FirstOrDefault(h => h.Ticker == "DUMMY");
        Assert.NotNull(dummy);
        Assert.Null(dummy!.Weight);
        Assert.Null(dummy!.MarketValue);
        Assert.Equal("Utilities", dummy!.Sector);
    }

    /// <summary>
    /// Load a JSON fixture from TestData directory.
    /// Fixtures are copied to bin output directory by csproj Content item.
    /// </summary>
    private static string LoadTestFixture(string filename)
    {
        var assemblyDir = Path.GetDirectoryName(typeof(ISharesConstituentServiceParsingTests).Assembly.Location)
                          ?? AppContext.BaseDirectory;

        // Fixtures are in TestData subdirectory of bin output
        var path = Path.Combine(assemblyDir, "TestData", filename);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Test fixture not found: {path}");
        }

        return File.ReadAllText(path);
    }
}
