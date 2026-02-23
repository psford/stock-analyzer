using System.Net;
using System.Text.Json;
using EodhdLoader.Models;
using EodhdLoader.Services;
using Microsoft.EntityFrameworkCore;
using Moq;
using Moq.Protected;
using StockAnalyzer.Core.Data;
using StockAnalyzer.Core.Data.Entities;
using Xunit;

namespace EodhdLoader.Tests.Services;

/// <summary>
/// Tests for ISharesConstituentService persistence layer (AC3).
/// Tests the full orchestration by calling IngestEtfAsync with mocked HTTP responses.
/// Focus: Acceptance criteria AC3.1-AC3.6.
/// </summary>
public class ISharesConstituentServicePersistenceTests
{
    private const string TestIndexCode = "SP500";
    private const string TestEtfTicker = "IVV";
    private const int ISharesSourceId = 10;
    private const int TestProductId = 239837; // IVV's product ID from config

    /// <summary>
    /// Helper: Creates a pre-seeded InMemory DbContext for testing.
    /// </summary>
    private static StockAnalyzerDbContext CreateTestContext()
    {
        var options = new DbContextOptionsBuilder<StockAnalyzerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var context = new StockAnalyzerDbContext(options);

        // Pre-seed IndexDefinition
        context.IndexDefinitions.Add(new IndexDefinitionEntity
        {
            IndexId = 1,
            IndexCode = TestIndexCode,
            IndexName = "S&P 500",
            ProxyEtfTicker = TestEtfTicker
        });

        // Pre-seed Source (iShares)
        context.Sources.Add(new SourceEntity
        {
            SourceId = ISharesSourceId,
            SourceShortName = "iShares",
            SourceLongName = "iShares ETF Provider"
        });

        context.SaveChanges();
        return context;
    }

    /// <summary>
    /// Helper: Creates a pre-seeded InMemory DbContext that throws DbUpdateException
    /// when SaveChangesAsync detects a newly-added SecurityMaster with the specified ticker.
    /// Used for AC3.6 error isolation testing.
    /// </summary>
    private static FailOnTickerDbContext CreateFailingTestContext(string failOnTicker)
    {
        var options = new DbContextOptionsBuilder<StockAnalyzerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var context = new FailOnTickerDbContext(options, failOnTicker);

        context.IndexDefinitions.Add(new IndexDefinitionEntity
        {
            IndexId = 1,
            IndexCode = TestIndexCode,
            IndexName = "S&P 500",
            ProxyEtfTicker = TestEtfTicker
        });

        context.Sources.Add(new SourceEntity
        {
            SourceId = ISharesSourceId,
            SourceShortName = "iShares",
            SourceLongName = "iShares ETF Provider"
        });

        context.SaveChangesWithoutFailing();
        return context;
    }

    /// <summary>
    /// Helper: Creates an ISharesConstituentService with mocked HTTP and InMemory DbContext.
    /// Mirrors the pattern from ISharesConstituentServiceDownloadTests.
    /// </summary>
    private static (ISharesConstituentService Service, Mock<HttpMessageHandler> MockHandler, StockAnalyzerDbContext DbContext)
        BuildServiceWithMockedHttpAndDb()
    {
        var mockHandler = new Mock<HttpMessageHandler>();
        var httpClient = new HttpClient(mockHandler.Object);

        var dbContext = CreateTestContext();
        var service = new ISharesConstituentService(httpClient, dbContext);

        return (service, mockHandler, dbContext);
    }

