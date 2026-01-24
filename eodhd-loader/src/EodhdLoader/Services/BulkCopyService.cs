using Microsoft.Data.SqlClient;

namespace EodhdLoader.Services;

/// <summary>
/// Handles bulk data migration from local SQL Server to Azure SQL.
/// Uses SqlBulkCopy for efficient batch transfers.
/// </summary>
public class BulkCopyService
{
    private readonly ConfigurationService _config;
    public int BatchSize { get; set; } = 5000;

    public BulkCopyService(ConfigurationService config)
    {
        _config = config;
    }

    public async Task<bool> TestConnectionAsync(string connectionString)
    {
        try
        {
            await using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<MigrationResult> MigrateSecurityMasterAsync(
        string targetConnectionString,
        IProgress<MigrationProgress>? progress = null,
        CancellationToken ct = default)
    {
        var result = new MigrationResult { TableName = "SecurityMaster" };
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            await using var sourceConn = new SqlConnection(_config.LocalConnectionString);
            await using var targetConn = new SqlConnection(targetConnectionString);

            await sourceConn.OpenAsync(ct);
            await targetConn.OpenAsync(ct);

            // Get source count
            await using (var countCmd = new SqlCommand("SELECT COUNT(*) FROM [data].[SecurityMaster]", sourceConn))
            {
                result.TotalRows = (int)(await countCmd.ExecuteScalarAsync(ct) ?? 0);
            }

            progress?.Report(new MigrationProgress
            {
                TableName = "SecurityMaster",
                TotalRows = result.TotalRows,
                RowsCopied = 0,
                Status = "Starting migration..."
            });

            // Read all data
            var query = @"SELECT SecurityAlias, TickerSymbol, IssueName, PrimaryAssetId,
                          Exchange, SecurityType, Country, Currency, Isin, IsActive,
                          CreatedAt, UpdatedAt FROM [data].[SecurityMaster]";

            await using var cmd = new SqlCommand(query, sourceConn);
            await using var reader = await cmd.ExecuteReaderAsync(ct);

            using var bulkCopy = new SqlBulkCopy(targetConn)
            {
                DestinationTableName = "[data].[SecurityMaster]",
                BatchSize = BatchSize,
                NotifyAfter = BatchSize
            };

            // Map columns
            bulkCopy.ColumnMappings.Add("SecurityAlias", "SecurityAlias");
            bulkCopy.ColumnMappings.Add("TickerSymbol", "TickerSymbol");
            bulkCopy.ColumnMappings.Add("IssueName", "IssueName");
            bulkCopy.ColumnMappings.Add("PrimaryAssetId", "PrimaryAssetId");
            bulkCopy.ColumnMappings.Add("Exchange", "Exchange");
            bulkCopy.ColumnMappings.Add("SecurityType", "SecurityType");
            bulkCopy.ColumnMappings.Add("Country", "Country");
            bulkCopy.ColumnMappings.Add("Currency", "Currency");
            bulkCopy.ColumnMappings.Add("Isin", "Isin");
            bulkCopy.ColumnMappings.Add("IsActive", "IsActive");
            bulkCopy.ColumnMappings.Add("CreatedAt", "CreatedAt");
            bulkCopy.ColumnMappings.Add("UpdatedAt", "UpdatedAt");

            bulkCopy.SqlRowsCopied += (s, e) =>
            {
                result.RowsCopied = e.RowsCopied;
                progress?.Report(new MigrationProgress
                {
                    TableName = "SecurityMaster",
                    TotalRows = result.TotalRows,
                    RowsCopied = e.RowsCopied,
                    Status = $"Copying... {e.RowsCopied:N0} / {result.TotalRows:N0}"
                });
            };

            await bulkCopy.WriteToServerAsync(reader, ct);

            result.RowsCopied = result.TotalRows; // Final count
            result.Success = true;
            result.Duration = stopwatch.Elapsed;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
            result.Duration = stopwatch.Elapsed;
        }

        return result;
    }

    public async Task<MigrationResult> MigratePricesAsync(
        string targetConnectionString,
        DateTime? fromDate = null,
        IProgress<MigrationProgress>? progress = null,
        CancellationToken ct = default)
    {
        var result = new MigrationResult { TableName = "Prices" };
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            await using var sourceConn = new SqlConnection(_config.LocalConnectionString);
            await using var targetConn = new SqlConnection(targetConnectionString);

            await sourceConn.OpenAsync(ct);
            await targetConn.OpenAsync(ct);

            // Get source count
            var countQuery = fromDate.HasValue
                ? "SELECT COUNT(*) FROM [data].[Prices] WHERE TradeDate >= @fromDate"
                : "SELECT COUNT(*) FROM [data].[Prices]";

            await using (var countCmd = new SqlCommand(countQuery, sourceConn))
            {
                if (fromDate.HasValue)
                    countCmd.Parameters.AddWithValue("@fromDate", fromDate.Value);
                result.TotalRows = (int)(await countCmd.ExecuteScalarAsync(ct) ?? 0);
            }

            progress?.Report(new MigrationProgress
            {
                TableName = "Prices",
                TotalRows = result.TotalRows,
                RowsCopied = 0,
                Status = "Starting migration..."
            });

            // Read data with optional date filter
            var query = fromDate.HasValue
                ? @"SELECT PriceId, SecurityAlias, TradeDate, OpenPrice, HighPrice, LowPrice,
                    ClosePrice, AdjustedClose, Volume, Source, CreatedAt
                    FROM [data].[Prices] WHERE TradeDate >= @fromDate"
                : @"SELECT PriceId, SecurityAlias, TradeDate, OpenPrice, HighPrice, LowPrice,
                    ClosePrice, AdjustedClose, Volume, Source, CreatedAt
                    FROM [data].[Prices]";

            await using var cmd = new SqlCommand(query, sourceConn);
            if (fromDate.HasValue)
                cmd.Parameters.AddWithValue("@fromDate", fromDate.Value);

            cmd.CommandTimeout = 600; // 10 minute timeout for large reads

            await using var reader = await cmd.ExecuteReaderAsync(ct);

            using var bulkCopy = new SqlBulkCopy(targetConn)
            {
                DestinationTableName = "[data].[Prices]",
                BatchSize = BatchSize,
                NotifyAfter = BatchSize,
                BulkCopyTimeout = 600
            };

            // Map columns
            bulkCopy.ColumnMappings.Add("PriceId", "PriceId");
            bulkCopy.ColumnMappings.Add("SecurityAlias", "SecurityAlias");
            bulkCopy.ColumnMappings.Add("TradeDate", "TradeDate");
            bulkCopy.ColumnMappings.Add("OpenPrice", "OpenPrice");
            bulkCopy.ColumnMappings.Add("HighPrice", "HighPrice");
            bulkCopy.ColumnMappings.Add("LowPrice", "LowPrice");
            bulkCopy.ColumnMappings.Add("ClosePrice", "ClosePrice");
            bulkCopy.ColumnMappings.Add("AdjustedClose", "AdjustedClose");
            bulkCopy.ColumnMappings.Add("Volume", "Volume");
            bulkCopy.ColumnMappings.Add("Source", "Source");
            bulkCopy.ColumnMappings.Add("CreatedAt", "CreatedAt");

            bulkCopy.SqlRowsCopied += (s, e) =>
            {
                result.RowsCopied = e.RowsCopied;
                var elapsed = stopwatch.Elapsed;
                var rate = elapsed.TotalSeconds > 0 ? e.RowsCopied / elapsed.TotalSeconds : 0;

                progress?.Report(new MigrationProgress
                {
                    TableName = "Prices",
                    TotalRows = result.TotalRows,
                    RowsCopied = e.RowsCopied,
                    Status = $"Copying... {e.RowsCopied:N0} / {result.TotalRows:N0} ({rate:N0} rows/sec)"
                });
            };

            await bulkCopy.WriteToServerAsync(reader, ct);

            result.RowsCopied = result.TotalRows;
            result.Success = true;
            result.Duration = stopwatch.Elapsed;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
            result.Duration = stopwatch.Elapsed;
        }

        return result;
    }
}

public class MigrationProgress
{
    public string TableName { get; set; } = string.Empty;
    public int TotalRows { get; set; }
    public long RowsCopied { get; set; }
    public string Status { get; set; } = string.Empty;
    public double PercentComplete => TotalRows > 0 ? (double)RowsCopied / TotalRows * 100 : 0;
}

public class MigrationResult
{
    public string TableName { get; set; } = string.Empty;
    public bool Success { get; set; }
    public int TotalRows { get; set; }
    public long RowsCopied { get; set; }
    public TimeSpan Duration { get; set; }
    public string? ErrorMessage { get; set; }
}
