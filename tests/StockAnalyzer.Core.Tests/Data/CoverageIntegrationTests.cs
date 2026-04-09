namespace StockAnalyzer.Core.Tests.Data;

using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using StockAnalyzer.Core.Data;
using StockAnalyzer.Core.Data.Entities;
using StockAnalyzer.Core.Services;
using StockAnalyzer.Core.Tests.TestHelpers;
using System.Data;
using Xunit;

/// <summary>
/// Integration tests for coverage MERGE behavior in SqlPriceRepository.
/// Tests against real SQL Express to verify both SecurityPriceCoverage
/// and SecurityPriceCoverageByYear update correctly.
/// Verifies AC1.2, AC1.3, and AC1.5.
/// </summary>
[Trait("Category", "Integration")]
public class CoverageIntegrationTests
{
    private const string ConnectionString =
        @"Server=.\SQLEXPRESS;Database=StockAnalyzer;Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True";

    [Fact]
    [Trait("Category", "Integration")]
    public async Task FirstPriceInsert_CreatesNewCoverageRow()
    {
        // Skip if SQL Express unavailable
        if (!IsSqlServerAvailable())
            return;

        int testSecurityAlias = 0;

        try
        {
            // Arrange: Insert security master via raw SQL to auto-generate alias
            await using (var context = CreateContext())
            {
                // Insert test security and capture auto-generated alias
                var connection = context.Database.GetDbConnection();
                var connectionWasOpen = connection.State == System.Data.ConnectionState.Open;
                if (!connectionWasOpen)
                    await connection.OpenAsync();

                try
                {
                    using var cmd = connection.CreateCommand();
                    cmd.CommandText = @"
                        INSERT INTO data.SecurityMaster (TickerSymbol, IssueName, Isin, CreatedAt, UpdatedAt)
                        OUTPUT inserted.SecurityAlias
                        VALUES ('TEST_' + CAST(ABS(CHECKSUM(NEWID())) AS NVARCHAR(10)), 'Test Company', 'US0000000001', GETUTCDATE(), GETUTCDATE())";
                    cmd.CommandTimeout = 30;

                    using var reader = await cmd.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                        testSecurityAlias = reader.GetInt32(0);
                }
                finally
                {
                    if (!connectionWasOpen && connection.State == System.Data.ConnectionState.Open)
                        await connection.CloseAsync();
                }

                // Seed business calendar for test date range
                SeedBusinessCalendar(context, new DateTime(2024, 1, 1), new DateTime(2024, 1, 31));
                await context.SaveChangesAsync();
            }

            // Act: Insert first batch of prices for this security
            var date1 = new DateTime(2024, 1, 10);
            var date2 = new DateTime(2024, 1, 20);

            await using (var context = CreateContext())
            {
                var logger = new NoopLogger<SqlPriceRepository>();
                var repo = new SqlPriceRepository(context, logger);

                var newPrices = new List<PriceCreateDto>
                {
                    CreatePrice(testSecurityAlias, date1),
                    CreatePrice(testSecurityAlias, date2)
                };

                await repo.BulkInsertAsync(newPrices);
            }

            // Assert: Coverage row created with correct values
            await using (var context = CreateContext())
            {
                var coverage = await context.Set<SecurityPriceCoverageEntity>()
                    .FirstOrDefaultAsync(c => c.SecurityAlias == testSecurityAlias);

                coverage.Should().NotBeNull();
                coverage!.PriceCount.Should().Be(2);
                coverage.FirstDate.Should().Be(date1.Date);
                coverage.LastDate.Should().Be(date2.Date);
                coverage.ExpectedCount.Should().BeGreaterThan(0);

                // Verify by-year coverage
                var byYearCoverage = await context.Set<SecurityPriceCoverageByYearEntity>()
                    .Where(c => c.SecurityAlias == testSecurityAlias)
                    .ToListAsync();

                byYearCoverage.Should().HaveCount(1);
                byYearCoverage[0].Year.Should().Be(2024);
                byYearCoverage[0].PriceCount.Should().Be(2);
            }
        }
        finally
        {
            // Cleanup
            if (testSecurityAlias > 0)
                await CleanupSecurityData(testSecurityAlias);
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task WidenFirstDate_WhenInsertingBeforeExistingRange()
    {
        // Skip if SQL Express unavailable
        if (!IsSqlServerAvailable())
            return;

        int testSecurityAlias = 0;

        try
        {
            // Arrange: Insert security via raw SQL with auto-generated alias
            var originalFirstDate = new DateTime(2024, 1, 15);
            var originalLastDate = new DateTime(2024, 1, 20);
            var newFirstDate = new DateTime(2024, 1, 1);

            await using (var context = CreateContext())
            {
                // Insert test security and capture auto-generated alias
                var connection = context.Database.GetDbConnection();
                var connectionWasOpen = connection.State == System.Data.ConnectionState.Open;
                if (!connectionWasOpen)
                    await connection.OpenAsync();

                try
                {
                    using var cmd = connection.CreateCommand();
                    cmd.CommandText = @"
                        INSERT INTO data.SecurityMaster (TickerSymbol, IssueName, Isin, CreatedAt, UpdatedAt)
                        OUTPUT inserted.SecurityAlias
                        VALUES ('WFST_' + CAST(ABS(CHECKSUM(NEWID())) AS NVARCHAR(10)), 'Test Widen First', 'US0000000002', GETUTCDATE(), GETUTCDATE())";
                    cmd.CommandTimeout = 30;

                    using var reader = await cmd.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                        testSecurityAlias = reader.GetInt32(0);
                }
                finally
                {
                    if (!connectionWasOpen && connection.State == System.Data.ConnectionState.Open)
                        await connection.CloseAsync();
                }

                // Seed calendar
                SeedBusinessCalendar(context, new DateTime(2024, 1, 1), new DateTime(2024, 1, 31));

                // Seed initial coverage
                var initialCoverage = new SecurityPriceCoverageEntity
                {
                    SecurityAlias = testSecurityAlias,
                    PriceCount = 5,
                    FirstDate = originalFirstDate,
                    LastDate = originalLastDate,
                    ExpectedCount = 4,
                    LastUpdatedAt = DateTime.UtcNow
                };
                context.Set<SecurityPriceCoverageEntity>().Add(initialCoverage);

                await context.SaveChangesAsync();
            }

            // Act: Insert prices before the original first date
            await using (var context = CreateContext())
            {
                var logger = new NoopLogger<SqlPriceRepository>();
                var repo = new SqlPriceRepository(context, logger);

                var newPrices = new List<PriceCreateDto>
                {
                    CreatePrice(testSecurityAlias, newFirstDate),
                    CreatePrice(testSecurityAlias, new DateTime(2024, 1, 5))
                };

                await repo.BulkInsertAsync(newPrices);
            }

            // Assert: FirstDate widened, LastDate unchanged
            await using (var context = CreateContext())
            {
                var coverage = await context.Set<SecurityPriceCoverageEntity>()
                    .FirstOrDefaultAsync(c => c.SecurityAlias == testSecurityAlias);

                coverage.Should().NotBeNull();
                coverage!.FirstDate.Should().Be(newFirstDate.Date);
                coverage.LastDate.Should().Be(originalLastDate.Date);
                coverage.PriceCount.Should().Be(7); // Original 5 + new 2
            }
        }
        finally
        {
            if (testSecurityAlias > 0)
                await CleanupSecurityData(testSecurityAlias);
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task WidenLastDate_WhenInsertingAfterExistingRange()
    {
        // Skip if SQL Express unavailable
        if (!IsSqlServerAvailable())
            return;

        int testSecurityAlias = 0;

        try
        {
            // Arrange: Insert security via raw SQL with auto-generated alias
            var originalFirstDate = new DateTime(2024, 1, 1);
            var originalLastDate = new DateTime(2024, 1, 10);
            var newLastDate = new DateTime(2024, 1, 31);

            await using (var context = CreateContext())
            {
                // Insert test security and capture auto-generated alias
                var connection = context.Database.GetDbConnection();
                var connectionWasOpen = connection.State == System.Data.ConnectionState.Open;
                if (!connectionWasOpen)
                    await connection.OpenAsync();

                try
                {
                    using var cmd = connection.CreateCommand();
                    cmd.CommandText = @"
                        INSERT INTO data.SecurityMaster (TickerSymbol, IssueName, Isin, CreatedAt, UpdatedAt)
                        OUTPUT inserted.SecurityAlias
                        VALUES ('WLST_' + CAST(ABS(CHECKSUM(NEWID())) AS NVARCHAR(10)), 'Test Widen Last', 'US0000000003', GETUTCDATE(), GETUTCDATE())";
                    cmd.CommandTimeout = 30;

                    using var reader = await cmd.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                        testSecurityAlias = reader.GetInt32(0);
                }
                finally
                {
                    if (!connectionWasOpen && connection.State == System.Data.ConnectionState.Open)
                        await connection.CloseAsync();
                }

                // Seed calendar
                SeedBusinessCalendar(context, new DateTime(2024, 1, 1), new DateTime(2024, 1, 31));

                // Seed initial coverage
                var initialCoverage = new SecurityPriceCoverageEntity
                {
                    SecurityAlias = testSecurityAlias,
                    PriceCount = 5,
                    FirstDate = originalFirstDate,
                    LastDate = originalLastDate,
                    ExpectedCount = 5,
                    LastUpdatedAt = DateTime.UtcNow
                };
                context.Set<SecurityPriceCoverageEntity>().Add(initialCoverage);

                await context.SaveChangesAsync();
            }

            // Act: Insert prices after the original last date
            await using (var context = CreateContext())
            {
                var logger = new NoopLogger<SqlPriceRepository>();
                var repo = new SqlPriceRepository(context, logger);

                var newPrices = new List<PriceCreateDto>
                {
                    CreatePrice(testSecurityAlias, new DateTime(2024, 1, 25)),
                    CreatePrice(testSecurityAlias, newLastDate)
                };

                await repo.BulkInsertAsync(newPrices);
            }

            // Assert: LastDate widened, FirstDate unchanged
            await using (var context = CreateContext())
            {
                var coverage = await context.Set<SecurityPriceCoverageEntity>()
                    .FirstOrDefaultAsync(c => c.SecurityAlias == testSecurityAlias);

                coverage.Should().NotBeNull();
                coverage!.FirstDate.Should().Be(originalFirstDate.Date);
                coverage.LastDate.Should().Be(newLastDate.Date);
                coverage.PriceCount.Should().Be(7); // Original 5 + new 2
            }
        }
        finally
        {
            if (testSecurityAlias > 0)
                await CleanupSecurityData(testSecurityAlias);
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExpectedCount_MatchesBusinessDayCountInRange()
    {
        // Skip if SQL Express unavailable
        if (!IsSqlServerAvailable())
            return;

        int testSecurityAlias = 0;

        try
        {
            // Arrange: Insert security via raw SQL with auto-generated alias
            var startDate = new DateTime(2024, 1, 1);
            var endDate = new DateTime(2024, 1, 31);

            await using (var context = CreateContext())
            {
                // Insert test security and capture auto-generated alias
                var connection = context.Database.GetDbConnection();
                var connectionWasOpen = connection.State == System.Data.ConnectionState.Open;
                if (!connectionWasOpen)
                    await connection.OpenAsync();

                try
                {
                    using var cmd = connection.CreateCommand();
                    cmd.CommandText = @"
                        INSERT INTO data.SecurityMaster (TickerSymbol, IssueName, Isin, CreatedAt, UpdatedAt)
                        OUTPUT inserted.SecurityAlias
                        VALUES ('BDAY_' + CAST(ABS(CHECKSUM(NEWID())) AS NVARCHAR(10)), 'Test Business Days', 'US0000000004', GETUTCDATE(), GETUTCDATE())";
                    cmd.CommandTimeout = 30;

                    using var reader = await cmd.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                        testSecurityAlias = reader.GetInt32(0);
                }
                finally
                {
                    if (!connectionWasOpen && connection.State == System.Data.ConnectionState.Open)
                        await connection.CloseAsync();
                }

                // Seed calendar with all days in January 2024
                // and count expected business days
                SeedBusinessCalendar(context, startDate, endDate);
                await context.SaveChangesAsync();
            }

            // Act: Insert prices and check ExpectedCount
            await using (var context = CreateContext())
            {
                var logger = new NoopLogger<SqlPriceRepository>();
                var repo = new SqlPriceRepository(context, logger);

                var newPrices = new List<PriceCreateDto>
                {
                    CreatePrice(testSecurityAlias, new DateTime(2024, 1, 1)),
                    CreatePrice(testSecurityAlias, new DateTime(2024, 1, 31))
                };

                await repo.BulkInsertAsync(newPrices);
            }

            // Assert: ExpectedCount matches business days in range
            await using (var context = CreateContext())
            {
                var coverage = await context.Set<SecurityPriceCoverageEntity>()
                    .FirstOrDefaultAsync(c => c.SecurityAlias == testSecurityAlias);

                coverage.Should().NotBeNull();
                coverage!.ExpectedCount.Should().BeGreaterThan(0);

                // Verify by querying BusinessCalendar directly
                var businessDayCount = await context.BusinessCalendar
                    .Where(bc => bc.SourceId == 1
                        && bc.IsBusinessDay
                        && bc.EffectiveDate >= coverage.FirstDate!.Value
                        && bc.EffectiveDate <= coverage.LastDate!.Value)
                    .CountAsync();

                coverage.ExpectedCount.Should().Be(businessDayCount);
            }
        }
        finally
        {
            if (testSecurityAlias > 0)
                await CleanupSecurityData(testSecurityAlias);
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ByYearCoverage_IncrementedCorrectly_WhenInsertingMultipleYears()
    {
        // Skip if SQL Express unavailable
        if (!IsSqlServerAvailable())
            return;

        int testSecurityAlias = 0;

        try
        {
            // Arrange: Insert security via raw SQL with auto-generated alias
            await using (var context = CreateContext())
            {
                // Insert test security and capture auto-generated alias
                var connection = context.Database.GetDbConnection();
                var connectionWasOpen = connection.State == System.Data.ConnectionState.Open;
                if (!connectionWasOpen)
                    await connection.OpenAsync();

                try
                {
                    using var cmd = connection.CreateCommand();
                    cmd.CommandText = @"
                        INSERT INTO data.SecurityMaster (TickerSymbol, IssueName, Isin, CreatedAt, UpdatedAt)
                        OUTPUT inserted.SecurityAlias
                        VALUES ('MYER_' + CAST(ABS(CHECKSUM(NEWID())) AS NVARCHAR(10)), 'Test Multi Year', 'US0000000005', GETUTCDATE(), GETUTCDATE())";
                    cmd.CommandTimeout = 30;

                    using var reader = await cmd.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                        testSecurityAlias = reader.GetInt32(0);
                }
                finally
                {
                    if (!connectionWasOpen && connection.State == System.Data.ConnectionState.Open)
                        await connection.CloseAsync();
                }

                // Seed calendar for 2 years
                SeedBusinessCalendar(context, new DateTime(2023, 12, 1), new DateTime(2025, 1, 31));

                // Pre-seed coverage for one year
                var existingCoverage = new SecurityPriceCoverageByYearEntity
                {
                    SecurityAlias = testSecurityAlias,
                    Year = 2024,
                    PriceCount = 10,
                    LastUpdatedAt = DateTime.UtcNow
                };
                context.Set<SecurityPriceCoverageByYearEntity>().Add(existingCoverage);

                await context.SaveChangesAsync();
            }

            // Act: Insert prices spanning 2024 and 2025
            await using (var context = CreateContext())
            {
                var logger = new NoopLogger<SqlPriceRepository>();
                var repo = new SqlPriceRepository(context, logger);

                var newPrices = new List<PriceCreateDto>
                {
                    CreatePrice(testSecurityAlias, new DateTime(2024, 12, 25)),
                    CreatePrice(testSecurityAlias, new DateTime(2024, 12, 30)),
                    CreatePrice(testSecurityAlias, new DateTime(2025, 1, 2)),
                    CreatePrice(testSecurityAlias, new DateTime(2025, 1, 15))
                };

                await repo.BulkInsertAsync(newPrices);
            }

            // Assert: Both years updated
            await using (var context = CreateContext())
            {
                var byYear = await context.Set<SecurityPriceCoverageByYearEntity>()
                    .Where(c => c.SecurityAlias == testSecurityAlias)
                    .OrderBy(c => c.Year)
                    .ToListAsync();

                byYear.Should().HaveCount(2);

                var coverage2024 = byYear.First(c => c.Year == 2024);
                coverage2024.PriceCount.Should().Be(12); // Original 10 + 2 new in 2024

                var coverage2025 = byYear.First(c => c.Year == 2025);
                coverage2025.PriceCount.Should().Be(2); // 2 new in 2025
            }
        }
        finally
        {
            if (testSecurityAlias > 0)
                await CleanupSecurityData(testSecurityAlias);
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ForwardFillHolidaysAsync_WithDefaultMaxFillDate_DoesNotFillFutureDates()
    {
        // Skip if SQL Express unavailable
        if (!IsSqlServerAvailable())
            return;

        int testSecurityAlias = 0;

        try
        {
            // Arrange: Create security and seed calendar for a range including near-future dates
            var startDate = new DateTime(2024, 1, 1);
            var endDate = DateTime.UtcNow.Date.AddDays(10);  // Include some future dates
            var today = DateTime.UtcNow.Date;

            await using (var context = CreateContext())
            {
                // Insert test security via raw SQL with auto-generated alias
                var connection = context.Database.GetDbConnection();
                var connectionWasOpen = connection.State == System.Data.ConnectionState.Open;
                if (!connectionWasOpen)
                    await connection.OpenAsync();

                try
                {
                    using var cmd = connection.CreateCommand();
                    cmd.CommandText = @"
                        INSERT INTO data.SecurityMaster (TickerSymbol, IssueName, Isin, CreatedAt, UpdatedAt)
                        OUTPUT inserted.SecurityAlias
                        VALUES ('FFILL_' + CAST(ABS(CHECKSUM(NEWID())) AS NVARCHAR(10)), 'Forward Fill Test', 'US0000000010', GETUTCDATE(), GETUTCDATE())";
                    cmd.CommandTimeout = 30;

                    using var reader = await cmd.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                        testSecurityAlias = reader.GetInt32(0);
                }
                finally
                {
                    if (!connectionWasOpen && connection.State == System.Data.ConnectionState.Open)
                        await connection.CloseAsync();
                }

                // Seed calendar with dates including weekends and future dates
                SeedBusinessCalendar(context, startDate, endDate);
                await context.SaveChangesAsync();

                // Insert prices for all business days up to today via BulkInsertAsync
                // (which filters out future dates)
                var logger = new NoopLogger<SqlPriceRepository>();
                var repo = new SqlPriceRepository(context, logger);

                var pricesUpToday = new List<PriceCreateDto>();
                for (var date = startDate.Date; date <= today; date = date.AddDays(1))
                {
                    // Only insert for business days
                    if (date.DayOfWeek != DayOfWeek.Saturday && date.DayOfWeek != DayOfWeek.Sunday)
                    {
                        pricesUpToday.Add(CreatePrice(testSecurityAlias, date));
                    }
                }

                await repo.BulkInsertAsync(pricesUpToday);

                // Insert future business day prices directly via context (bypassing BulkInsertAsync filtering)
                // to test that ForwardFillHolidaysAsync respects maxFillDate and doesn't fill them
                var futurePrices = new List<PriceEntity>();
                for (var date = today.AddDays(1); date <= endDate.Date; date = date.AddDays(1))
                {
                    // Only insert for business days
                    if (date.DayOfWeek != DayOfWeek.Saturday && date.DayOfWeek != DayOfWeek.Sunday)
                    {
                        futurePrices.Add(new PriceEntity
                        {
                            SecurityAlias = testSecurityAlias,
                            EffectiveDate = date,
                            Open = 100m,
                            High = 105m,
                            Low = 99m,
                            Close = 102m,
                            AdjustedClose = null,
                            Volume = null,
                            Volatility = null
                        });
                    }
                }

                context.Prices.AddRange(futurePrices);
                await context.SaveChangesAsync();
            }

            // Act: Call ForwardFillHolidaysAsync without specifying maxFillDate (should default to today)
            await using (var context = CreateContext())
            {
                var logger = new NoopLogger<SqlPriceRepository>();
                var repo = new SqlPriceRepository(context, logger);

                var result = await repo.ForwardFillHolidaysAsync();

                // Assert: Should succeed
                result.Success.Should().BeTrue();

                // Verify no prices exist for future dates
                var futureRecords = await context.Prices
                    .Where(p => p.SecurityAlias == testSecurityAlias && p.EffectiveDate > today)
                    .CountAsync();

                futureRecords.Should().Be(0, "ForwardFillHolidaysAsync with default maxFillDate should not fill beyond today");
            }
        }
        finally
        {
            if (testSecurityAlias > 0)
                await CleanupSecurityData(testSecurityAlias);
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ForwardFillHolidaysAsync_WithExplicitMaxFillDateInPast_RespectsCap()
    {
        // Skip if SQL Express unavailable
        if (!IsSqlServerAvailable())
            return;

        int testSecurityAlias = 0;

        try
        {
            // Arrange: Create security and seed calendar for a month range
            var startDate = new DateTime(2024, 1, 1);
            var endDate = new DateTime(2024, 1, 31);
            var maxFillDate = new DateTime(2024, 1, 15);  // Cap fill to middle of month

            await using (var context = CreateContext())
            {
                // Insert test security via raw SQL with auto-generated alias
                var connection = context.Database.GetDbConnection();
                var connectionWasOpen = connection.State == System.Data.ConnectionState.Open;
                if (!connectionWasOpen)
                    await connection.OpenAsync();

                try
                {
                    using var cmd = connection.CreateCommand();
                    cmd.CommandText = @"
                        INSERT INTO data.SecurityMaster (TickerSymbol, IssueName, Isin, CreatedAt, UpdatedAt)
                        OUTPUT inserted.SecurityAlias
                        VALUES ('FFCAP_' + CAST(ABS(CHECKSUM(NEWID())) AS NVARCHAR(10)), 'Forward Fill Cap Test', 'US0000000011', GETUTCDATE(), GETUTCDATE())";
                    cmd.CommandTimeout = 30;

                    using var reader = await cmd.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                        testSecurityAlias = reader.GetInt32(0);
                }
                finally
                {
                    if (!connectionWasOpen && connection.State == System.Data.ConnectionState.Open)
                        await connection.CloseAsync();
                }

                // Seed calendar for full month
                SeedBusinessCalendar(context, startDate, endDate);
                await context.SaveChangesAsync();

                // Insert prices for all business days in the month
                var logger = new NoopLogger<SqlPriceRepository>();
                var repo = new SqlPriceRepository(context, logger);

                var prices = new List<PriceCreateDto>();
                for (var date = startDate.Date; date <= endDate.Date; date = date.AddDays(1))
                {
                    // Only insert for business days
                    if (date.DayOfWeek != DayOfWeek.Saturday && date.DayOfWeek != DayOfWeek.Sunday)
                    {
                        prices.Add(CreatePrice(testSecurityAlias, date));
                    }
                }

                await repo.BulkInsertAsync(prices);
            }

            // Act: Call ForwardFillHolidaysAsync with explicit maxFillDate in the past
            await using (var context = CreateContext())
            {
                var logger = new NoopLogger<SqlPriceRepository>();
                var repo = new SqlPriceRepository(context, logger);

                var result = await repo.ForwardFillHolidaysAsync(limit: null, maxFillDate: maxFillDate);

                // Assert: Should succeed
                result.Success.Should().BeTrue();

                // Verify no forward-filled prices exist beyond maxFillDate
                var beyondCapRecords = await context.Prices
                    .Where(p => p.SecurityAlias == testSecurityAlias && p.EffectiveDate > maxFillDate && p.Volume == 0)
                    .CountAsync();

                beyondCapRecords.Should().Be(0, "ForwardFillHolidaysAsync should not create records beyond maxFillDate");
            }
        }
        finally
        {
            if (testSecurityAlias > 0)
                await CleanupSecurityData(testSecurityAlias);
        }
    }

    private static bool IsSqlServerAvailable()
    {
        try
        {
            using var connection = new SqlConnection(ConnectionString);
            connection.Open();
            return true;
        }
        catch (SqlException)
        {
            return false;
        }
    }

    private static StockAnalyzerDbContext CreateContext()
    {
        var optionsBuilder = new DbContextOptionsBuilder<StockAnalyzerDbContext>();
        optionsBuilder.UseSqlServer(ConnectionString);
        return new StockAnalyzerDbContext(optionsBuilder.Options);
    }

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

    private static void SeedBusinessCalendar(StockAnalyzerDbContext context, DateTime startDate, DateTime endDate)
    {
        var entries = new List<BusinessCalendarEntity>();
        for (var date = startDate.Date; date <= endDate.Date; date = date.AddDays(1))
        {
            // Mark weekdays (Mon-Fri) as business days, weekends as non-business days
            var isBusinessDay = date.DayOfWeek != DayOfWeek.Saturday && date.DayOfWeek != DayOfWeek.Sunday;
            var isHoliday = false; // Simplified: no actual holidays in test

            var entry = new BusinessCalendarEntity
            {
                SourceId = 1,
                EffectiveDate = date,
                IsBusinessDay = isBusinessDay,
                IsHoliday = isHoliday,
                IsMonthEnd = date.Day == DateTime.DaysInMonth(date.Year, date.Month),
                IsLastBusinessDayMonthEnd = false // Simplified
            };

            entries.Add(entry);
        }

        // Load existing dates in a single query to avoid N+1 problem
        var existingDates = context.BusinessCalendar
            .Where(bc => bc.SourceId == 1 && bc.EffectiveDate >= startDate.Date && bc.EffectiveDate <= endDate.Date)
            .Select(bc => bc.EffectiveDate)
            .ToHashSet();

        // Only add if not already exists
        foreach (var entry in entries)
        {
            if (!existingDates.Contains(entry.EffectiveDate))
            {
                context.BusinessCalendar.Add(entry);
            }
        }
    }

    private static async Task CleanupSecurityData(int securityAlias)
    {
        await using var context = CreateContext();

        // Delete in correct order (foreign keys)
        await context.Database.ExecuteSqlInterpolatedAsync($@"
            DELETE FROM data.SecurityPriceCoverageByYear WHERE SecurityAlias = {securityAlias};
            DELETE FROM data.SecurityPriceCoverage WHERE SecurityAlias = {securityAlias};
            DELETE FROM data.Prices WHERE SecurityAlias = {securityAlias};
            DELETE FROM data.SecurityMaster WHERE SecurityAlias = {securityAlias};
        ");
    }
}