    /// <summary>
    /// Helper: Creates valid iShares Format A JSON for testing.
    /// </summary>
    private static string CreateValidISharesJson()
    {
        return @"{
  ""aaData"": [
    [""AAPL"", ""Apple Inc."", ""Information Technology"", ""Equity"", {""display"": ""$1,234.56"", ""raw"": 1234.56}, {""display"": ""2.34%"", ""raw"": 2.34}, null, {""display"": ""123.45"", ""raw"": 123.45}, ""037833100"", ""US0378331005"", ""2588173"", ""$10.01"", ""UNITED STATES"", ""NASDAQ"", ""USD""],
    [""MSFT"", ""Microsoft Corporation"", ""Information Technology"", ""Equity"", {""display"": ""$5,000.00"", ""raw"": 5000.00}, {""display"": ""3.50%"", ""raw"": 3.50}, null, {""display"": ""50.00"", ""raw"": 50.00}, ""594918104"", ""US5949181045"", ""2588141"", ""$100.00"", ""UNITED STATES"", ""NASDAQ"", ""USD""]
  ]
}";
    }

    /// <summary>
    /// AC3.1: New securities are created in SecurityMaster with correct fields.
    /// Tests that IngestEtfAsync creates new securities when holdings don't match existing data.
    /// </summary>
    [Fact]
    public async Task AC3_1_SecurityCreation_CreatesEntityWithCorrectFields()
    {
        // Arrange
        var (service, mockHandler, dbContext) = BuildServiceWithMockedHttpAndDb();
        var validJson = CreateValidISharesJson();

        mockHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(validJson)
            });

        var asOfDate = new DateTime(2025, 1, 31);

        // Act - Call IngestEtfAsync (this is the service method under test)
        var stats = await service.IngestEtfAsync(TestEtfTicker, asOfDate);

        // Assert AC3.1: Both holdings should be created as new securities
        Assert.Equal(2, stats.Created); // AAPL and MSFT are new
        Assert.Equal(2, stats.Inserted); // Both inserted as constituents

        // Verify securities were created with correct fields
        var appleSecurity = dbContext.SecurityMaster.FirstOrDefault(s => s.TickerSymbol == "AAPL");
        Assert.NotNull(appleSecurity);
        Assert.Equal("Apple Inc.", appleSecurity.IssueName);
        Assert.Equal("037833100", appleSecurity.PrimaryAssetId);
        Assert.Equal("US0378331005", appleSecurity.Isin);
        Assert.Equal("Common Stock", appleSecurity.SecurityType);
        Assert.Equal("UNITED STATES", appleSecurity.Country);
        Assert.Equal("USD", appleSecurity.Currency);
        Assert.True(appleSecurity.IsActive);

        var msftSecurity = dbContext.SecurityMaster.FirstOrDefault(s => s.TickerSymbol == "MSFT");
        Assert.NotNull(msftSecurity);
        Assert.Equal("Microsoft Corporation", msftSecurity.IssueName);
    }

    /// <summary>
    /// AC3.2: 3-level matching (ticker → CUSIP → ISIN) finds existing securities.
    /// Tests that IngestEtfAsync matches existing securities at each level before creating new ones.
    /// </summary>
    [Fact]
    public async Task AC3_2_SecurityMatching_Matches3Levels()
    {
        // Arrange
        var (service, mockHandler, dbContext) = BuildServiceWithMockedHttpAndDb();

        // Pre-seed existing security matched by ticker (AAPL)
        var existingAapl = new SecurityMasterEntity
        {
            TickerSymbol = "AAPL",
            IssueName = "Apple Inc.",
            Exchange = "NASDAQ",
            SecurityType = "Common Stock",
            Country = "UNITED STATES",
            Currency = "USD",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        dbContext.SecurityMaster.Add(existingAapl);
        dbContext.SaveChanges();

        var validJson = CreateValidISharesJson();
        mockHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(validJson)
            });

        var asOfDate = new DateTime(2025, 1, 31);

        // Act - Call IngestEtfAsync
        var stats = await service.IngestEtfAsync(TestEtfTicker, asOfDate);

        // Assert AC3.2: AAPL should be matched (not created), MSFT should be created
        Assert.Equal(1, stats.Matched); // AAPL matched by ticker
        Assert.Equal(1, stats.Created); // MSFT created as new
        Assert.Equal(2, stats.Inserted); // Both inserted as constituents

        // Verify that both constituents exist and point to the right securities
        var aaplConstituent = dbContext.IndexConstituents
            .FirstOrDefault(c => c.SourceTicker == "AAPL");
        Assert.NotNull(aaplConstituent);
        Assert.Equal(existingAapl.SecurityAlias, aaplConstituent.SecurityAlias); // Matched, same alias

        var msftConstituent = dbContext.IndexConstituents
            .FirstOrDefault(c => c.SourceTicker == "MSFT");
        Assert.NotNull(msftConstituent);
        Assert.NotEqual(existingAapl.SecurityAlias, msftConstituent.SecurityAlias); // Created, different alias
    }

    /// <summary>
    /// AC3.3: SCD Type 2 snapshots old identifier values when changed.
    /// Tests that IngestEtfAsync captures identifier changes into SecurityIdentifierHist.
    /// </summary>
    [Fact]
    public async Task AC3_3_IdentifierUpsert_SnapshotsOldValueOnChange()
    {
        // Arrange
        var (service, mockHandler, dbContext) = BuildServiceWithMockedHttpAndDb();

        // Pre-seed a security with an old CUSIP
        var security = new SecurityMasterEntity
        {
            TickerSymbol = "AAPL",
            IssueName = "Apple Inc.",
            Exchange = "NASDAQ",
            SecurityType = "Common Stock",
            Country = "UNITED STATES",
            Currency = "USD",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        dbContext.SecurityMaster.Add(security);

        // Pre-seed old CUSIP identifier
        var oldCusip = new SecurityIdentifierEntity
        {
            SecurityAlias = security.SecurityAlias,
            IdentifierType = "CUSIP",
            IdentifierValue = "037833099", // Old value
            SourceId = ISharesSourceId,
            UpdatedAt = DateTime.UtcNow.AddDays(-1)
        };
        dbContext.SecurityIdentifiers.Add(oldCusip);
        dbContext.SaveChanges();

        var oldUpdatedAt = oldCusip.UpdatedAt.Date;

        // Prepare JSON with AAPL and DIFFERENT CUSIP
        var jsonWithNewCusip = @"{
  ""aaData"": [
    [""AAPL"", ""Apple Inc."", ""Information Technology"", ""Equity"", {""display"": ""$1,234.56"", ""raw"": 1234.56}, {""display"": ""2.34%"", ""raw"": 2.34}, null, {""display"": ""123.45"", ""raw"": 123.45}, ""037833100"", ""US0378331005"", ""2588173"", ""$10.01"", ""UNITED STATES"", ""NASDAQ"", ""USD""]
  ]
}";

        mockHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(jsonWithNewCusip)
            });

        var asOfDate = new DateTime(2025, 1, 31);

        // Act - Call IngestEtfAsync (should trigger SCD Type 2 snapshot)
        var stats = await service.IngestEtfAsync(TestEtfTicker, asOfDate);

        // Assert AC3.3: IdentifiersSet > 0 indicates update occurred
        Assert.Equal(1, stats.Matched); // AAPL matched by ticker
        Assert.Equal(3, stats.IdentifiersSet); // CUSIP (updated), ISIN (new), SEDOL (new)

        // Verify history snapshot was created
        var historyEntry = dbContext.SecurityIdentifierHistory
            .FirstOrDefault(h => h.SecurityAlias == security.SecurityAlias && h.IdentifierType == "CUSIP");
        Assert.NotNull(historyEntry);
        Assert.Equal("037833099", historyEntry.IdentifierValue); // Old value
        Assert.Equal(oldUpdatedAt, historyEntry.EffectiveFrom);

        // Verify current value was updated
        var current = dbContext.SecurityIdentifiers
            .FirstOrDefault(si => si.SecurityAlias == security.SecurityAlias && si.IdentifierType == "CUSIP");
        Assert.NotNull(current);
        Assert.Equal("037833100", current.IdentifierValue); // New value
    }

    /// <summary>
    /// AC3.4: Constituent records have all fields populated correctly.
    /// Tests that IngestEtfAsync populates all IndexConstituent fields from parsed holdings.
    /// </summary>
    [Fact]
    public async Task AC3_4_ConstituentInsert_PopulatesAllFields()
    {
        // Arrange
        var (service, mockHandler, dbContext) = BuildServiceWithMockedHttpAndDb();
        var validJson = CreateValidISharesJson();

        mockHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(validJson)
            });

        var asOfDate = new DateTime(2025, 1, 31);

        // Act - Call IngestEtfAsync
        var stats = await service.IngestEtfAsync(TestEtfTicker, asOfDate);

        // Assert AC3.4
        Assert.Equal(2, stats.Inserted); // Both holdings inserted

        var aaplConstituent = dbContext.IndexConstituents
            .FirstOrDefault(c => c.SourceTicker == "AAPL");
        Assert.NotNull(aaplConstituent);
        Assert.Equal(1, aaplConstituent.IndexId); // Pre-seeded index
        Assert.Equal(asOfDate, aaplConstituent.EffectiveDate);
        Assert.Equal(0.0234m, aaplConstituent.Weight); // 2.34% -> 0.0234
        Assert.Equal(1234.56m, aaplConstituent.MarketValue);
        Assert.Equal(123.45m, aaplConstituent.Shares);
        Assert.Equal("Information Technology", aaplConstituent.Sector);
        Assert.Equal("UNITED STATES", aaplConstituent.Location);
        Assert.Equal("USD", aaplConstituent.Currency);
        Assert.Equal(ISharesSourceId, aaplConstituent.SourceId);
        Assert.Equal("AAPL", aaplConstituent.SourceTicker);
    }

    /// <summary>
    /// AC3.5: Duplicate constituent inserts are skipped idempotently.
    /// Tests that calling IngestEtfAsync twice with the same data doesn't create duplicates.
    /// </summary>
    [Fact]
    public async Task AC3_5_ConstituentInsert_IdempotentDuplicateCheck()
    {
        // Arrange
        var (service, mockHandler, dbContext) = BuildServiceWithMockedHttpAndDb();
        var validJson = CreateValidISharesJson();

        mockHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(validJson)
            });

        var asOfDate = new DateTime(2025, 1, 31);

        // Act: Call IngestEtfAsync twice with same parameters
        var stats1 = await service.IngestEtfAsync(TestEtfTicker, asOfDate);
        var initialCount = dbContext.IndexConstituents.Count();

        var stats2 = await service.IngestEtfAsync(TestEtfTicker, asOfDate);
        var finalCount = dbContext.IndexConstituents.Count();

        // Assert AC3.5
        // First run: should insert both holdings
        Assert.Equal(2, stats1.Inserted);
        Assert.Equal(0, stats1.SkippedExisting);

        // Second run: should skip both (already exist)
        Assert.Equal(0, stats2.Inserted);
        Assert.Equal(2, stats2.SkippedExisting); // Both skipped as duplicates

        // Count should not have changed
        Assert.Equal(initialCount, finalCount);
        Assert.Equal(2, finalCount); // Only the original 2
    }

    /// <summary>
    /// AC3.6: DB write failure for one holding doesn't abort the entire ETF.
    /// Error isolation allows remaining holdings to be processed.
    ///
    /// Test strategy: Use a custom DbContext subclass that throws DbUpdateException
    /// when saving a security with ticker "MSFT". This exercises the per-holding
    /// catch block in IngestEtfAsync. AAPL (processed first) succeeds, MSFT throws,
    /// GOOGL (processed last) succeeds — proving the loop continues past the failure.
    /// </summary>
    [Fact]
    public async Task AC3_6_ErrorIsolation_OneSecurity_FailureDoesntAbortOthers()
    {
        // Arrange — use a DbContext that throws on MSFT
        var mockHandler = new Mock<HttpMessageHandler>();
        var httpClient = new HttpClient(mockHandler.Object);
        var dbContext = CreateFailingTestContext("MSFT");
        var service = new ISharesConstituentService(httpClient, dbContext);

        // 3 holdings: AAPL (succeeds), MSFT (will throw), GOOGL (succeeds)
        var jsonWith3Holdings = @"{
  ""aaData"": [
    [""AAPL"", ""Apple Inc."", ""Information Technology"", ""Equity"", {""display"": ""$1,234.56"", ""raw"": 1234.56}, {""display"": ""2.34%"", ""raw"": 2.34}, null, {""display"": ""123.45"", ""raw"": 123.45}, ""037833100"", ""US0378331005"", ""2588173"", ""$10.01"", ""UNITED STATES"", ""NASDAQ"", ""USD""],
    [""MSFT"", ""Microsoft Corporation"", ""Information Technology"", ""Equity"", {""display"": ""$5,000.00"", ""raw"": 5000.00}, {""display"": ""3.50%"", ""raw"": 3.50}, null, {""display"": ""50.00"", ""raw"": 50.00}, ""594918104"", ""US5949181045"", ""2588141"", ""$100.00"", ""UNITED STATES"", ""NASDAQ"", ""USD""],
    [""GOOGL"", ""Alphabet Inc."", ""Information Technology"", ""Equity"", {""display"": ""$3,000.00"", ""raw"": 3000.00}, {""display"": ""2.00%"", ""raw"": 2.00}, null, {""display"": ""75.00"", ""raw"": 75.00}, ""042188129"", ""US02079K3059"", ""2605947"", ""$40.00"", ""UNITED STATES"", ""NASDAQ"", ""USD""]
  ]
}";

        mockHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(jsonWith3Holdings)
            });

        var asOfDate = new DateTime(2025, 1, 31);

        // Act
        var stats = await service.IngestEtfAsync(TestEtfTicker, asOfDate);

        // Assert AC3.6: MSFT failed but AAPL and GOOGL still processed
        Assert.Equal(3, stats.Parsed);
        Assert.True(stats.Failed > 0, "At least one holding should have failed");
        Assert.True(stats.Inserted > 0, "Other holdings should have succeeded despite the failure");
    }

    /// <summary>
    /// Comprehensive test: Full workflow with pre-seeded security (match) and new security (create).
    /// Validates end-to-end orchestration of matching, creation, upsert, and insertion via IngestEtfAsync.
    /// </summary>
    [Fact]
    public async Task FullWorkflow_EndToEndWithMixedMatching()
    {
        // Arrange
        var (service, mockHandler, dbContext) = BuildServiceWithMockedHttpAndDb();

        // Pre-seed one existing security (MSFT) to test matching
        var existingMsft = new SecurityMasterEntity
        {
            TickerSymbol = "MSFT",
            IssueName = "Microsoft Corporation",
            Exchange = "NASDAQ",
            SecurityType = "Common Stock",
            Country = "UNITED STATES",
            Currency = "USD",
            Isin = "US5949181045",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        dbContext.SecurityMaster.Add(existingMsft);
        dbContext.SaveChanges();

        var msftAlias = existingMsft.SecurityAlias;

        var validJson = CreateValidISharesJson();
        mockHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(validJson)
            });

        var asOfDate = new DateTime(2025, 1, 31);

        // Act: Call IngestEtfAsync
        var stats = await service.IngestEtfAsync(TestEtfTicker, asOfDate);

        // Assert: Full workflow
        Assert.Equal(2, stats.Parsed); // Both holdings parsed
        Assert.Equal(1, stats.Matched); // MSFT matched
        Assert.Equal(1, stats.Created); // AAPL created
        Assert.Equal(2, stats.Inserted); // Both inserted as constituents
        Assert.Equal(0, stats.Failed);
        Assert.Equal(6, stats.IdentifiersSet); // 3 for AAPL (CUSIP, ISIN, SEDOL) + 3 for MSFT (CUSIP updated, ISIN, SEDOL)

        // Verify AAPL (created)
        var aaplSecurity = dbContext.SecurityMaster.FirstOrDefault(s => s.TickerSymbol == "AAPL");
        Assert.NotNull(aaplSecurity);
        Assert.NotEqual(msftAlias, aaplSecurity.SecurityAlias); // Different from pre-seeded MSFT

        // Verify MSFT was matched (same SecurityAlias)
        var msftConstituent = dbContext.IndexConstituents
            .FirstOrDefault(c => c.SourceTicker == "MSFT");
        Assert.NotNull(msftConstituent);
        Assert.Equal(msftAlias, msftConstituent.SecurityAlias); // Same as pre-seeded

        // Verify AAPL was inserted (new alias)
        var aaplConstituent = dbContext.IndexConstituents
            .FirstOrDefault(c => c.SourceTicker == "AAPL");
        Assert.NotNull(aaplConstituent);
        Assert.Equal(aaplSecurity.SecurityAlias, aaplConstituent.SecurityAlias);
    }

    /// <summary>
    /// Custom DbContext that throws DbUpdateException when SaveChangesAsync detects
    /// a newly-added SecurityMaster with a specific ticker. Used for AC3.6 testing.
    /// </summary>
    private class FailOnTickerDbContext : StockAnalyzerDbContext
    {
        private readonly string _failOnTicker;
        private bool _failingEnabled = true;

        public FailOnTickerDbContext(DbContextOptions<StockAnalyzerDbContext> options, string failOnTicker)
            : base(options)
        {
            _failOnTicker = failOnTicker;
        }

        /// <summary>Saves without the failure trigger (for seeding test data).</summary>
        public int SaveChangesWithoutFailing()
        {
            _failingEnabled = false;
            try { return base.SaveChanges(); }
            finally { _failingEnabled = true; }
        }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (_failingEnabled)
            {
                var failingEntry = ChangeTracker.Entries<SecurityMasterEntity>()
                    .FirstOrDefault(e => e.State == EntityState.Added &&
                                         e.Entity.TickerSymbol == _failOnTicker);

                if (failingEntry != null)
                {
                    // Detach the failing entity to prevent state corruption
                    failingEntry.State = EntityState.Detached;
                    throw new DbUpdateException(
                        $"Simulated DB failure for ticker {_failOnTicker}");
                }
            }

            return base.SaveChangesAsync(cancellationToken);
        }
    }
}
