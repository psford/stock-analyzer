using StockAnalyzer.Core.Data.Entities;

namespace StockAnalyzer.Core.Services;

/// <summary>
/// Repository interface for security master operations.
/// Manages the canonical list of securities tracked by the system.
/// </summary>
public interface ISecurityMasterRepository
{
    /// <summary>
    /// Get a security by its ticker symbol.
    /// </summary>
    /// <param name="ticker">The ticker symbol (e.g., "AAPL").</param>
    /// <returns>The security entity or null if not found.</returns>
    Task<SecurityMasterEntity?> GetByTickerAsync(string ticker);

    /// <summary>
    /// Get a security by its internal alias.
    /// </summary>
    /// <param name="securityAlias">The internal security alias (primary key).</param>
    /// <returns>The security entity or null if not found.</returns>
    Task<SecurityMasterEntity?> GetByAliasAsync(int securityAlias);

    /// <summary>
    /// Get all active securities.
    /// </summary>
    /// <returns>List of all securities where IsActive = true.</returns>
    Task<List<SecurityMasterEntity>> GetAllActiveAsync();

    /// <summary>
    /// Check if a ticker symbol exists in the security master.
    /// </summary>
    /// <param name="ticker">The ticker symbol to check.</param>
    /// <returns>True if the ticker exists, false otherwise.</returns>
    Task<bool> ExistsAsync(string ticker);

    /// <summary>
    /// Add a new security to the master list.
    /// </summary>
    /// <param name="dto">The security data to create.</param>
    /// <returns>The created security entity with assigned alias.</returns>
    Task<SecurityMasterEntity> CreateAsync(SecurityMasterCreateDto dto);

    /// <summary>
    /// Update an existing security.
    /// </summary>
    /// <param name="securityAlias">The security alias to update.</param>
    /// <param name="dto">The updated security data.</param>
    Task UpdateAsync(int securityAlias, SecurityMasterUpdateDto dto);

    /// <summary>
    /// Mark a security as inactive (soft delete).
    /// </summary>
    /// <param name="securityAlias">The security alias to deactivate.</param>
    Task DeactivateAsync(int securityAlias);

    /// <summary>
    /// Bulk upsert securities (insert or update if exists).
    /// Useful for batch loading from data providers.
    /// </summary>
    /// <param name="securities">Collection of securities to upsert.</param>
    /// <returns>Number of securities created or updated.</returns>
    Task<int> UpsertManyAsync(IEnumerable<SecurityMasterCreateDto> securities);

    /// <summary>
    /// Get count of active securities.
    /// </summary>
    /// <returns>Number of securities where IsActive = true.</returns>
    Task<int> GetActiveCountAsync();

    /// <summary>
    /// Search securities by name or ticker (partial match).
    /// </summary>
    /// <param name="query">Search query string.</param>
    /// <param name="limit">Maximum results to return.</param>
    /// <param name="includeInactive">Whether to include inactive securities.</param>
    /// <returns>List of matching securities.</returns>
    Task<List<SecurityMasterEntity>> SearchAsync(string query, int limit = 20, bool includeInactive = false);
}

/// <summary>
/// DTO for creating a new security in the security master.
/// </summary>
public record SecurityMasterCreateDto
{
    /// <summary>
    /// The ticker symbol (required, e.g., "AAPL").
    /// </summary>
    public required string TickerSymbol { get; init; }

    /// <summary>
    /// The full name of the security (required, e.g., "Apple Inc.").
    /// </summary>
    public required string IssueName { get; init; }

    /// <summary>
    /// Optional primary asset identifier (CUSIP, ISIN, etc.).
    /// </summary>
    public string? PrimaryAssetId { get; init; }

    /// <summary>
    /// Optional exchange where the security is traded.
    /// </summary>
    public string? Exchange { get; init; }

    /// <summary>
    /// Optional security type (e.g., "Common Stock", "ETF").
    /// </summary>
    public string? SecurityType { get; init; }
}

/// <summary>
/// DTO for updating an existing security in the security master.
/// All fields are optional - only provided fields will be updated.
/// </summary>
public record SecurityMasterUpdateDto
{
    /// <summary>
    /// Updated security name.
    /// </summary>
    public string? IssueName { get; init; }

    /// <summary>
    /// Updated primary asset identifier.
    /// </summary>
    public string? PrimaryAssetId { get; init; }

    /// <summary>
    /// Updated exchange.
    /// </summary>
    public string? Exchange { get; init; }

    /// <summary>
    /// Updated security type.
    /// </summary>
    public string? SecurityType { get; init; }

    /// <summary>
    /// Updated active status.
    /// </summary>
    public bool? IsActive { get; init; }
}
