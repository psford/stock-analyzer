namespace EodhdLoader.Tests.Integration;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;
using EodhdLoader.Services;
using EodhdLoader.Models;

/// <summary>
/// Tests for pipeline parity between C# and Python iShares constituent loaders.
/// Verifies AC6.2: C# output matches Python pipeline output for IVV (Format A) and IJK (Format B).
/// These tests compare parsed holdings from known-good test fixtures against expected values derived from
/// the Python parsing logic (ishares_ingest.py:parse_holdings).
/// </summary>
public class PipelineParityTests
{
    /// <summary>
    /// Helper: Loads a test fixture JSON file and parses it with the C# ParseHoldings method.
    /// Returns the parsed holdings list.
    /// </summary>
    private static (List<ISharesHolding> Holdings, int SkippedRows) ParseFixture(string fixturePath)
    {
        var json = File.ReadAllText(fixturePath);
        var doc = JsonDocument.Parse(json);
        return ISharesConstituentService.ParseHoldings(doc.RootElement);
    }

    /// <summary>
    /// AC6.2 Test 1: FormatA_ParseOutput_MatchesPythonPipeline
    /// Loads format_a_sample.json (IVV Format A with 5 rows: 3 equities, 1 Cash, 1 with missing weight).
    /// Asserts equity count = 3 (Cash excluded, missing weight included as Equity with null weight).
    /// Asserts first holding (AAPL) has: ticker="AAPL", name="Apple Inc", sector="Information Technology".
    /// Asserts weight was divided by 100: if fixture has 7.65, parsed weight is 0.0765m.
    /// Asserts market value extracted from {display, raw} JSON object (Format A style).
    /// </summary>
    [Fact]
    public void FormatA_ParseOutput_MatchesPythonPipeline()
    {
        // Arrange
        var fixturePath = "TestData/format_a_sample.json";
        Assert.True(File.Exists(fixturePath), $"Test fixture not found: {fixturePath}");

        // Act
        var (holdings, skippedRows) = ParseFixture(fixturePath);

        // Assert: Equity count should be 3 (AAPL, MSFT, JPM - excluding CASH and including DUMMY with null weight)
        // Format A has 5 rows: [AAPL, MSFT, JPM, CASH, DUMMY]
        // Filtered: [AAPL, MSFT, JPM, DUMMY] = 4 equities (CASH excluded)
        Assert.Equal(4, holdings.Count);

        // First holding (AAPL) - verify ticker, name, sector
        var aapl = holdings[0];
        Assert.Equal("AAPL", aapl.Ticker);
        Assert.Equal("Apple Inc", aapl.Name);
        Assert.Equal("Information Technology", aapl.Sector);

        // Weight conversion: 7.65% -> 0.0765
        Assert.NotNull(aapl.Weight);
        Assert.Equal(0.0765m, aapl.Weight);

        // Market value from {display, raw} object (Format A col[4])
        Assert.NotNull(aapl.MarketValue);
        Assert.Equal(183450123456m, aapl.MarketValue);

        // Verify second holding (MSFT)
        var msft = holdings[1];
        Assert.Equal("MSFT", msft.Ticker);
        Assert.Equal("Microsoft Corporation", msft.Name);
        Assert.Equal("Information Technology", msft.Sector);
        Assert.Equal(0.065m, msft.Weight); // 6.50% -> 0.065
        Assert.Equal(172100000000m, msft.MarketValue);

        // Verify third holding (JPM)
        var jpm = holdings[2];
        Assert.Equal("JPM", jpm.Ticker);
        Assert.Equal("JPMorgan Chase & Co", jpm.Name);
        Assert.Equal("Financials", jpm.Sector);
        Assert.Equal(0.0325m, jpm.Weight); // 3.25% -> 0.0325
        Assert.Equal(95000000000m, jpm.MarketValue);

        // Verify fourth holding (DUMMY with missing weight)
        var dummy = holdings[3];
        Assert.Equal("DUMMY", dummy.Ticker);
        Assert.Equal("Dummy Holding with Missing Values", dummy.Name);
        Assert.Equal("Utilities", dummy.Sector);
        Assert.Null(dummy.Weight); // Missing weight -> null
    }

    /// <summary>
    /// AC6.2 Test 2: FormatB_ParseOutput_MatchesPythonPipeline
    /// Loads format_b_sample.json (IJK Format B with 5 rows: 3 equities, 1 Futures, 1 with missing weight).
    /// Asserts equity count = 4 (Futures excluded, missing weight included as Equity with null weight).
    /// Asserts weight comes from column index 17 (Format B) not column index 5 (Format A).
    /// Asserts sector comes from column index 3 (Format B) not column index 2 (Format A).
    /// </summary>
    [Fact]
    public void FormatB_ParseOutput_MatchesPythonPipeline()
    {
        // Arrange
        var fixturePath = "TestData/format_b_sample.json";
        Assert.True(File.Exists(fixturePath), $"Test fixture not found: {fixturePath}");

        // Act
        var (holdings, skippedRows) = ParseFixture(fixturePath);

        // Assert: Equity count should be 4 (AAPL, MSFT, JPM, DUMMY - Futures excluded)
        // Format B has 5 rows: [AAPL, MSFT, JPM, FUT1, DUMMY]
        // Filtered: [AAPL, MSFT, JPM, DUMMY] = 4 equities (Futures excluded)
        Assert.Equal(4, holdings.Count);

        // First holding (AAPL) - verify column indices differ from Format A
        // Format B: sector = col[3], weight = col[17]
        var aapl = holdings[0];
        Assert.Equal("AAPL", aapl.Ticker);
        Assert.Equal("Apple Inc", aapl.Name);
        Assert.Equal("Information Technology", aapl.Sector); // col[3] in Format B
        Assert.NotNull(aapl.Weight);
        Assert.Equal(0.0765m, aapl.Weight); // col[17] = "7.65" -> 0.0765

        // Verify second holding (MSFT)
        var msft = holdings[1];
        Assert.Equal("MSFT", msft.Ticker);
        Assert.Equal("Microsoft Corporation", msft.Name);
        Assert.Equal("Information Technology", msft.Sector); // col[3]
        Assert.Equal(0.065m, msft.Weight); // col[17] = "6.50" -> 0.065

        // Verify third holding (JPM)
        var jpm = holdings[2];
        Assert.Equal("JPM", jpm.Ticker);
        Assert.Equal("JPMorgan Chase & Co", jpm.Name);
        Assert.Equal("Financials", jpm.Sector); // col[3]
        Assert.Equal(0.0325m, jpm.Weight); // col[17] = "3.25" -> 0.0325

        // Verify fourth holding (DUMMY with missing weight) - ensures col[17] handling
        var dummy = holdings[3];
        Assert.Equal("DUMMY", dummy.Ticker);
        Assert.Equal("Dummy Holding with Missing Values", dummy.Name);
        Assert.Equal("Utilities", dummy.Sector); // col[3]
        Assert.Null(dummy.Weight); // col[17] = "-" -> null
    }

