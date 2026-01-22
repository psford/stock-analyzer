using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using StockAnalyzer.Core.Data;
using StockAnalyzer.Core.Data.Entities;
using StockAnalyzer.Core.Services;
using Xunit;

namespace StockAnalyzer.Core.Tests.Services;

public class SqlSymbolRepositoryTests
{
    private static StockAnalyzerDbContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<StockAnalyzerDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new StockAnalyzerDbContext(options);
    }

    private static SqlSymbolRepository CreateRepository(StockAnalyzerDbContext context)
    {
        var logger = new Mock<ILogger<SqlSymbolRepository>>().Object;
        var cacheLogger = new Mock<ILogger<SymbolCache>>().Object;
        var cache = new SymbolCache(cacheLogger);
        return new SqlSymbolRepository(context, logger, cache);
    }

    private static async Task SeedSymbols(StockAnalyzerDbContext context)
    {
        context.Symbols.AddRange(
            new SymbolEntity
            {
                Symbol = "AAPL",
                DisplaySymbol = "AAPL",
                Description = "Apple Inc",
                Type = "Common Stock",
                Exchange = "US",
                Currency = "USD",
                Country = "US",
                IsActive = true,
                LastUpdated = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            },
            new SymbolEntity
            {
                Symbol = "AAPD",
                DisplaySymbol = "AAPD",
                Description = "Direxion Daily AAPL Bear 1X Shares",
                Type = "ETF",
                Exchange = "US",
                Currency = "USD",
                Country = "US",
                IsActive = true,
                LastUpdated = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            },
            new SymbolEntity
            {
                Symbol = "MSFT",
                DisplaySymbol = "MSFT",
                Description = "Microsoft Corporation",
                Type = "Common Stock",
                Exchange = "US",
                Currency = "USD",
                Country = "US",
                IsActive = true,
                LastUpdated = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            },
            new SymbolEntity
            {
                Symbol = "GOOG",
                DisplaySymbol = "GOOG",
                Description = "Alphabet Inc Class C",
                Type = "Common Stock",
                Exchange = "US",
                Currency = "USD",
                Country = "US",
                IsActive = true,
                LastUpdated = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            },
            new SymbolEntity
            {
                Symbol = "AMZN",
                DisplaySymbol = "AMZN",
                Description = "Amazon.com Inc",
                Type = "Common Stock",
                Exchange = "US",
                Currency = "USD",
                Country = "US",
                IsActive = false, // Inactive for testing
                LastUpdated = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            }
        );
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task SearchAsync_ExactMatch_ReturnsFirst()
    {
        // Arrange
        using var context = CreateInMemoryContext();
        await SeedSymbols(context);
        var repo = CreateRepository(context);

        // Act
        var results = await repo.SearchAsync("AAPL");

        // Assert
        Assert.NotEmpty(results);
        Assert.Equal("AAPL", results[0].Symbol);
        Assert.Equal("Apple Inc", results[0].ShortName);
    }

    [Fact]
    public async Task SearchAsync_PrefixMatch_ReturnsMultiple()
    {
        // Arrange
        using var context = CreateInMemoryContext();
        await SeedSymbols(context);
        var repo = CreateRepository(context);

        // Act
        var results = await repo.SearchAsync("AAP");

        // Assert
        Assert.Equal(2, results.Count);
        // AAPL should come before AAPD (alphabetically when same rank)
        Assert.Equal("AAPD", results[0].Symbol);
        Assert.Equal("AAPL", results[1].Symbol);
    }

    [Fact]
    public async Task SearchAsync_DescriptionMatch_ReturnsResults()
    {
        // Arrange
        using var context = CreateInMemoryContext();
        await SeedSymbols(context);
        var repo = CreateRepository(context);

        // Act
        var results = await repo.SearchAsync("Microsoft");

        // Assert
        Assert.Single(results);
        Assert.Equal("MSFT", results[0].Symbol);
    }

    [Fact]
    public async Task SearchAsync_InactiveSymbols_ExcludedByDefault()
    {
        // Arrange
        using var context = CreateInMemoryContext();
        await SeedSymbols(context);
        var repo = CreateRepository(context);

        // Act
        var results = await repo.SearchAsync("Amazon");

        // Assert - AMZN is inactive, should not be found
        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchAsync_IncludeInactive_ReturnsInactiveSymbols()
    {
        // Arrange
        using var context = CreateInMemoryContext();
        await SeedSymbols(context);
        var repo = CreateRepository(context);

        // Act
        var results = await repo.SearchAsync("Amazon", includeInactive: true);

        // Assert - AMZN should be found when includeInactive is true
        Assert.Single(results);
        Assert.Equal("AMZN", results[0].Symbol);
    }

    [Fact]
    public async Task SearchAsync_EmptyQuery_ReturnsEmpty()
    {
        // Arrange
        using var context = CreateInMemoryContext();
        await SeedSymbols(context);
        var repo = CreateRepository(context);

        // Act
        var results = await repo.SearchAsync("");

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchAsync_WhitespaceQuery_ReturnsEmpty()
    {
        // Arrange
        using var context = CreateInMemoryContext();
        await SeedSymbols(context);
        var repo = CreateRepository(context);

        // Act
        var results = await repo.SearchAsync("   ");

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchAsync_LimitResults()
    {
        // Arrange
        using var context = CreateInMemoryContext();
        await SeedSymbols(context);
        var repo = CreateRepository(context);

        // Act
        var results = await repo.SearchAsync("A", limit: 2);

        // Assert
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task GetBySymbolAsync_ExactMatch_ReturnsSymbol()
    {
        // Arrange
        using var context = CreateInMemoryContext();
        await SeedSymbols(context);
        var repo = CreateRepository(context);

        // Act
        var result = await repo.GetBySymbolAsync("MSFT");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("MSFT", result.Symbol);
        Assert.Equal("Microsoft Corporation", result.ShortName);
    }

    [Fact]
    public async Task GetBySymbolAsync_CaseInsensitive()
    {
        // Arrange
        using var context = CreateInMemoryContext();
        await SeedSymbols(context);
        var repo = CreateRepository(context);

        // Act
        var result = await repo.GetBySymbolAsync("msft");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("MSFT", result.Symbol);
    }

    [Fact]
    public async Task GetBySymbolAsync_NotFound_ReturnsNull()
    {
        // Arrange
        using var context = CreateInMemoryContext();
        await SeedSymbols(context);
        var repo = CreateRepository(context);

        // Act
        var result = await repo.GetBySymbolAsync("NOTFOUND");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task ExistsAsync_ExistingSymbol_ReturnsTrue()
    {
        // Arrange
        using var context = CreateInMemoryContext();
        await SeedSymbols(context);
        var repo = CreateRepository(context);

        // Act
        var exists = await repo.ExistsAsync("AAPL");

        // Assert
        Assert.True(exists);
    }

    [Fact]
    public async Task ExistsAsync_NonExistingSymbol_ReturnsFalse()
    {
        // Arrange
        using var context = CreateInMemoryContext();
        await SeedSymbols(context);
        var repo = CreateRepository(context);

        // Act
        var exists = await repo.ExistsAsync("NOTFOUND");

        // Assert
        Assert.False(exists);
    }

    [Fact]
    public async Task GetActiveCountAsync_ReturnsActiveCount()
    {
        // Arrange
        using var context = CreateInMemoryContext();
        await SeedSymbols(context);
        var repo = CreateRepository(context);

        // Act
        var count = await repo.GetActiveCountAsync();

        // Assert - 4 active, 1 inactive (AMZN)
        Assert.Equal(4, count);
    }

    [Fact]
    public async Task UpsertManyAsync_InsertsNewSymbols()
    {
        // Arrange
        using var context = CreateInMemoryContext();
        var repo = CreateRepository(context);

        var symbols = new List<SymbolUpsertDto>
        {
            new() { Symbol = "TEST1", DisplaySymbol = "TEST1", Description = "Test One", Type = "Common Stock" },
            new() { Symbol = "TEST2", DisplaySymbol = "TEST2", Description = "Test Two", Type = "ETF" }
        };

        // Act
        var count = await repo.UpsertManyAsync(symbols);

        // Assert
        Assert.Equal(2, count);
        Assert.Equal(2, await context.Symbols.CountAsync());
    }

    [Fact]
    public async Task UpsertManyAsync_UpdatesExistingSymbols()
    {
        // Arrange
        using var context = CreateInMemoryContext();
        await SeedSymbols(context);
        var repo = CreateRepository(context);

        var symbols = new List<SymbolUpsertDto>
        {
            new() { Symbol = "AAPL", DisplaySymbol = "AAPL", Description = "Apple Inc - Updated", Type = "Common Stock" }
        };

        // Act
        var count = await repo.UpsertManyAsync(symbols);

        // Assert
        Assert.Equal(1, count);
        var updated = await context.Symbols.FirstOrDefaultAsync(s => s.Symbol == "AAPL");
        Assert.NotNull(updated);
        Assert.Equal("Apple Inc - Updated", updated.Description);
    }

    [Fact]
    public async Task MarkInactiveAsync_MarksSymbolsAsInactive()
    {
        // Arrange
        using var context = CreateInMemoryContext();
        await SeedSymbols(context);
        var repo = CreateRepository(context);

        // Only AAPL and MSFT are "active" in new list
        var activeSymbols = new[] { "AAPL", "MSFT" };

        // Act
        var inactiveCount = await repo.MarkInactiveAsync(activeSymbols);

        // Assert - AAPD and GOOG should be marked inactive (AMZN was already inactive)
        Assert.Equal(2, inactiveCount);

        var aapd = await context.Symbols.FirstOrDefaultAsync(s => s.Symbol == "AAPD");
        Assert.NotNull(aapd);
        Assert.False(aapd.IsActive);
    }

    [Fact]
    public async Task GetLastRefreshTimeAsync_ReturnsLatestTimestamp()
    {
        // Arrange
        using var context = CreateInMemoryContext();
        await SeedSymbols(context);
        var repo = CreateRepository(context);

        // Act
        var lastRefresh = await repo.GetLastRefreshTimeAsync();

        // Assert
        Assert.NotNull(lastRefresh);
        Assert.True(lastRefresh.Value <= DateTime.UtcNow);
    }
}
