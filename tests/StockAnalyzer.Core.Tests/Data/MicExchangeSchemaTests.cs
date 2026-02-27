namespace StockAnalyzer.Core.Tests.Data;

using System.Reflection;
using Microsoft.EntityFrameworkCore;
using StockAnalyzer.Core.Data;
using StockAnalyzer.Core.Data.Entities;
using Xunit;

/// <summary>
/// Tests MicExchangeEntity schema configuration and relationships.
/// AC1.4: MicExchangeEntity primary key, required properties, and collections
/// AC2.2: SecurityMasterEntity has MicCode property
/// AC2.4: SecurityMasterEntity has MicExchange navigation and no Exchange property
/// </summary>
public class MicExchangeSchemaTests
{
    private DbContextOptions<StockAnalyzerDbContext> CreateInMemoryOptions()
    {
        return new DbContextOptionsBuilder<StockAnalyzerDbContext>()
            .UseInMemoryDatabase(databaseName: $"MicExchangeTest_{Guid.NewGuid()}")
            .Options;
    }

    #region AC1.4: MicExchangeEntity Schema Tests

    [Fact]
    [Trait("AC", "1.4")]
    public void MicExchangeEntity_HasCorrectPrimaryKey()
    {
        // Arrange
        using var context = new StockAnalyzerDbContext(CreateInMemoryOptions());
        var entity = context.Model.FindEntityType(typeof(MicExchangeEntity));

        // Act
        var primaryKey = entity!.FindPrimaryKey();

        // Assert
        Assert.NotNull(primaryKey);
        Assert.Single(primaryKey.Properties);
        Assert.Equal(nameof(MicExchangeEntity.MicCode), primaryKey.Properties[0].Name);
    }

    [Fact]
    [Trait("AC", "1.4")]
    public void MicExchangeEntity_HasRequiredProperties()
    {
        // Arrange
        using var context = new StockAnalyzerDbContext(CreateInMemoryOptions());
        var entity = context.Model.FindEntityType(typeof(MicExchangeEntity));

        // Act
        var micCodeProperty = entity!.FindProperty(nameof(MicExchangeEntity.MicCode));
        var exchangeNameProperty = entity.FindProperty(nameof(MicExchangeEntity.ExchangeName));
        var countryProperty = entity.FindProperty(nameof(MicExchangeEntity.Country));

        // Assert
        Assert.NotNull(micCodeProperty);
        Assert.False(micCodeProperty.IsNullable, "MicCode should be required");

        Assert.NotNull(exchangeNameProperty);
        Assert.False(exchangeNameProperty.IsNullable, "ExchangeName should be required");

        Assert.NotNull(countryProperty);
        Assert.False(countryProperty.IsNullable, "Country should be required");
    }

    [Fact]
    [Trait("AC", "1.4")]
    public void MicExchangeEntity_HasSecuritiesCollection()
    {
        // Arrange
        var property = typeof(MicExchangeEntity).GetProperty(
            nameof(MicExchangeEntity.Securities),
            BindingFlags.Public | BindingFlags.Instance);

        // Act & Assert
        Assert.NotNull(property);
        Assert.True(typeof(ICollection<SecurityMasterEntity>).IsAssignableFrom(property.PropertyType),
            "Securities should be ICollection<SecurityMasterEntity>");
    }

    [Fact]
    [Trait("AC", "1.4")]
    public void MicExchangeEntity_IsActivePropertyHasDefaultValue()
    {
        // Arrange
        using var context = new StockAnalyzerDbContext(CreateInMemoryOptions());
        var entity = context.Model.FindEntityType(typeof(MicExchangeEntity));

        // Act
        var isActiveProperty = entity!.FindProperty(nameof(MicExchangeEntity.IsActive));

        // Assert
        Assert.NotNull(isActiveProperty);
        Assert.False(isActiveProperty.IsNullable, "IsActive should be non-nullable");
        Assert.True(isActiveProperty.GetDefaultValue() is true, "IsActive should default to true");
    }

    #endregion

    #region AC2.2: SecurityMasterEntity MicCode Property Tests

    [Fact]
    [Trait("AC", "2.2")]
    public void SecurityMasterEntity_HasMicCodeProperty()
    {
        // Arrange
        var property = typeof(SecurityMasterEntity).GetProperty(
            nameof(SecurityMasterEntity.MicCode),
            BindingFlags.Public | BindingFlags.Instance);

        // Act & Assert
        Assert.NotNull(property);
        Assert.True(property.PropertyType == typeof(string), "MicCode should be of type string");
    }

    [Fact]
    [Trait("AC", "2.2")]
    public void SecurityMasterEntity_MicCodePropertyIsNullable()
    {
        // Arrange
        using var context = new StockAnalyzerDbContext(CreateInMemoryOptions());
        var entity = context.Model.FindEntityType(typeof(SecurityMasterEntity));

        // Act
        var micCodeProperty = entity!.FindProperty(nameof(SecurityMasterEntity.MicCode));

        // Assert
        Assert.NotNull(micCodeProperty);
        Assert.True(micCodeProperty.IsNullable, "MicCode should be nullable (for backfill)");
    }

