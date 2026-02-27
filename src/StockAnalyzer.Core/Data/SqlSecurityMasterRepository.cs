using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StockAnalyzer.Core.Data.Entities;
using StockAnalyzer.Core.Helpers;
using StockAnalyzer.Core.Services;

namespace StockAnalyzer.Core.Data;

/// <summary>
/// SQL Server implementation of ISecurityMasterRepository.
/// Manages the canonical list of securities in the data.SecurityMaster table.
/// </summary>
public class SqlSecurityMasterRepository : ISecurityMasterRepository
{
    private readonly StockAnalyzerDbContext _context;
    private readonly ILogger<SqlSecurityMasterRepository> _logger;

    public SqlSecurityMasterRepository(StockAnalyzerDbContext context, ILogger<SqlSecurityMasterRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<SecurityMasterEntity?> GetByTickerAsync(string ticker)
    {
        if (string.IsNullOrWhiteSpace(ticker))
            return null;

        var normalizedTicker = ticker.Trim().ToUpperInvariant();
        return await _context.SecurityMaster
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.TickerSymbol == normalizedTicker);
    }

    /// <inheritdoc />
    public async Task<SecurityMasterEntity?> GetByAliasAsync(int securityAlias)
    {
        return await _context.SecurityMaster
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.SecurityAlias == securityAlias);
    }

    /// <inheritdoc />
    public async Task<List<SecurityMasterEntity>> GetAllActiveAsync()
    {
        return await _context.SecurityMaster
            .AsNoTracking()
            .Where(s => s.IsActive)
            .OrderBy(s => s.TickerSymbol)
            .ToListAsync();
    }

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(string ticker)
    {
        if (string.IsNullOrWhiteSpace(ticker))
            return false;

        var normalizedTicker = ticker.Trim().ToUpperInvariant();
        return await _context.SecurityMaster
            .AnyAsync(s => s.TickerSymbol == normalizedTicker);
    }

    /// <inheritdoc />
    public async Task<SecurityMasterEntity> CreateAsync(SecurityMasterCreateDto dto)
    {
        var now = DateTime.UtcNow;
        var entity = new SecurityMasterEntity
        {
            TickerSymbol = dto.TickerSymbol.Trim().ToUpperInvariant(),
            IssueName = dto.IssueName.Trim(),
            PrimaryAssetId = dto.PrimaryAssetId?.Trim(),
            // MicCode: populated by backfill phase (replaces Exchange field)
            MicCode = null,
            SecurityType = dto.SecurityType?.Trim(),
            Country = dto.Country?.Trim(),
            Currency = dto.Currency?.Trim(),
            Isin = dto.Isin?.Trim(),
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        };

        _context.SecurityMaster.Add(entity);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Created security: {Ticker} ({Name})", LogSanitizer.Sanitize(entity.TickerSymbol), LogSanitizer.Sanitize(entity.IssueName));
        return entity;
    }

    /// <inheritdoc />
    public async Task UpdateAsync(int securityAlias, SecurityMasterUpdateDto dto)
    {
        var entity = await _context.SecurityMaster.FindAsync(securityAlias);
        if (entity == null)
        {
            _logger.LogWarning("Attempted to update non-existent security alias: {Alias}", securityAlias);
            return;
        }

        if (dto.IssueName != null)
            entity.IssueName = dto.IssueName.Trim();
        if (dto.PrimaryAssetId != null)
            entity.PrimaryAssetId = dto.PrimaryAssetId.Trim();
        if (dto.MicCode != null)
            entity.MicCode = dto.MicCode.Trim();
        if (dto.SecurityType != null)
            entity.SecurityType = dto.SecurityType.Trim();
        if (dto.Country != null)
            entity.Country = dto.Country.Trim();
        if (dto.Currency != null)
            entity.Currency = dto.Currency.Trim();
        if (dto.Isin != null)
            entity.Isin = dto.Isin.Trim();
        if (dto.IsActive.HasValue)
            entity.IsActive = dto.IsActive.Value;

        entity.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        _logger.LogInformation("Updated security: {Ticker} (alias {Alias})", LogSanitizer.Sanitize(entity.TickerSymbol), securityAlias);
    }

    /// <inheritdoc />
    public async Task DeactivateAsync(int securityAlias)
    {
        var entity = await _context.SecurityMaster.FindAsync(securityAlias);
        if (entity == null)
        {
            _logger.LogWarning("Attempted to deactivate non-existent security alias: {Alias}", securityAlias);
            return;
        }

        entity.IsActive = false;
        entity.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        _logger.LogInformation("Deactivated security: {Ticker} (alias {Alias})", LogSanitizer.Sanitize(entity.TickerSymbol), securityAlias);
    }

    /// <inheritdoc />
    public async Task<int> UpsertManyAsync(IEnumerable<SecurityMasterCreateDto> securities)
    {
        var securityList = securities.ToList();
        var now = DateTime.UtcNow;
        var created = 0;
        var updated = 0;

        // Process in batches of 500, batch-fetch existing to avoid N+1
        foreach (var batch in securityList.Chunk(500))
        {
            var normalizedTickers = batch
                .Select(dto => dto.TickerSymbol.Trim().ToUpperInvariant())
                .Distinct()
                .ToList();

            // Single query per batch instead of one per dto
            var existingEntities = await _context.SecurityMaster
                .Where(s => normalizedTickers.Contains(s.TickerSymbol))
                .ToDictionaryAsync(s => s.TickerSymbol);

            foreach (var dto in batch)
            {
                var normalizedTicker = dto.TickerSymbol.Trim().ToUpperInvariant();

                if (existingEntities.TryGetValue(normalizedTicker, out var existing))
                {
                    // Update existing
                    existing.IssueName = dto.IssueName.Trim();
                    if (dto.PrimaryAssetId != null)
                        existing.PrimaryAssetId = dto.PrimaryAssetId.Trim();
                    if (dto.MicCode != null)
                        existing.MicCode = dto.MicCode.Trim();
                    if (dto.SecurityType != null)
                        existing.SecurityType = dto.SecurityType.Trim();
                    if (dto.Country != null)
                        existing.Country = dto.Country.Trim();
                    if (dto.Currency != null)
                        existing.Currency = dto.Currency.Trim();
                    if (dto.Isin != null)
                        existing.Isin = dto.Isin.Trim();
                    existing.IsActive = true;
                    existing.UpdatedAt = now;
                    updated++;
                }
                else
                {
                    // Insert new
                    _context.SecurityMaster.Add(new SecurityMasterEntity
                    {
                        TickerSymbol = normalizedTicker,
                        IssueName = dto.IssueName.Trim(),
                        PrimaryAssetId = dto.PrimaryAssetId?.Trim(),
                        // MicCode: populated by backfill phase (replaces Exchange field)
                        MicCode = null,
                        SecurityType = dto.SecurityType?.Trim(),
                        Country = dto.Country?.Trim(),
                        Currency = dto.Currency?.Trim(),
                        Isin = dto.Isin?.Trim(),
                        IsActive = true,
                        CreatedAt = now,
                        UpdatedAt = now
                    });
                    created++;
                }
            }
            await _context.SaveChangesAsync();
        }

        _logger.LogInformation("Upserted securities: {Created} created, {Updated} updated", created, updated);
        return created + updated;
    }

    /// <inheritdoc />
    public async Task<Dictionary<string, int>> GetActiveTickerAliasMapAsync()
    {
        // Projected query: only fetches TickerSymbol + SecurityAlias (2 columns)
        // instead of all 12+ columns from GetAllActiveAsync()
        var pairs = await _context.SecurityMaster
            .AsNoTracking()
            .Where(s => s.IsActive)
            .Select(s => new { s.TickerSymbol, s.SecurityAlias })
            .ToListAsync();

        return pairs.ToDictionary(p => p.TickerSymbol, p => p.SecurityAlias);
    }

    /// <inheritdoc />
    public async Task<int> GetActiveCountAsync()
    {
        return await _context.SecurityMaster.CountAsync(s => s.IsActive);
    }

    /// <inheritdoc />
    public async Task<List<SecurityMasterEntity>> SearchAsync(string query, int limit = 20, bool includeInactive = false)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new List<SecurityMasterEntity>();

        var normalizedQuery = query.Trim().ToUpperInvariant();

        var baseQuery = _context.SecurityMaster.AsNoTracking();

        if (!includeInactive)
        {
            baseQuery = baseQuery.Where(s => s.IsActive);
        }

        // Search by ticker prefix first, then name contains
        var results = await baseQuery
            .Where(s =>
                s.TickerSymbol.StartsWith(normalizedQuery) ||
                s.IssueName.ToUpper().Contains(normalizedQuery))
            .Take(limit * 5) // Bound server-side to prevent unbounded fetch of 55K+ entities
            .ToListAsync();

        // Rank: exact match > prefix match > contains match
        var ranked = results
            .Select(s => new
            {
                Entity = s,
                Rank = s.TickerSymbol == normalizedQuery ? 1 :
                       s.TickerSymbol.StartsWith(normalizedQuery) ? 2 : 3
            })
            .OrderBy(x => x.Rank)
            .ThenBy(x => x.Entity.TickerSymbol)
            .Take(limit)
            .Select(x => x.Entity)
            .ToList();

        return ranked;
    }
}
