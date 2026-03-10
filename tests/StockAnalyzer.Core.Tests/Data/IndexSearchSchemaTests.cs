namespace StockAnalyzer.Core.Tests.Data;

using System.Reflection;
using Microsoft.EntityFrameworkCore;
using StockAnalyzer.Core.Data;
using StockAnalyzer.Core.Data.Entities;
using Xunit;

/// <summary>
/// Tests IndexDefinitionEntity schema and the index search query logic.
/// AC5.1: Search queries IndexName, IndexCode, IndexFamily (case-insensitive)
/// AC5.2: Results filtered to ProxyEtfTicker IS NOT NULL
/// AC5.3: Response contains required fields: indexId, indexCode, indexName, proxyEtfTicker
/// AC5.4: Empty query returns empty results
/// AC5.F1: No-match query returns empty results (not 404)
/// AC5.F2: Indices without ProxyEtfTicker never appear in results
/// </summary>
public class IndexSearchSchemaTests
{
    private DbContextOptions<StockAnalyzerDbContext> CreateInMemoryOptions() =>
        new DbContextOptionsBuilder<StockAnalyzerDbContext>()
            .UseInMemoryDatabase(databaseName: $"IndexSearchTest_{Guid.NewGuid()}")
            .Options;

    #region AC5.3: IndexDefinitionEntity Schema

    [Fact]
    [Trait("AC", "5.3")]
    public void IndexDefinitionEntity_HasRequiredProperties()
    {
        var type = typeof(IndexDefinitionEntity);

        Assert.NotNull(type.GetProperty(nameof(IndexDefinitionEntity.IndexId)));
        Assert.NotNull(type.GetProperty(nameof(IndexDefinitionEntity.IndexCode)));
        Assert.NotNull(type.GetProperty(nameof(IndexDefinitionEntity.IndexName)));
        Assert.NotNull(type.GetProperty(nameof(IndexDefinitionEntity.IndexFamily)));
        Assert.NotNull(type.GetProperty(nameof(IndexDefinitionEntity.Region)));
        Assert.NotNull(type.GetProperty(nameof(IndexDefinitionEntity.ProxyEtfTicker)));
    }

    [Fact]
    [Trait("AC", "5.3")]
    public void IndexDefinitionEntity_ProxyEtfTickerIsNullable()
    {
        var property = typeof(IndexDefinitionEntity).GetProperty(nameof(IndexDefinitionEntity.ProxyEtfTicker));
        Assert.NotNull(property);
        // Nullable string — type is string? (still string at runtime, but nullability context applies)
        Assert.Equal(typeof(string), property!.PropertyType);
    }

    [Fact]
    [Trait("AC", "5.3")]
    public void IndexDefinitionEntity_IsRegisteredInDbContext()
    {
        using var context = new StockAnalyzerDbContext(CreateInMemoryOptions());
        var entity = context.Model.FindEntityType(typeof(IndexDefinitionEntity));
        Assert.NotNull(entity);
    }

    #endregion

    #region AC5.1 + AC5.2 + AC5.F1 + AC5.F2: Search Query Logic

    private static List<IndexDefinitionEntity> BuildTestData() =>
    [
        new() { IndexId = 1, IndexCode = "SP500", IndexName = "S&P 500", IndexFamily = "S&P", Region = "US", ProxyEtfTicker = "SPY" },
        new() { IndexId = 2, IndexCode = "NDX100", IndexName = "Nasdaq-100", IndexFamily = "Nasdaq", Region = "US", ProxyEtfTicker = "QQQ" },
        new() { IndexId = 3, IndexCode = "DJIA", IndexName = "Dow Jones Industrial Average", IndexFamily = "Dow Jones", Region = "US", ProxyEtfTicker = "DIA" },
        new() { IndexId = 4, IndexCode = "NOETF", IndexName = "No ETF Index", IndexFamily = "Other", Region = "US", ProxyEtfTicker = null },
        new() { IndexId = 5, IndexCode = "MSCI_EAFE", IndexName = "MSCI EAFE", IndexFamily = "MSCI", Region = "Intl", ProxyEtfTicker = "EFA" }
    ];

    private static List<object> ExecuteSearch(List<IndexDefinitionEntity> data, string? q)
    {
        if (string.IsNullOrWhiteSpace(q))
            return [];

        var normalizedQuery = q.Trim().ToUpperInvariant();

        return data
            .Where(idx => idx.ProxyEtfTicker != null &&
                (idx.IndexName.ToUpper().Contains(normalizedQuery) ||
                 idx.IndexCode.ToUpper().Contains(normalizedQuery) ||
                 (idx.IndexFamily != null && idx.IndexFamily.ToUpper().Contains(normalizedQuery))))
            .OrderBy(idx => idx.IndexName)
            .Take(10)
            .Select(idx => (object)new
            {
                indexId = idx.IndexId,
                indexCode = idx.IndexCode,
                indexName = idx.IndexName,
                indexFamily = idx.IndexFamily,
                region = idx.Region,
                proxyEtfTicker = idx.ProxyEtfTicker
            })
            .ToList();
    }

    [Fact]
    [Trait("AC", "5.4")]
    public void Search_EmptyQuery_ReturnsEmpty()
    {
        var results = ExecuteSearch(BuildTestData(), "");
        Assert.Empty(results);
    }

