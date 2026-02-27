namespace StockAnalyzer.Core.Tests.Data;

using System.Reflection;
using StockAnalyzer.Core.Services;
using Xunit;

/// <summary>
/// Tests SecurityMaster DTOs - ensures MicCode is used instead of Exchange property.
/// AC6.1: SecurityMasterCreateDto has MicCode not Exchange
/// AC6.3: SecurityMasterUpdateDto has MicCode not Exchange
/// Also ensures DTOs properly reflect entity design changes
/// </summary>
public class SecurityMasterDtoTests
{
    #region AC6.1: SecurityMasterCreateDto Tests

    [Fact]
    [Trait("AC", "6.1")]
    public void SecurityMasterCreateDto_HasMicCodeProperty()
    {
        // Arrange
        var property = typeof(SecurityMasterCreateDto).GetProperty(
            nameof(SecurityMasterCreateDto.MicCode),
            BindingFlags.Public | BindingFlags.Instance);

        // Act & Assert
        Assert.NotNull(property);
        Assert.True(property.PropertyType == typeof(string));
        Assert.True(property.CanRead && property.CanWrite);
    }

    [Fact]
    [Trait("AC", "6.1")]
    public void SecurityMasterCreateDto_MicCodePropertyIsNullable()
    {
        // Arrange & Act
        var dto = new SecurityMasterCreateDto
        {
            TickerSymbol = "AAPL",
            IssueName = "Apple Inc.",
            MicCode = null  // Should allow null for backfill scenarios
        };

        // Assert
        Assert.Null(dto.MicCode);
    }

