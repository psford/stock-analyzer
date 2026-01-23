using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StockAnalyzer.Core.Data.Entities;
using StockAnalyzer.Core.Services;

namespace StockAnalyzer.Core.Data;

/// <summary>
/// SQL Server implementation of IPriceRepository.
/// Manages historical price data in the data.Prices table.
/// Optimized for efficient querying of large datasets (~1.26M+ rows).
/// </summary>
public class SqlPriceRepository : IPriceRepository
{
    private readonly StockAnalyzerDbContext _context;
    private readonly ILogger<SqlPriceRepository> _logger;

    public SqlPriceRepository(StockAnalyzerDbContext context, ILogger<SqlPriceRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<List<PriceEntity>> GetPricesAsync(int securityAlias, DateTime startDate, DateTime endDate)
    {
        return await _context.Prices
            .AsNoTracking()
            .Where(p => p.SecurityAlias == securityAlias &&
                        p.EffectiveDate >= startDate.Date &&
                        p.EffectiveDate <= endDate.Date)
            .OrderBy(p => p.EffectiveDate)
            .ToListAsync();
    }

    /// <inheritdoc />
    public async Task<List<PriceEntity>> GetAllPricesAsync(int securityAlias)
    {
        return await _context.Prices
            .AsNoTracking()
            .Where(p => p.SecurityAlias == securityAlias)
            .OrderBy(p => p.EffectiveDate)
            .ToListAsync();
    }

    /// <inheritdoc />
    public async Task<List<PriceEntity>> GetPricesForDateAsync(DateTime date)
    {
        return await _context.Prices
            .AsNoTracking()
            .Where(p => p.EffectiveDate == date.Date)
            .ToListAsync();
    }

    /// <inheritdoc />
    public async Task<PriceEntity?> GetLatestPriceAsync(int securityAlias)
    {
        return await _context.Prices
            .AsNoTracking()
            .Where(p => p.SecurityAlias == securityAlias)
            .OrderByDescending(p => p.EffectiveDate)
            .FirstOrDefaultAsync();
    }

    /// <inheritdoc />
    public async Task<Dictionary<int, PriceEntity>> GetLatestPricesAsync(IEnumerable<int> securityAliases)
    {
        var aliasList = securityAliases.ToList();
        if (aliasList.Count == 0)
            return new Dictionary<int, PriceEntity>();

        // Get the latest price for each security using a grouped query
        var latestPrices = await _context.Prices
            .AsNoTracking()
            .Where(p => aliasList.Contains(p.SecurityAlias))
            .GroupBy(p => p.SecurityAlias)
            .Select(g => g.OrderByDescending(p => p.EffectiveDate).First())
            .ToListAsync();

        return latestPrices.ToDictionary(p => p.SecurityAlias);
    }

    /// <inheritdoc />
    public async Task<PriceEntity> CreateAsync(PriceCreateDto dto)
    {
        var entity = new PriceEntity
        {
            SecurityAlias = dto.SecurityAlias,
            EffectiveDate = dto.EffectiveDate.Date,
            Open = dto.Open,
            High = dto.High,
            Low = dto.Low,
            Close = dto.Close,
            Volatility = dto.Volatility,
            Volume = dto.Volume,
            AdjustedClose = dto.AdjustedClose,
            CreatedAt = DateTime.UtcNow
        };

        _context.Prices.Add(entity);
        await _context.SaveChangesAsync();

        return entity;
    }

    /// <inheritdoc />
    public async Task<int> BulkInsertAsync(IEnumerable<PriceCreateDto> prices)
    {
        var priceList = prices.ToList();
        var now = DateTime.UtcNow;
        var count = 0;

        // Process in batches of 1000 for efficiency with large datasets
        foreach (var batch in priceList.Chunk(1000))
        {
            var entities = batch.Select(dto => new PriceEntity
            {
                SecurityAlias = dto.SecurityAlias,
                EffectiveDate = dto.EffectiveDate.Date,
                Open = dto.Open,
                High = dto.High,
                Low = dto.Low,
                Close = dto.Close,
                Volatility = dto.Volatility,
                Volume = dto.Volume,
                AdjustedClose = dto.AdjustedClose,
                CreatedAt = now
            });

            _context.Prices.AddRange(entities);
            await _context.SaveChangesAsync();
            count += batch.Length;

            // Log progress for large inserts
            if (count % 10000 == 0)
            {
                _logger.LogInformation("Bulk insert progress: {Count} prices inserted", count);
            }
        }

        _logger.LogInformation("Bulk insert complete: {Count} prices inserted", count);
        return count;
    }

    /// <inheritdoc />
    public async Task UpsertAsync(PriceCreateDto dto)
    {
        var effectiveDate = dto.EffectiveDate.Date;
        var existing = await _context.Prices
            .FirstOrDefaultAsync(p => p.SecurityAlias == dto.SecurityAlias &&
                                       p.EffectiveDate == effectiveDate);

        if (existing != null)
        {
            // Update existing
            existing.Open = dto.Open;
            existing.High = dto.High;
            existing.Low = dto.Low;
            existing.Close = dto.Close;
            existing.Volatility = dto.Volatility;
            existing.Volume = dto.Volume;
            existing.AdjustedClose = dto.AdjustedClose;
        }
        else
        {
            // Insert new
            _context.Prices.Add(new PriceEntity
            {
                SecurityAlias = dto.SecurityAlias,
                EffectiveDate = effectiveDate,
                Open = dto.Open,
                High = dto.High,
                Low = dto.Low,
                Close = dto.Close,
                Volatility = dto.Volatility,
                Volume = dto.Volume,
                AdjustedClose = dto.AdjustedClose,
                CreatedAt = DateTime.UtcNow
            });
        }

        await _context.SaveChangesAsync();
    }

    /// <inheritdoc />
    public async Task<int> DeleteOlderThanAsync(int securityAlias, DateTime cutoffDate)
    {
        var count = await _context.Prices
            .Where(p => p.SecurityAlias == securityAlias && p.EffectiveDate < cutoffDate.Date)
            .ExecuteDeleteAsync();

        if (count > 0)
        {
            _logger.LogInformation("Deleted {Count} prices older than {Date} for security {Alias}",
                count, cutoffDate.Date, securityAlias);
        }

        return count;
    }

    /// <inheritdoc />
    public async Task<(DateTime? Earliest, DateTime? Latest)> GetDateRangeAsync(int securityAlias)
    {
        var dates = await _context.Prices
            .AsNoTracking()
            .Where(p => p.SecurityAlias == securityAlias)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Earliest = g.Min(p => (DateTime?)p.EffectiveDate),
                Latest = g.Max(p => (DateTime?)p.EffectiveDate)
            })
            .FirstOrDefaultAsync();

        return (dates?.Earliest, dates?.Latest);
    }

    /// <inheritdoc />
    public async Task<long> GetTotalCountAsync()
    {
        return await _context.Prices.LongCountAsync();
    }

    /// <inheritdoc />
    public async Task<int> GetCountForSecurityAsync(int securityAlias)
    {
        return await _context.Prices.CountAsync(p => p.SecurityAlias == securityAlias);
    }
}