    #endregion

    #region AC2.4: SecurityMasterEntity MicExchange Navigation Tests

    [Fact]
    [Trait("AC", "2.4")]
    public void SecurityMasterEntity_HasMicExchangeNavigation()
    {
        // Arrange
        var property = typeof(SecurityMasterEntity).GetProperty(
            nameof(SecurityMasterEntity.MicExchange),
            BindingFlags.Public | BindingFlags.Instance);

        // Act & Assert
        Assert.NotNull(property);
        Assert.True(property.PropertyType == typeof(MicExchangeEntity),
            "MicExchange should be of type MicExchangeEntity");
    }

    [Fact]
    [Trait("AC", "2.4")]
    public void SecurityMasterEntity_NoExchangeProperty()
    {
        // Arrange
        var property = typeof(SecurityMasterEntity).GetProperty(
            "Exchange",
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

        // Act & Assert
        Assert.Null(property);
    }

    [Fact]
    [Trait("AC", "2.4")]
    public void SecurityMasterEntity_MicExchangeNavigationIsNullable()
    {
        // Arrange
        using var context = new StockAnalyzerDbContext(CreateInMemoryOptions());
        var entity = context.Model.FindEntityType(typeof(SecurityMasterEntity));

        // Act
        var navigationProperty = entity!.FindNavigation(nameof(SecurityMasterEntity.MicExchange));

        // Assert
        Assert.NotNull(navigationProperty);
        // The navigation is based on MicCode which is nullable, so the navigation can be null
        var foreignKey = navigationProperty.ForeignKey;
        Assert.True(foreignKey.Properties[0].IsNullable,
            "MicExchange navigation should be nullable (based on MicCode FK)");
    }

    [Fact]
    [Trait("AC", "2.4")]
    public void MicExchangeEntity_SecuritysNavigationIsOneToMany()
    {
        // Arrange
        using var context = new StockAnalyzerDbContext(CreateInMemoryOptions());
        var micEntity = context.Model.FindEntityType(typeof(MicExchangeEntity));
        var secEntity = context.Model.FindEntityType(typeof(SecurityMasterEntity));

        // Act
        var micNavigation = micEntity!.FindNavigation(nameof(MicExchangeEntity.Securities));
        var secNavigation = secEntity!.FindNavigation(nameof(SecurityMasterEntity.MicExchange));

        // Assert
        Assert.NotNull(micNavigation);
        Assert.NotNull(secNavigation);
        Assert.True(micNavigation.IsCollection, "Securities should be a collection (one-to-many)");
        Assert.False(secNavigation.IsCollection, "MicExchange should not be a collection (many-to-one)");
    }

    #endregion

    #region Integration Tests: Full Relationship Setup

    [Fact]
    [Trait("Category", "Integration")]
    public void MicExchange_CanBeInsertedAndRetrieved()
    {
        // Arrange
        var options = CreateInMemoryOptions();
        var mic = new MicExchangeEntity
        {
            MicCode = "XNYS",
            ExchangeName = "New York Stock Exchange",
            Country = "US",
            IsActive = true
        };

        // Act & Assert
        using (var context = new StockAnalyzerDbContext(options))
        {
            context.MicExchange.Add(mic);
            context.SaveChanges();
        }

        using (var context = new StockAnalyzerDbContext(options))
        {
            var retrieved = context.MicExchange.FirstOrDefault(m => m.MicCode == "XNYS");
            Assert.NotNull(retrieved);
            Assert.Equal("New York Stock Exchange", retrieved.ExchangeName);
            Assert.Equal("US", retrieved.Country);
            Assert.True(retrieved.IsActive);
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void SecurityMaster_CanReferenceMicExchange()
    {
        // Arrange
        var options = CreateInMemoryOptions();
        var mic = new MicExchangeEntity
        {
            MicCode = "XNAS",
            ExchangeName = "NASDAQ",
            Country = "US",
            IsActive = true
        };
        var security = new SecurityMasterEntity
        {
            TickerSymbol = "AAPL",
            IssueName = "Apple Inc.",
            MicCode = "XNAS",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        // Act & Assert
        using (var context = new StockAnalyzerDbContext(options))
        {
            context.MicExchange.Add(mic);
            context.SecurityMaster.Add(security);
            context.SaveChanges();
        }

        using (var context = new StockAnalyzerDbContext(options))
        {
            var retrieved = context.SecurityMaster
                .Include(s => s.MicExchange)
                .FirstOrDefault(s => s.TickerSymbol == "AAPL");

            Assert.NotNull(retrieved);
            Assert.Equal("XNAS", retrieved.MicCode);
            Assert.NotNull(retrieved.MicExchange);
            Assert.Equal("NASDAQ", retrieved.MicExchange.ExchangeName);
        }
    }

    #endregion
}