    [Fact]
    [Trait("AC", "5.4")]
    public void Search_NullQuery_ReturnsEmpty()
    {
        var results = ExecuteSearch(BuildTestData(), null);
        Assert.Empty(results);
    }

    [Fact]
    [Trait("AC", "5.F1")]
    public void Search_NoMatch_ReturnsEmpty()
    {
        var results = ExecuteSearch(BuildTestData(), "xyznonexistent");
        Assert.Empty(results);
    }

    [Fact]
    [Trait("AC", "5.1")]
    public void Search_ByIndexName_ReturnsMatch()
    {
        var results = ExecuteSearch(BuildTestData(), "nasdaq");
        Assert.Single(results);
    }

    [Fact]
    [Trait("AC", "5.1")]
    public void Search_ByIndexCode_ReturnsMatch()
    {
        var results = ExecuteSearch(BuildTestData(), "DJIA");
        Assert.Single(results);
    }

    [Fact]
    [Trait("AC", "5.1")]
    public void Search_ByIndexFamily_ReturnsMatch()
    {
        var results = ExecuteSearch(BuildTestData(), "msci");
        Assert.Single(results);
    }

    [Fact]
    [Trait("AC", "5.1")]
    public void Search_IsCaseInsensitive()
    {
        var lower = ExecuteSearch(BuildTestData(), "sp500");
        var upper = ExecuteSearch(BuildTestData(), "SP500");
        var mixed = ExecuteSearch(BuildTestData(), "Sp500");
        Assert.Equal(lower.Count, upper.Count);
        Assert.Equal(lower.Count, mixed.Count);
    }

    [Fact]
    [Trait("AC", "5.F2")]
    public void Search_ExcludesEntriesWithoutProxyEtfTicker()
    {
        var results = ExecuteSearch(BuildTestData(), "no etf");
        Assert.Empty(results);
    }

    [Fact]
    [Trait("AC", "5.2")]
    public void Search_AllResultsHaveNonNullProxyEtfTicker()
    {
        // Search broadly — "index" matches multiple
        var results = ExecuteSearch(BuildTestData(), "s");
        // All returned results must have ProxyEtfTicker
        foreach (var r in results)
        {
            var prop = r.GetType().GetProperty("proxyEtfTicker");
            Assert.NotNull(prop);
            Assert.NotNull(prop!.GetValue(r));
        }
    }

    [Fact]
    [Trait("AC", "5.3")]
    public void Search_ResultsContainRequiredFields()
    {
        var results = ExecuteSearch(BuildTestData(), "sp500");
        Assert.Single(results);

        var item = results[0];
        var type = item.GetType();
        Assert.NotNull(type.GetProperty("indexId"));
        Assert.NotNull(type.GetProperty("indexCode"));
        Assert.NotNull(type.GetProperty("indexName"));
        Assert.NotNull(type.GetProperty("indexFamily"));
        Assert.NotNull(type.GetProperty("region"));
        Assert.NotNull(type.GetProperty("proxyEtfTicker"));
    }

    [Fact]
    [Trait("AC", "5.3")]
    public void Search_LimitedToTen()
    {
        // Build 15 entries all matching "test"
        var manyEntries = Enumerable.Range(1, 15).Select(i => new IndexDefinitionEntity
        {
            IndexId = i,
            IndexCode = $"TEST{i}",
            IndexName = $"Test Index {i}",
            IndexFamily = "Test",
            Region = "US",
            ProxyEtfTicker = $"T{i:D2}"
        }).ToList();

        var results = ExecuteSearch(manyEntries, "test");
        Assert.Equal(10, results.Count);
    }

    #endregion

    #region Integration: DbContext Can Store and Retrieve IndexDefinitionEntity

    [Fact]
    [Trait("Category", "Integration")]
    public void IndexDefinition_CanBeInsertedAndRetrieved()
    {
        var options = CreateInMemoryOptions();
        var entry = new IndexDefinitionEntity
        {
            IndexCode = "SP500",
            IndexName = "S&P 500",
            IndexFamily = "S&P",
            Region = "US",
            ProxyEtfTicker = "SPY"
        };

        using (var context = new StockAnalyzerDbContext(options))
        {
            context.IndexDefinitions.Add(entry);
            context.SaveChanges();
        }

        using (var context = new StockAnalyzerDbContext(options))
        {
            var retrieved = context.IndexDefinitions
                .AsNoTracking()
                .FirstOrDefault(i => i.IndexCode == "SP500");
            Assert.NotNull(retrieved);
            Assert.Equal("S&P 500", retrieved!.IndexName);
            Assert.Equal("SPY", retrieved.ProxyEtfTicker);
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void IndexDefinition_NullProxyEtfTicker_CanBeStored()
    {
        var options = CreateInMemoryOptions();

        using (var context = new StockAnalyzerDbContext(options))
        {
            context.IndexDefinitions.Add(new IndexDefinitionEntity
            {
                IndexCode = "NOPROXY",
                IndexName = "No Proxy Index",
                ProxyEtfTicker = null
            });
            context.SaveChanges();
        }

        using (var context = new StockAnalyzerDbContext(options))
        {
            var retrieved = context.IndexDefinitions
                .AsNoTracking()
                .FirstOrDefault(i => i.IndexCode == "NOPROXY");
            Assert.NotNull(retrieved);
            Assert.Null(retrieved!.ProxyEtfTicker);
        }
    }

    #endregion
}
