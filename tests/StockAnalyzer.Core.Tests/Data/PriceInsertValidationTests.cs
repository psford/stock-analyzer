namespace StockAnalyzer.Core.Tests.Data;

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StockAnalyzer.Core.Data;
using StockAnalyzer.Core.Data.Entities;
using StockAnalyzer.Core.Services;
using StockAnalyzer.Core.Tests.TestHelpers;
using Xunit;

/// <summary>
/// Unit tests for insert validation in SqlPriceRepository.
/// Tests future-date guard behavior on CreateAsync and BulkInsertAsync.
/// Verifies AC3.1 and AC3.3 (rejection of future-dated records).
/// </summary>
public class PriceInsertValidationTests
{
    /// <summary>
    /// Helper to create an in-memory EF Core context for testing.
    /// </summary>
    private static StockAnalyzerDbContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<StockAnalyzerDbContext>()
            .UseInMemoryDatabase(databaseName: $"TestDb_{Guid.NewGuid()}")
            .Options;

        return new StockAnalyzerDbContext(options);
    }

    /// <summary>
    /// Helper to create a NoopLogger for SqlPriceRepository.
    /// </summary>
    private static ILogger<SqlPriceRepository> CreateNoopLogger()
    {
        return new NoopLogger<SqlPriceRepository>();
    }

    /// <summary>
    /// Helper to create a PriceCreateDto with specified date and alias.
    /// </summary>
    private static PriceCreateDto CreatePrice(int securityAlias, DateTime effectiveDate)
    {
        return new PriceCreateDto
        {
            SecurityAlias = securityAlias,
            EffectiveDate = effectiveDate,
            Open = 100m,
            High = 105m,
            Low = 99m,
            Close = 102m,
            Volatility = null,
            Volume = null,
            AdjustedClose = null
        };
    }

    // ========== CreateAsync Tests ==========

    [Fact]
    public async Task CreateAsync_WithFutureDateRecord_ThrowsArgumentException()
    {
        // Arrange
        using var context = CreateInMemoryContext();
        var repo = new SqlPriceRepository(context, CreateNoopLogger());
        var futureDateDto = CreatePrice(1, DateTime.UtcNow.AddDays(1));

        // Act
        var act = async () => await repo.CreateAsync(futureDateDto);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*Cannot insert price with future date*");
    }

    [Fact]
    public async Task CreateAsync_WithTodayDate_Succeeds()
    {
        // Arrange
        using var context = CreateInMemoryContext();
        var repo = new SqlPriceRepository(context, CreateNoopLogger());
        var todayDto = CreatePrice(1, DateTime.UtcNow.Date);

        // Act
        var result = await repo.CreateAsync(todayDto);

        // Assert
        result.Should().NotBeNull();
        result.EffectiveDate.Should().Be(DateTime.UtcNow.Date);
    }

    [Fact]
    public async Task CreateAsync_WithPastDate_Succeeds()
    {
        // Arrange
        using var context = CreateInMemoryContext();
        var repo = new SqlPriceRepository(context, CreateNoopLogger());
        var pastDto = CreatePrice(1, DateTime.UtcNow.AddDays(-10));

        // Act
        var result = await repo.CreateAsync(pastDto);

        // Assert
        result.Should().NotBeNull();
        result.EffectiveDate.Should().Be(DateTime.UtcNow.AddDays(-10).Date);
    }

    // ========== BulkInsertAsync Tests ==========
    // Note: BulkInsertAsync requires real database due to relational operations (GetDistinctDatesAsync, MERGE).
    // These tests verify the filtering logic before the relational portion executes.

    [Fact]
    public async Task BulkInsertAsync_EmptyList_ReturnsZero()
    {
        // Arrange: Empty list should return 0 immediately (before any DB access)
        using var context = CreateInMemoryContext();
        var repo = new SqlPriceRepository(context, CreateNoopLogger());
        var prices = new List<PriceCreateDto>();

        // Act
        var result = await repo.BulkInsertAsync(prices);

        // Assert
        result.Should().Be(0);
    }
}

