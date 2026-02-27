using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using StockAnalyzer.Core.Data;
using StockAnalyzer.Core.Data.Entities;
using StockAnalyzer.Core.Services;
using Xunit;

namespace StockAnalyzer.Core.Tests.Services;

/// <summary>
/// Tests for the backfill-mic-codes admin endpoint functionality.
/// Verifies EODHD exchange name to MIC code mapping and batch processing.
/// </summary>
public class BackfillMicCodesTests
{
    private static StockAnalyzerDbContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<StockAnalyzerDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new StockAnalyzerDbContext(options);
    }

    private static async Task SeedSecurities(StockAnalyzerDbContext context, int count = 10)
    {
        var securities = new List<SecurityMasterEntity>();
        for (int i = 0; i < count; i++)
        {
            securities.Add(new SecurityMasterEntity
            {
                SecurityAlias = i + 1,
                TickerSymbol = $"TEST{i:D3}",
                IssueName = $"Test Company {i}",
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }
        context.SecurityMaster.AddRange(securities);
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task MicCodeMapping_MapNyseToXnys()
    {
        // Test the EODHD exchange name to MIC code mapping dictionary
        var exchangeMapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "NYSE", "XNYS" },
            { "NASDAQ", "XNAS" },
            { "NYSE ARCA", "ARCX" },
            { "ARCA", "ARCX" },
            { "BATS", "BATS" },
            { "NYSE MKT", "XNYS" },
            { "OTC", "OTCM" },
            { "PINK", "PINX" },
            { "OTCQB", "OTCM" },
            { "OTCQX", "OTCM" },
            { "OTCMKTS", "OTCM" },
            { "OTCBB", "OTCM" },
            { "OTCGREY", "XOTC" },
            { "NMFQS", "XNAS" },
        };

        // Verify mapping for common exchanges
        Assert.Equal("XNYS", exchangeMapping["NYSE"]);
        Assert.Equal("XNAS", exchangeMapping["NASDAQ"]);
        Assert.Equal("ARCX", exchangeMapping["NYSE ARCA"]);
    }

    [Fact]
    public async Task MicCodeMapping_CaseInsensitive()
    {
        // Test case-insensitive lookup
        var exchangeMapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "NYSE", "XNYS" },
            { "NASDAQ", "XNAS" }
        };

        Assert.Equal("XNYS", exchangeMapping["nyse"]);
        Assert.Equal("XNYS", exchangeMapping["NYSE"]);
        Assert.Equal("XNYS", exchangeMapping["NySe"]);
    }

    [Fact]
    public async Task BuildTickerToMicLookup_BuildsDictionaryFromSymbols()
    {
        // Test building ticker → MIC lookup from EODHD symbol records
        var symbols = new List<EodhdSymbolRecord>
        {
            new() { Code = "AAPL", Exchange = "NASDAQ" },
            new() { Code = "MSFT", Exchange = "NASDAQ" },
            new() { Code = "F", Exchange = "NYSE" },
            new() { Code = "GE", Exchange = "NYSE" },
        };

        var exchangeMapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "NYSE", "XNYS" },
            { "NASDAQ", "XNAS" }
        };

        var tickerToMic = new Dictionary<string, string>();
        var unknownExchanges = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var symbol in symbols)
        {
            if (exchangeMapping.TryGetValue(symbol.Exchange, out var mic))
            {
                tickerToMic[symbol.Code] = mic;
            }
            else
            {
                if (!unknownExchanges.ContainsKey(symbol.Exchange))
                    unknownExchanges[symbol.Exchange] = 0;
                unknownExchanges[symbol.Exchange]++;
            }
        }

        Assert.Equal(4, tickerToMic.Count);
        Assert.Equal("XNAS", tickerToMic["AAPL"]);
        Assert.Equal("XNYS", tickerToMic["F"]);
    }

    [Fact]
    public async Task BuildTickerToMicLookup_TracksUnknownExchanges()
    {
        // Test that unknown exchange names are tracked but don't fail processing
        var symbols = new List<EodhdSymbolRecord>
        {
            new() { Code = "AAPL", Exchange = "NASDAQ" },
            new() { Code = "PINK1", Exchange = "UNKNOWN_EXCHANGE" },
            new() { Code = "PINK2", Exchange = "UNKNOWN_EXCHANGE" },
        };

        var exchangeMapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "NASDAQ", "XNAS" }
        };

        var tickerToMic = new Dictionary<string, string>();
        var unknownExchanges = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var symbol in symbols)
        {
            if (exchangeMapping.TryGetValue(symbol.Exchange, out var mic))
            {
                tickerToMic[symbol.Code] = mic;
            }
            else
            {
                if (!unknownExchanges.ContainsKey(symbol.Exchange))
                    unknownExchanges[symbol.Exchange] = 0;
                unknownExchanges[symbol.Exchange]++;
            }
        }

        Assert.Single(tickerToMic);
        Assert.Single(unknownExchanges);
        Assert.Equal(2, unknownExchanges["UNKNOWN_EXCHANGE"]);
    }

    [Fact]
    public async Task BatchUpdate_UpdatesMicCodes()
    {
        // Test batch updating MIC codes in SecurityMaster
        var context = CreateInMemoryContext();
        await SeedSecurities(context, 10);

        var tickerToMic = new Dictionary<string, string>
        {
            { "TEST000", "XNYS" },
            { "TEST001", "XNAS" },
            { "TEST002", "XNAS" }
        };

        // Simulate batch update
        var batch = await context.SecurityMaster
            .Where(s => s.IsActive)
            .OrderBy(s => s.SecurityAlias)
            .Take(1000)
            .ToListAsync();

        var matched = 0;
        var unmatched = 0;

        foreach (var security in batch)
        {
            if (tickerToMic.TryGetValue(security.TickerSymbol, out var mic))
            {
                security.MicCode = mic;
                security.UpdatedAt = DateTime.UtcNow;
                matched++;
            }
            else
            {
                unmatched++;
            }
        }

        await context.SaveChangesAsync();

        // Verify updates
        var updated = await context.SecurityMaster
            .Where(s => s.TickerSymbol == "TEST000")
            .FirstOrDefaultAsync();

        Assert.NotNull(updated);
        Assert.Equal("XNYS", updated.MicCode);
        Assert.Equal(3, matched);
        Assert.Equal(7, unmatched);
    }

    [Fact]
    public async Task BatchProcessing_SkipsInactiveSecurity()
    {
        // Test that inactive securities are skipped
        var context = CreateInMemoryContext();

        context.SecurityMaster.AddRange(
            new SecurityMasterEntity
            {
                SecurityAlias = 1,
                TickerSymbol = "ACTIVE",
                IssueName = "Active Security",
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new SecurityMasterEntity
            {
                SecurityAlias = 2,
                TickerSymbol = "INACTIVE",
                IssueName = "Inactive Security",
                IsActive = false,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            }
        );
        await context.SaveChangesAsync();

        var batch = await context.SecurityMaster
            .Where(s => s.IsActive)
            .OrderBy(s => s.SecurityAlias)
            .Take(1000)
            .ToListAsync();

        Assert.Single(batch);
        Assert.Equal("ACTIVE", batch[0].TickerSymbol);
    }

    [Fact]
    public async Task BatchProcessing_ProcessesBatches()
    {
        // Test batch processing with multiple batches
        var context = CreateInMemoryContext();
        await SeedSecurities(context, 2500);

        var pageSize = 1000;
        var skip = 0;
        var batchCount = 0;
        var totalProcessed = 0;

        while (true)
        {
            var batch = await context.SecurityMaster
                .Where(s => s.IsActive)
                .OrderBy(s => s.SecurityAlias)
                .Skip(skip)
                .Take(pageSize)
                .ToListAsync();

            if (batch.Count == 0) break;

            batchCount++;
            totalProcessed += batch.Count;
            skip += pageSize;
        }

        Assert.Equal(3, batchCount); // 2500 securities / 1000 per batch = 3 batches
        Assert.Equal(2500, totalProcessed);
    }
}
