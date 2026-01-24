using Microsoft.Data.SqlClient;

namespace EodhdLoader.Services;

/// <summary>
/// Service to sync data FROM production TO local database.
/// Pulls data via API and inserts into local SQL Server.
/// Complementary to BulkCopyService which pushes data TO production.
/// </summary>
public class ProdSyncService
{
    private readonly ConfigurationService _config;
    private readonly StockAnalyzerApiClient _apiClient;

    public event EventHandler<string>? LogMessage;

    public ProdSyncService(ConfigurationService config, StockAnalyzerApiClient apiClient)
    {
        _config = config;
        _apiClient = apiClient;
    }

    /// <summary>
    /// Gets a summary of what data is available on production.
    /// </summary>
    public async Task<ProdSyncStatus> GetProductionStatusAsync(CancellationToken ct = default)
    {
        var result = new ProdSyncStatus();

        try
        {
            _apiClient.CurrentEnvironment = TargetEnvironment.Production;
            var summary = await _apiClient.GetPriceSummaryAsync(ct);

            if (!summary.Success)
            {
                result.Error = summary.Error ?? "Failed to get production status";
                return result;
            }

            result.Success = true;
            result.HasData = summary.HasData;
            result.StartDate = summary.StartDate;
            result.EndDate = summary.EndDate;
            result.TotalPriceRecords = summary.TotalRecords;
            result.DistinctSecurities = summary.DistinctSecurities;
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
        }

        return result;
    }