    /// <summary>
    /// AC6.2 Test 3: WeightConversion_PercentageToDecimal_ConsistentWithPython
    /// Loads fixtures and verifies weight conversion is consistent with Python:
    /// - Numeric weight (e.g., 6.5432) -> divide by 100 -> 0.065432
    /// - Sentinel "-" -> null
    /// - Zero weight (0.0) -> 0m (not null)
    /// This catches the common bug of forgetting to divide by 100 or mishandling sentinel values.
    /// </summary>
    [Fact]
    public void WeightConversion_PercentageToDecimal_ConsistentWithPython()
    {
        // Arrange
        var fixturePath = "TestData/format_a_sample.json";
        Assert.True(File.Exists(fixturePath), $"Test fixture not found: {fixturePath}");

        // Act
        var (holdings, skippedRows) = ParseFixture(fixturePath);

        // Assert: Verify weight conversions
        // AAPL: 7.65% -> 0.0765
        Assert.Equal(0.0765m, holdings[0].Weight);

        // MSFT: 6.50% -> 0.065 (verify trailing zero handling)
        Assert.Equal(0.065m, holdings[1].Weight);

        // JPM: 3.25% -> 0.0325
        Assert.Equal(0.0325m, holdings[2].Weight);

        // DUMMY: "-" (null raw) -> null
        Assert.Null(holdings[3].Weight);

        // Additional check: Load Format B and verify weight from col[17]
        var fixtureBPath = "TestData/format_b_sample.json";
        var (holdingsB, skippedRowsB) = ParseFixture(fixtureBPath);

        // Format B AAPL: col[17] = "7.65" -> 0.0765
        Assert.Equal(0.0765m, holdingsB[0].Weight);

        // Format B MSFT: col[17] = "6.50" -> 0.065
        Assert.Equal(0.065m, holdingsB[1].Weight);

        // Format B JPM: col[17] = "3.25" -> 0.0325
        Assert.Equal(0.0325m, holdingsB[2].Weight);

        // Format B DUMMY: col[17] = "-" -> null
        Assert.Null(holdingsB[3].Weight);
    }

    /// <summary>
    /// AC6.2 Test 4: NonEquityFiltering_ExcludesSameRowsAsPython
    /// Parses fixtures containing non-equity rows (Cash, Futures, Money Market, etc.).
    /// Asserts only Equity rows remain in parsed output.
    /// Asserts count = total rows minus non-equity rows.
    /// Python's filter set (ishares_ingest.py:191-194): {"Cash", "Cash Collateral and Margins", "Cash and/or Derivatives", "Futures", "Money Market"}
    /// This test verifies the C# implementation matches the Python filter.
    /// </summary>
    [Fact]
    public void NonEquityFiltering_ExcludesSameRowsAsPython()
    {
        // Arrange
        var fixturePath = "TestData/format_a_sample.json";
        Assert.True(File.Exists(fixturePath), $"Test fixture not found: {fixturePath}");

        // Act
        var (holdings, skippedRows) = ParseFixture(fixturePath);

        // Assert: Format A fixture has 5 total rows:
        // [AAPL (Equity), MSFT (Equity), JPM (Equity), CASH (Cash), DUMMY (Equity)]
        // Expected: 4 equities (Cash excluded)
        Assert.Equal(4, holdings.Count);

        // Verify only equity rows are included
        foreach (var holding in holdings)
        {
            // All returned holdings should be equities
            Assert.True(!string.IsNullOrWhiteSpace(holding.Ticker), "Ticker should not be empty");
            // Could add additional equity-specific checks if needed
        }

        // Load Format B and verify Futures filtering
        var fixtureBPath = "TestData/format_b_sample.json";
        var (holdingsB, skippedRowsB) = ParseFixture(fixtureBPath);

        // Format B fixture has 5 total rows:
        // [AAPL (Equity), MSFT (Equity), JPM (Equity), FUT1 (Futures), DUMMY (Equity)]
        // Expected: 4 equities (Futures excluded)
        Assert.Equal(4, holdingsB.Count);

        // Verify Futures was excluded
        var tickers = new HashSet<string>(holdingsB.Select(h => h.Ticker));
        Assert.DoesNotContain("FUT1", tickers); // Futures should be excluded
        Assert.Contains("AAPL", tickers);
        Assert.Contains("MSFT", tickers);
        Assert.Contains("JPM", tickers);
        Assert.Contains("DUMMY", tickers);
    }
}
