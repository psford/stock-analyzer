namespace EodhdLoader.Tests.Services;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using EodhdLoader.Services;
using StockAnalyzer.Core.Data;
using StockAnalyzer.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;
using EodhdLoader.Models;

/// <summary>
/// Tests for ISharesConstituentService.GetStaleEtfsAsync method.
/// Verifies per-ETF staleness detection against latest month-end business day.
/// Covers AC5.1 (stale data detected) and AC5.2 (current data skipped).
/// </summary>
public class ISharesConstituentServiceStalenessTests
{
    /// <summary>
    /// Helper: Creates an in-memory DbContext with minimal ETF configuration.
    /// </summary>
    private static StockAnalyzerDbContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<StockAnalyzerDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new StockAnalyzerDbContext(options);
    }

    /// <summary>
    /// Helper: Creates a mock HttpClient.
    /// </summary>
    private static HttpClient CreateMockHttpClient()
    {
        return new HttpClient();
    }

    /// <summary>
    /// Helper: Calculates the last business day of the current/previous month.
    /// Mirrors GetLastMonthEnd() logic from phase spec.
    /// </summary>
    private static DateTime GetLastMonthEnd()
    {
        var today = DateTime.UtcNow.Date;
        var firstOfMonth = new DateTime(today.Year, today.Month, 1);
        var lastDayOfMonth = firstOfMonth.AddDays(-1);

        // Adjust to last business day
        while (lastDayOfMonth.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            lastDayOfMonth = lastDayOfMonth.AddDays(-1);
        }

        return lastDayOfMonth;
    }

    /// <summary>
    /// AC5.1: Detects stale data — ETF with max EffectiveDate two months ago should be returned as stale.
    /// </summary>
    [Fact]
    public async Task GetStaleEtfsAsync_WithOldConstituents_ReturnsStalEtf()
    {
        // Arrange
        var dbContext = CreateInMemoryContext();
        var httpClient = CreateMockHttpClient();

        // Pre-seed IndexDefinition for SP500
        var indexDef = new IndexDefinitionEntity
        {
            IndexId = 1,
            IndexCode = "SP500",
            IndexName = "S&P 500",
            ProxyEtfTicker = "IVV"
        };
        dbContext.IndexDefinitions.Add(indexDef);

        // Pre-seed IndexConstituent with max EffectiveDate = two months ago (stale)
        var twoMonthsAgo = DateTime.UtcNow.Date.AddMonths(-2);
        var constituent = new IndexConstituentEntity
        {
            IndexId = 1,
            SecurityAlias = 1,
            EffectiveDate = twoMonthsAgo,
            Weight = 0.01m,
            SourceId = 10 // ISharesSourceId
        };
        dbContext.IndexConstituents.Add(constituent);
        await dbContext.SaveChangesAsync();

        var service = new ISharesConstituentService(httpClient, dbContext);

        // Act
        var staleEtfs = await service.GetStaleEtfsAsync();

        // Assert
        Assert.NotEmpty(staleEtfs);
        Assert.Single(staleEtfs);
        Assert.Equal("IVV", staleEtfs[0].EtfTicker);
        Assert.Equal("SP500", staleEtfs[0].IndexCode);
    }

    /// <summary>
    /// AC5.2: Up-to-date data is skipped — ETF with EffectiveDate at last month-end should NOT be returned.
    /// </summary>
    [Fact]
    public async Task GetStaleEtfsAsync_WithCurrentConstituents_ReturnsEmpty()
    {
        // Arrange
        var dbContext = CreateInMemoryContext();
        var httpClient = CreateMockHttpClient();

        var lastMonthEnd = GetLastMonthEnd();

        // Pre-seed IndexDefinition for SP500
        var indexDef = new IndexDefinitionEntity
        {
            IndexId = 1,
            IndexCode = "SP500",
            IndexName = "S&P 500",
            ProxyEtfTicker = "IVV"
        };
        dbContext.IndexDefinitions.Add(indexDef);

        // Pre-seed IndexConstituent with EffectiveDate = last month-end (current)
        var constituent = new IndexConstituentEntity
        {
            IndexId = 1,
            SecurityAlias = 1,
            EffectiveDate = lastMonthEnd,
            Weight = 0.01m,
            SourceId = 10
        };
        dbContext.IndexConstituents.Add(constituent);
        await dbContext.SaveChangesAsync();

        var service = new ISharesConstituentService(httpClient, dbContext);

        // Act
        var staleEtfs = await service.GetStaleEtfsAsync();

        // Assert
        Assert.Empty(staleEtfs);
    }

    /// <summary>
    /// Mixed staleness: Two ETFs, one stale one current — only stale should be returned.
    /// </summary>
    [Fact]
    public async Task GetStaleEtfsAsync_WithMixedStaleness_ReturnsOnlyStalEtf()
    {
        // Arrange
        var dbContext = CreateInMemoryContext();
        var httpClient = CreateMockHttpClient();

        var lastMonthEnd = GetLastMonthEnd();
        var twoMonthsAgo = DateTime.UtcNow.Date.AddMonths(-2);

        // Pre-seed two IndexDefinitions
        var staleIndexDef = new IndexDefinitionEntity
        {
            IndexId = 1,
            IndexCode = "SP500",
            IndexName = "S&P 500",
            ProxyEtfTicker = "IVV"
        };
        var currentIndexDef = new IndexDefinitionEntity
        {
            IndexId = 2,
            IndexCode = "RUSSELL2000",
            IndexName = "Russell 2000",
            ProxyEtfTicker = "IWM"
        };
        dbContext.IndexDefinitions.Add(staleIndexDef);
        dbContext.IndexDefinitions.Add(currentIndexDef);

        // Pre-seed constituents: one stale, one current
        var staleConstituent = new IndexConstituentEntity
        {
            IndexId = 1,
            SecurityAlias = 1,
            EffectiveDate = twoMonthsAgo,
            Weight = 0.01m,
            SourceId = 10
        };
        var currentConstituent = new IndexConstituentEntity
        {
            IndexId = 2,
            SecurityAlias = 2,
            EffectiveDate = lastMonthEnd,
            Weight = 0.01m,
            SourceId = 10
        };
        dbContext.IndexConstituents.Add(staleConstituent);
        dbContext.IndexConstituents.Add(currentConstituent);
        await dbContext.SaveChangesAsync();

        var service = new ISharesConstituentService(httpClient, dbContext);

        // Act
        var staleEtfs = await service.GetStaleEtfsAsync();

        // Assert
        Assert.Single(staleEtfs);
        Assert.Equal("IVV", staleEtfs[0].EtfTicker);
        Assert.Equal("SP500", staleEtfs[0].IndexCode);
    }

    /// <summary>
    /// No constituent data at all — IndexDefinition with no constituents should be returned as stale (null max date).
    /// </summary>
    [Fact]
    public async Task GetStaleEtfsAsync_WithNoConstituentData_ReturnsStalEtf()
    {
        // Arrange
        var dbContext = CreateInMemoryContext();
        var httpClient = CreateMockHttpClient();

        // Pre-seed IndexDefinition but NO IndexConstituent rows
        var indexDef = new IndexDefinitionEntity
        {
            IndexId = 1,
            IndexCode = "SP500",
            IndexName = "S&P 500",
            ProxyEtfTicker = "IVV"
        };
        dbContext.IndexDefinitions.Add(indexDef);
        await dbContext.SaveChangesAsync();

        var service = new ISharesConstituentService(httpClient, dbContext);

        // Act
        var staleEtfs = await service.GetStaleEtfsAsync();

        // Assert
        Assert.NotEmpty(staleEtfs);
        Assert.Single(staleEtfs);
        Assert.Equal("IVV", staleEtfs[0].EtfTicker);
        Assert.Equal("SP500", staleEtfs[0].IndexCode);
    }

    /// <summary>
    /// Only indices with ProxyEtfTicker are checked — indices without it are ignored.
    /// </summary>
    [Fact]
    public async Task GetStaleEtfsAsync_IgnoresIndicesWithoutProxyEtf()
    {
        // Arrange
        var dbContext = CreateInMemoryContext();
        var httpClient = CreateMockHttpClient();

        var twoMonthsAgo = DateTime.UtcNow.Date.AddMonths(-2);

        // Pre-seed two IndexDefinitions: one with ProxyEtfTicker, one without
        var withProxy = new IndexDefinitionEntity
        {
            IndexId = 1,
            IndexCode = "SP500",
            IndexName = "S&P 500",
            ProxyEtfTicker = "IVV"
        };
        var withoutProxy = new IndexDefinitionEntity
        {
            IndexId = 2,
            IndexCode = "CUSTOM",
            IndexName = "Custom Index",
            ProxyEtfTicker = null
        };
        dbContext.IndexDefinitions.Add(withProxy);
        dbContext.IndexDefinitions.Add(withoutProxy);

        // Pre-seed constituent for the one with proxy
        var constituent = new IndexConstituentEntity
        {
            IndexId = 1,
            SecurityAlias = 1,
            EffectiveDate = twoMonthsAgo,
            Weight = 0.01m,
            SourceId = 10
        };
        dbContext.IndexConstituents.Add(constituent);
        await dbContext.SaveChangesAsync();

        var service = new ISharesConstituentService(httpClient, dbContext);

        // Act
        var staleEtfs = await service.GetStaleEtfsAsync();

        // Assert
        Assert.Single(staleEtfs);
        Assert.Equal("IVV", staleEtfs[0].EtfTicker);
    }
}