    /// <summary>
    /// Gets a summary of what data is in the local database.
    /// </summary>
    public async Task<ProdSyncStatus> GetLocalStatusAsync(CancellationToken ct = default)
    {
        var result = new ProdSyncStatus();

        try
        {
            var connectionString = _config.LocalConnectionString;
            if (string.IsNullOrEmpty(connectionString))
            {
                result.Error = "Local connection string not configured";
                return result;
            }

            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(ct);

            // Get price data summary
            await using var cmd = new SqlCommand(@"
                SELECT
                    MIN(EffectiveDate) as MinDate,
                    MAX(EffectiveDate) as MaxDate,
                    COUNT(*) as TotalRecords,
                    COUNT(DISTINCT SecurityAlias) as DistinctSecurities
                FROM data.Prices", connection);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct) && !reader.IsDBNull(0))
            {
                result.Success = true;
                result.HasData = true;
                result.StartDate = reader.GetDateTime(0).ToString("yyyy-MM-dd");
                result.EndDate = reader.GetDateTime(1).ToString("yyyy-MM-dd");
                result.TotalPriceRecords = reader.GetInt32(2);
                result.DistinctSecurities = reader.GetInt32(3);
            }
            else
            {
                result.Success = true;
                result.HasData = false;
            }
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
        }

        return result;
    }

    /// <summary>
    /// Syncs securities from production to local database.
    /// </summary>
    public async Task<SyncResult> SyncSecuritiesAsync(
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var result = new SyncResult();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            progress?.Report("Fetching securities from production...");
            _apiClient.CurrentEnvironment = TargetEnvironment.Production;

            var exportResult = await _apiClient.ExportSecuritiesAsync(ct);
            if (!exportResult.Success)
            {
                result.Error = exportResult.Error ?? "Failed to export securities";
                return result;
            }

            Log($"Received {exportResult.Count:N0} securities from production");
            progress?.Report($"Received {exportResult.Count:N0} securities, inserting into local...");

            var connectionString = _config.LocalConnectionString;
            if (string.IsNullOrEmpty(connectionString))
            {
                result.Error = "Local connection string not configured";
                return result;
            }

            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(ct);

            int inserted = 0;
            int updated = 0;

            foreach (var security in exportResult.Securities)
            {
                ct.ThrowIfCancellationRequested();

                // Check if exists
                await using var checkCmd = new SqlCommand(
                    "SELECT SecurityAlias FROM data.SecurityMaster WHERE SecurityAlias = @alias",
                    connection);
                checkCmd.Parameters.AddWithValue("@alias", security.SecurityAlias);

                var exists = await checkCmd.ExecuteScalarAsync(ct) != null;

                if (exists)
                {
                    // Update existing
                    await using var updateCmd = new SqlCommand(@"
                        UPDATE data.SecurityMaster SET
                            TickerSymbol = @ticker,
                            IssueName = @name,
                            Exchange = @exchange,
                            SecurityType = @secType,
                            Country = @country,
                            Currency = @currency,
                            Isin = @isin,
                            IsActive = @isActive,
                            UpdatedAt = GETUTCDATE()
                        WHERE SecurityAlias = @alias", connection);

                    updateCmd.Parameters.AddWithValue("@alias", security.SecurityAlias);
                    updateCmd.Parameters.AddWithValue("@ticker", security.TickerSymbol);
                    updateCmd.Parameters.AddWithValue("@name", security.IssueName);
                    updateCmd.Parameters.AddWithValue("@exchange", (object?)security.Exchange ?? DBNull.Value);
                    updateCmd.Parameters.AddWithValue("@secType", (object?)security.SecurityType ?? DBNull.Value);
                    updateCmd.Parameters.AddWithValue("@country", (object?)security.Country ?? DBNull.Value);
                    updateCmd.Parameters.AddWithValue("@currency", (object?)security.Currency ?? DBNull.Value);
                    updateCmd.Parameters.AddWithValue("@isin", (object?)security.Isin ?? DBNull.Value);
                    updateCmd.Parameters.AddWithValue("@isActive", security.IsActive);

                    await updateCmd.ExecuteNonQueryAsync(ct);
                    updated++;
                }
                else
                {
                    // Insert with IDENTITY_INSERT
                    await using var insertCmd = new SqlCommand(@"
                        SET IDENTITY_INSERT data.SecurityMaster ON;
                        INSERT INTO data.SecurityMaster
                            (SecurityAlias, TickerSymbol, IssueName, Exchange, SecurityType,
                             Country, Currency, Isin, IsActive, CreatedAt, UpdatedAt)
                        VALUES
                            (@alias, @ticker, @name, @exchange, @secType,
                             @country, @currency, @isin, @isActive, GETUTCDATE(), GETUTCDATE());
                        SET IDENTITY_INSERT data.SecurityMaster OFF;", connection);

                    insertCmd.Parameters.AddWithValue("@alias", security.SecurityAlias);
                    insertCmd.Parameters.AddWithValue("@ticker", security.TickerSymbol);
                    insertCmd.Parameters.AddWithValue("@name", security.IssueName);
                    insertCmd.Parameters.AddWithValue("@exchange", (object?)security.Exchange ?? DBNull.Value);
                    insertCmd.Parameters.AddWithValue("@secType", (object?)security.SecurityType ?? DBNull.Value);
                    insertCmd.Parameters.AddWithValue("@country", (object?)security.Country ?? DBNull.Value);
                    insertCmd.Parameters.AddWithValue("@currency", (object?)security.Currency ?? DBNull.Value);
                    insertCmd.Parameters.AddWithValue("@isin", (object?)security.Isin ?? DBNull.Value);
                    insertCmd.Parameters.AddWithValue("@isActive", security.IsActive);

                    await insertCmd.ExecuteNonQueryAsync(ct);
                    inserted++;
                }

                if ((inserted + updated) % 100 == 0)
                {
                    progress?.Report($"Processing securities: {inserted + updated:N0} / {exportResult.Count:N0}");
                }
            }

            result.Success = true;
            result.Inserted = inserted;
            result.Updated = updated;
            result.Duration = stopwatch.Elapsed;
            result.Message = $"Synced {inserted:N0} new, {updated:N0} updated securities";

            Log(result.Message);
            progress?.Report(result.Message);
        }
        catch (OperationCanceledException)
        {
            result.Error = "Operation cancelled";
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
            Log($"Error syncing securities: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// Syncs prices from production to local database.
    /// </summary>
    public async Task<SyncResult> SyncPricesAsync(
        DateTime? startDate = null,
        DateTime? endDate = null,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var result = new SyncResult();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var connectionString = _config.LocalConnectionString;
            if (string.IsNullOrEmpty(connectionString))
            {
                result.Error = "Local connection string not configured";
                return result;
            }

            progress?.Report("Fetching prices from production...");
            _apiClient.CurrentEnvironment = TargetEnvironment.Production;

            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(ct);

            int totalInserted = 0;
            int totalSkipped = 0;
            int page = 1;
            bool hasMore = true;

            while (hasMore && !ct.IsCancellationRequested)
            {
                var exportResult = await _apiClient.ExportPricesAsync(startDate, endDate, page, 10000, ct);

                if (!exportResult.Success)
                {
                    result.Error = exportResult.Error ?? "Failed to export prices";
                    return result;
                }

                if (page == 1)
                {
                    Log($"Total prices available: {exportResult.TotalCount:N0} ({exportResult.TotalPages} pages)");
                }

                progress?.Report($"Processing page {page}/{exportResult.TotalPages} ({exportResult.Count:N0} prices)...");

                // Batch insert prices
                foreach (var priceBatch in exportResult.Prices.Chunk(1000))
                {
                    ct.ThrowIfCancellationRequested();

                    foreach (var price in priceBatch)
                    {
                        if (!DateTime.TryParse(price.EffectiveDate, out var effectiveDate))
                            continue;

                        // Check if exists
                        await using var checkCmd = new SqlCommand(@"
                            SELECT 1 FROM data.Prices
                            WHERE SecurityAlias = @alias AND EffectiveDate = @date",
                            connection);
                        checkCmd.Parameters.AddWithValue("@alias", price.SecurityAlias);
                        checkCmd.Parameters.AddWithValue("@date", effectiveDate);

                        if (await checkCmd.ExecuteScalarAsync(ct) != null)
                        {
                            totalSkipped++;
                            continue;
                        }

                        // Insert
                        await using var insertCmd = new SqlCommand(@"
                            INSERT INTO data.Prices
                                (SecurityAlias, EffectiveDate, [Open], High, Low, [Close], Volume, AdjustedClose, CreatedAt)
                            VALUES
                                (@alias, @date, @open, @high, @low, @close, @volume, @adjClose, GETUTCDATE())",
                            connection);

                        insertCmd.Parameters.AddWithValue("@alias", price.SecurityAlias);
                        insertCmd.Parameters.AddWithValue("@date", effectiveDate);
                        insertCmd.Parameters.AddWithValue("@open", price.Open);
                        insertCmd.Parameters.AddWithValue("@high", price.High);
                        insertCmd.Parameters.AddWithValue("@low", price.Low);
                        insertCmd.Parameters.AddWithValue("@close", price.Close);
                        insertCmd.Parameters.AddWithValue("@volume", (object?)price.Volume ?? DBNull.Value);
                        insertCmd.Parameters.AddWithValue("@adjClose", (object?)price.AdjustedClose ?? DBNull.Value);

                        await insertCmd.ExecuteNonQueryAsync(ct);
                        totalInserted++;
                    }

                    if (totalInserted % 5000 == 0)
                    {
                        progress?.Report($"Inserted {totalInserted:N0} prices (skipped {totalSkipped:N0} existing)...");
                    }
                }

                hasMore = exportResult.HasMore;
                page++;
            }

            result.Success = true;
            result.Inserted = totalInserted;
            result.Skipped = totalSkipped;
            result.Duration = stopwatch.Elapsed;
            result.Message = $"Synced {totalInserted:N0} prices (skipped {totalSkipped:N0} existing) in {result.Duration.TotalSeconds:F1}s";

            Log(result.Message);
            progress?.Report(result.Message);
        }
        catch (OperationCanceledException)
        {
            result.Error = "Operation cancelled";
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
            Log($"Error syncing prices: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// Performs a full sync: securities first, then prices.
    /// </summary>
    public async Task<FullSyncResult> FullSyncAsync(
        DateTime? startDate = null,
        DateTime? endDate = null,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var result = new FullSyncResult();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            // Sync securities first
            progress?.Report("Step 1/2: Syncing securities...");
            result.SecuritiesResult = await SyncSecuritiesAsync(progress, ct);

            if (!result.SecuritiesResult.Success)
            {
                result.Error = $"Securities sync failed: {result.SecuritiesResult.Error}";
                return result;
            }

            // Then sync prices
            progress?.Report("Step 2/2: Syncing prices...");
            result.PricesResult = await SyncPricesAsync(startDate, endDate, progress, ct);

            if (!result.PricesResult.Success)
            {
                result.Error = $"Prices sync failed: {result.PricesResult.Error}";
                return result;
            }

            result.Success = true;
            result.Duration = stopwatch.Elapsed;
            result.Message = $"Full sync complete: {result.SecuritiesResult.Inserted + result.SecuritiesResult.Updated:N0} securities, {result.PricesResult.Inserted:N0} new prices";

            progress?.Report(result.Message);
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
        }

        return result;
    }

    private void Log(string message)
    {
        LogMessage?.Invoke(this, message);
    }
}

public class ProdSyncStatus
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public bool HasData { get; set; }
    public string StartDate { get; set; } = "";
    public string EndDate { get; set; } = "";
    public int TotalPriceRecords { get; set; }
    public int DistinctSecurities { get; set; }
}

public class SyncResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? Message { get; set; }
    public int Inserted { get; set; }
    public int Updated { get; set; }
    public int Skipped { get; set; }
    public TimeSpan Duration { get; set; }
}

public class FullSyncResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? Message { get; set; }
    public SyncResult? SecuritiesResult { get; set; }
    public SyncResult? PricesResult { get; set; }
    public TimeSpan Duration { get; set; }
}