    [Fact]
    [Trait("AC", "6.1")]
    public void SecurityMasterCreateDto_NoExchangeProperty()
    {
        // Arrange
        var property = typeof(SecurityMasterCreateDto).GetProperty(
            "Exchange",
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

        // Act & Assert
        Assert.Null(property);
    }

    [Fact]
    [Trait("AC", "6.1")]
    public void SecurityMasterCreateDto_HasAllRequiredProperties()
    {
        // Arrange
        var requiredProperties = new[] { nameof(SecurityMasterCreateDto.TickerSymbol), nameof(SecurityMasterCreateDto.IssueName) };

        // Act & Assert
        foreach (var propName in requiredProperties)
        {
            var property = typeof(SecurityMasterCreateDto).GetProperty(
                propName,
                BindingFlags.Public | BindingFlags.Instance);

            Assert.NotNull(property);
        }
    }

    [Fact]
    [Trait("AC", "6.1")]
    public void SecurityMasterCreateDto_HasOptionalProperties()
    {
        // Arrange
        var optionalProperties = new[]
        {
            nameof(SecurityMasterCreateDto.PrimaryAssetId),
            nameof(SecurityMasterCreateDto.MicCode),
            nameof(SecurityMasterCreateDto.SecurityType),
            nameof(SecurityMasterCreateDto.Country),
            nameof(SecurityMasterCreateDto.Currency),
            nameof(SecurityMasterCreateDto.Isin)
        };

        // Act & Assert
        foreach (var propName in optionalProperties)
        {
            var property = typeof(SecurityMasterCreateDto).GetProperty(
                propName,
                BindingFlags.Public | BindingFlags.Instance);

            Assert.NotNull(property);
        }
    }

    [Fact]
    [Trait("AC", "6.1")]
    public void SecurityMasterCreateDto_CanBeInstantiatedWithMicCode()
    {
        // Arrange
        var mic = "XNYS";
        var ticker = "AAPL";
        var name = "Apple Inc.";

        // Act
        var dto = new SecurityMasterCreateDto
        {
            TickerSymbol = ticker,
            IssueName = name,
            MicCode = mic,
            SecurityType = "Common Stock",
            Country = "USA",
            Currency = "USD",
            Isin = "US0378331005"
        };

        // Assert
        Assert.Equal(ticker, dto.TickerSymbol);
        Assert.Equal(name, dto.IssueName);
        Assert.Equal(mic, dto.MicCode);
        Assert.Equal("Common Stock", dto.SecurityType);
        Assert.Equal("USA", dto.Country);
        Assert.Equal("USD", dto.Currency);
        Assert.Equal("US0378331005", dto.Isin);
    }

    #endregion

    #region AC6.3: SecurityMasterUpdateDto Tests

    [Fact]
    [Trait("AC", "6.3")]
    public void SecurityMasterUpdateDto_HasMicCodeProperty()
    {
        // Arrange
        var property = typeof(SecurityMasterUpdateDto).GetProperty(
            nameof(SecurityMasterUpdateDto.MicCode),
            BindingFlags.Public | BindingFlags.Instance);

        // Act & Assert
        Assert.NotNull(property);
        Assert.True(property.PropertyType == typeof(string));
        Assert.True(property.CanRead && property.CanWrite);
    }

    [Fact]
    [Trait("AC", "6.3")]
    public void SecurityMasterUpdateDto_NoExchangeProperty()
    {
        // Arrange
        var property = typeof(SecurityMasterUpdateDto).GetProperty(
            "Exchange",
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

        // Act & Assert
        Assert.Null(property);
    }

    [Fact]
    [Trait("AC", "6.3")]
    public void SecurityMasterUpdateDto_AllPropertiesAreOptional()
    {
        // Arrange
        var propertyNames = new[]
        {
            nameof(SecurityMasterUpdateDto.IssueName),
            nameof(SecurityMasterUpdateDto.PrimaryAssetId),
            nameof(SecurityMasterUpdateDto.MicCode),
            nameof(SecurityMasterUpdateDto.SecurityType),
            nameof(SecurityMasterUpdateDto.Country),
            nameof(SecurityMasterUpdateDto.Currency),
            nameof(SecurityMasterUpdateDto.Isin),
            nameof(SecurityMasterUpdateDto.IsActive)
        };

        // Act & Assert
        var emptyDto = new SecurityMasterUpdateDto();

        foreach (var propName in propertyNames)
        {
            var property = typeof(SecurityMasterUpdateDto).GetProperty(
                propName,
                BindingFlags.Public | BindingFlags.Instance);

            Assert.NotNull(property);

            // All properties should be nullable (optional)
            var value = property!.GetValue(emptyDto);
            Assert.Null(value);
        }
    }

    [Fact]
    [Trait("AC", "6.3")]
    public void SecurityMasterUpdateDto_CanBePartiallyPopulated()
    {
        // Arrange
        var newMic = "XNAS";

        // Act
        var dto = new SecurityMasterUpdateDto
        {
            MicCode = newMic
            // All other properties left as null
        };

        // Assert
        Assert.Equal(newMic, dto.MicCode);
        Assert.Null(dto.IssueName);
        Assert.Null(dto.SecurityType);
        Assert.Null(dto.Country);
        Assert.Null(dto.Currency);
        Assert.Null(dto.Isin);
        Assert.Null(dto.IsActive);
    }

    [Fact]
    [Trait("AC", "6.3")]
    public void SecurityMasterUpdateDto_IsActiveCanBeSet()
    {
        // Arrange & Act
        var dtoDeactivate = new SecurityMasterUpdateDto { IsActive = false };
        var dtoActivate = new SecurityMasterUpdateDto { IsActive = true };

        // Assert
        Assert.False(dtoDeactivate.IsActive);
        Assert.True(dtoActivate.IsActive);
    }

    #endregion

    #region Comparative Tests: CreateDto vs UpdateDto

    [Fact]
    [Trait("Category", "Comparison")]
    public void CreateDto_HasTickerSymbolButUpdateDtoDoesNot()
    {
        // Arrange & Act
        var createHasTickerSymbol = typeof(SecurityMasterCreateDto).GetProperty(
            nameof(SecurityMasterCreateDto.TickerSymbol),
            BindingFlags.Public | BindingFlags.Instance) != null;

        var updateHasTickerSymbol = typeof(SecurityMasterUpdateDto).GetProperty(
            "TickerSymbol",
            BindingFlags.Public | BindingFlags.Instance) != null;

        // Assert
        Assert.True(createHasTickerSymbol);
        Assert.False(updateHasTickerSymbol);
    }

    [Fact]
    [Trait("Category", "Comparison")]
    public void BothDtos_HaveMicCodeAndNotExchange()
    {
        // Arrange
        var createHasMicCode = typeof(SecurityMasterCreateDto).GetProperty(
            nameof(SecurityMasterCreateDto.MicCode),
            BindingFlags.Public | BindingFlags.Instance) != null;

        var updateHasMicCode = typeof(SecurityMasterUpdateDto).GetProperty(
            nameof(SecurityMasterUpdateDto.MicCode),
            BindingFlags.Public | BindingFlags.Instance) != null;

        var createHasExchange = typeof(SecurityMasterCreateDto).GetProperty(
            "Exchange",
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase) != null;

        var updateHasExchange = typeof(SecurityMasterUpdateDto).GetProperty(
            "Exchange",
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase) != null;

        // Assert
        Assert.True(createHasMicCode, "CreateDto should have MicCode");
        Assert.True(updateHasMicCode, "UpdateDto should have MicCode");
        Assert.False(createHasExchange, "CreateDto should not have Exchange");
        Assert.False(updateHasExchange, "UpdateDto should not have Exchange");
    }

    #endregion

    #region Record-based Tests

    [Fact]
    [Trait("Category", "Record")]
    public void SecurityMasterCreateDto_IsRecord()
    {
        // Arrange
        var dto1 = new SecurityMasterCreateDto
        {
            TickerSymbol = "AAPL",
            IssueName = "Apple Inc.",
            MicCode = "XNAS"
        };

        var dto2 = new SecurityMasterCreateDto
        {
            TickerSymbol = "AAPL",
            IssueName = "Apple Inc.",
            MicCode = "XNAS"
        };

        // Act & Assert
        Assert.Equal(dto1, dto2);
    }

    [Fact]
    [Trait("Category", "Record")]
    public void SecurityMasterUpdateDto_IsRecord()
    {
        // Arrange
        var dto1 = new SecurityMasterUpdateDto { MicCode = "XNAS" };
        var dto2 = new SecurityMasterUpdateDto { MicCode = "XNAS" };

        // Act & Assert
        Assert.Equal(dto1, dto2);
    }

    #endregion
}
