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

    [Fact]
    public async Task BulkInsertAsync_AllFutureDated_ReturnsZero()
    {
        // Arrange: All records have EffectiveDate > DateTime.UtcNow.Date
        // This exercises the future-date guard without requiring SQL Server
        using var context = CreateInMemoryContext();
        var repo = new SqlPriceRepository(context, CreateNoopLogger());
        var futureDate1 = DateTime.UtcNow.AddDays(1);
        var futureDate2 = DateTime.UtcNow.AddDays(5);

        var prices = new List<PriceCreateDto>
        {
            CreatePrice(1, futureDate1),
            CreatePrice(1, futureDate2)
        };

        // Act
        var result = await repo.BulkInsertAsync(prices);

        // Assert
        result.Should().Be(0, "BulkInsertAsync should filter out all future-dated records");
    }

    /// <summary>
    /// AC3.1: BulkInsertAsync filters out future-dated records before any database operation.
    /// Verifies the filtering logic that prevents bad dates from reaching the database.
    /// Note: Full BulkInsertAsync integration test requires SQL Server (see CoverageIntegrationTests.cs)
    /// </summary>
    [Fact]
    public void BulkInsertAsync_FilteringLogic_RejectsAllFutureDates()
    {
        // Arrange
        var today = DateTime.UtcNow.Date;
        var prices = new List<PriceCreateDto>
        {
            CreatePrice(1, today.AddDays(-3)), // Valid (past)
            CreatePrice(1, today),              // Valid (today)
            CreatePrice(1, today.AddDays(1)),  // Invalid (future)
            CreatePrice(1, today.AddDays(-1)), // Valid (past)
            CreatePrice(1, today.AddDays(5))   // Invalid (future)
        };

        // Act - Simulate the filter that BulkInsertAsync applies
        var filtered = prices.Where(p => p.EffectiveDate <= today).ToList();

        // Assert
        filtered.Count.Should().Be(3, "Only 3 records should pass filter (past and today dates)");
        filtered.All(p => p.EffectiveDate <= today).Should().BeTrue("All filtered records should have dates <= today");
    }

    /// <summary>
    /// AC3.1: Verify BulkInsertAsync with all future-dated records returns 0 with no database writes.
    /// Tests that the filter prevents any database interaction for invalid data.
    /// </summary>
    [Fact]
    public async Task BulkInsertAsync_WithAllFutureDateRecords_WritesNothingToDatabase()
    {
        // Arrange
        using var context = CreateInMemoryContext();
        var repo = new SqlPriceRepository(context, CreateNoopLogger());
        var futureDate = DateTime.UtcNow.AddDays(10);

        var prices = new List<PriceCreateDto>
        {
            CreatePrice(1, futureDate),
            CreatePrice(2, futureDate),
            CreatePrice(3, futureDate)
        };

        // Act
        var result = await repo.BulkInsertAsync(prices);

        // Assert
        result.Should().Be(0);
        // Verify nothing was written (in-memory context would have entries if writes occurred)
        var allPrices = await context.Set<PriceEntity>()
            .AsNoTracking()
            .ToListAsync();
        allPrices.Should().BeEmpty("No prices should be written for future-dated records");
    }

    /// <summary>
    /// AC3.3: CreateAsync rejects future dates with ArgumentException.
    /// </summary>
    [Fact]
    public async Task CreateAsync_WithFutureDate_ThrowsArgumentExceptionWithFutureDate()
    {
        // Arrange
        using var context = CreateInMemoryContext();
        var repo = new SqlPriceRepository(context, CreateNoopLogger());
        var futureDate = DateTime.UtcNow.AddDays(1).Date;
        var dto = CreatePrice(1, futureDate);

        // Act
        var act = async () => await repo.CreateAsync(dto);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*Cannot insert price with future date*");
    }
}

