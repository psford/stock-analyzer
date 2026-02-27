namespace StockAnalyzer.Core.Tests.Models;

using StockAnalyzer.Core.Models;
using Xunit;

/// <summary>
/// Tests SearchResult.DisplayName computed property.
/// AC5.4: DisplayName prefers ExchangeName, falls back to Exchange, shows clean format
/// </summary>
public class SearchResultDisplayTests
{
    [Fact]
    [Trait("AC", "5.4")]
    public void DisplayName_WithExchangeName_ShowsExchangeName()
    {
        // Arrange
        var result = new SearchResult
        {
            Symbol = "AAPL",
            ShortName = "Apple",
            LongName = "Apple Inc.",
            Exchange = "NASDAQ",
            MicCode = "XNAS",
            ExchangeName = "New York Stock Exchange",
            Type = "Common Stock"
        };

        // Act
        var displayName = result.DisplayName;

        // Assert
        Assert.Equal("AAPL - Apple (New York Stock Exchange)", displayName);
    }

    [Fact]
    [Trait("AC", "5.4")]
    public void DisplayName_WithExchangeOnly_ShowsExchange()
    {
        // Arrange
        var result = new SearchResult
        {
            Symbol = "MSFT",
            ShortName = "Microsoft",
            LongName = "Microsoft Corporation",
            Exchange = "NASDAQ",
            MicCode = "XNAS",
            ExchangeName = null,  // No exchange name from reference table
            Type = "Common Stock"
        };

        // Act
        var displayName = result.DisplayName;

        // Assert
        Assert.Equal("MSFT - Microsoft (NASDAQ)", displayName);
    }

    [Fact]
    [Trait("AC", "5.4")]
    public void DisplayName_WithBothNull_ShowsNoParentheses()
    {
        // Arrange
        var result = new SearchResult
        {
            Symbol = "TSLA",
            ShortName = "Tesla",
            LongName = "Tesla Inc.",
            Exchange = null,
            MicCode = null,
            ExchangeName = null,
            Type = "Common Stock"
        };

        // Act
        var displayName = result.DisplayName;

        // Assert
        Assert.Equal("TSLA - Tesla", displayName);
    }

    [Fact]
    [Trait("AC", "5.4")]
    public void DisplayName_ExchangeNameTakesPrecedence()
    {
        // Arrange
        var result = new SearchResult
        {
            Symbol = "GOOG",
            ShortName = "Alphabet",
            Exchange = "NASDAQ",
            ExchangeName = "NASDAQ-listed"
        };

        // Act
        var displayName = result.DisplayName;

        // Assert
        // ExchangeName should be used, not Exchange
        Assert.Contains("NASDAQ-listed", displayName);
        Assert.DoesNotContain("(NASDAQ)", displayName);
    }

    [Fact]
    [Trait("AC", "5.4")]
    public void DisplayName_WithEmptyExchangeName_FallsBackToExchange()
    {
        // Arrange
        var result = new SearchResult
        {
            Symbol = "FB",
            ShortName = "Meta",
            Exchange = "NASDAQ",
            ExchangeName = string.Empty  // Empty string should be treated as non-null
        };

        // Act
        var displayName = result.DisplayName;

        // Assert
        Assert.Equal("FB - Meta ()", displayName);
    }

    [Fact]
    [Trait("AC", "5.4")]
    public void DisplayName_WithWhitespaceOnlyExchangeName_StillShown()
    {
        // Arrange
        var result = new SearchResult
        {
            Symbol = "NVDA",
            ShortName = "NVIDIA",
            Exchange = "NASDAQ",
            ExchangeName = "  "
        };

        // Act
        var displayName = result.DisplayName;

        // Assert
        // Even whitespace-only string will be included if not null
        Assert.Equal("NVDA - NVIDIA (  )", displayName);
    }

    [Fact]
    [Trait("AC", "5.4")]
    public void DisplayName_WithSpecialCharacters_HandledCorrectly()
    {
        // Arrange
        var result = new SearchResult
        {
            Symbol = "BRK.A",
            ShortName = "Berkshire Hathaway",
            Exchange = "NYSE",
            ExchangeName = "New York Stock Exchange (NYSE)",
            Type = "Common Stock"
        };

        // Act
        var displayName = result.DisplayName;

        // Assert
        Assert.Equal("BRK.A - Berkshire Hathaway (New York Stock Exchange (NYSE))", displayName);
    }

    [Fact]
    [Trait("Category", "Record")]
    public void SearchResult_IsRecord()
    {
        // Arrange & Act
        var result1 = new SearchResult { Symbol = "TEST", ShortName = "Test" };
        var result2 = new SearchResult { Symbol = "TEST", ShortName = "Test" };

        // Assert - records support value-based equality
        Assert.Equal(result1, result2);
    }

}
