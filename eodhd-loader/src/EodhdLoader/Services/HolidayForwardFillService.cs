using Microsoft.Data.SqlClient;

namespace EodhdLoader.Services;

/// <summary>
/// Service to forward-fill price data for US market holidays.
/// On holidays, markets are closed so we copy the prior trading day's close price.
/// Uses direct SQL for local database, API for production.
/// </summary>
public class HolidayForwardFillService
{
    private readonly ConfigurationService _config;
    private readonly StockAnalyzerApiClient _apiClient;

    public event EventHandler<string>? LogMessage;

    public HolidayForwardFillService(ConfigurationService config, StockAnalyzerApiClient apiClient)
    {
        _config = config;
        _apiClient = apiClient;
    }

    /// <summary>
    /// Analyzes the database to find holidays missing price data.
    /// </summary>
    public async Task<HolidayAnalysisResult> AnalyzeHolidaysAsync(TargetEnvironment environment, IProgress<string>? progress = null)
    {
        // Use API for production, direct SQL for local
        if (environment == TargetEnvironment.Production)
        {
            return await AnalyzeHolidaysViaApiAsync(progress);
        }

        return await AnalyzeHolidaysViaSqlAsync(environment, progress);
    }

    private async Task<HolidayAnalysisResult> AnalyzeHolidaysViaApiAsync(IProgress<string>? progress)
    {
        var result = new HolidayAnalysisResult();

        try
        {
            progress?.Report("Calling API to analyze holidays...");
            _apiClient.CurrentEnvironment = TargetEnvironment.Production;

            var apiResult = await _apiClient.AnalyzeHolidaysAsync();

            if (!apiResult.Success)
            {
                result.Error = apiResult.Error ?? "API call failed";
                return result;
            }

            // Convert API result to our result type
            if (DateTime.TryParse(apiResult.DataStartDate, out var startDate))
                result.DataStartDate = DateOnly.FromDateTime(startDate);
            if (DateTime.TryParse(apiResult.DataEndDate, out var endDate))
                result.DataEndDate = DateOnly.FromDateTime(endDate);

            result.TotalDatesWithData = apiResult.TotalDatesWithData;

            foreach (var h in apiResult.MissingHolidays)
            {
                if (DateTime.TryParse(h.Date, out var holidayDate) &&
                    DateTime.TryParse(h.PriorTradingDay, out var priorDate))
                {
                    result.MissingHolidays.Add(new MissingHolidayInfo
                    {
                        Holiday = new MarketHoliday(h.Name, DateOnly.FromDateTime(holidayDate)),
                        PriorTradingDay = DateOnly.FromDateTime(priorDate),
                        HasPriorDayData = h.HasPriorData
                    });
                }
            }

            result.Success = true;
            progress?.Report($"Found {result.MissingHolidays.Count} holidays needing forward-fill");
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
        }

        return result;
    }

    private async Task<HolidayAnalysisResult> AnalyzeHolidaysViaSqlAsync(TargetEnvironment environment, IProgress<string>? progress)
    {
        var result = new HolidayAnalysisResult();

        try
        {
            var connectionString = environment == TargetEnvironment.Local
                ? _config.LocalConnectionString
                : _config.ProductionConnectionString;

            if (string.IsNullOrEmpty(connectionString))
            {
                result.Error = $"No connection string configured for {environment}";
                return result;
            }

            progress?.Report("Connecting to database...");

            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();

            // Get date range and existing dates
            progress?.Report("Fetching existing price dates...");

            var existingDates = new HashSet<DateOnly>();
            DateOnly minDate, maxDate;

            await using (var cmd = new SqlCommand(@"
                SELECT MIN(EffectiveDate), MAX(EffectiveDate) FROM data.Prices", connection))
            {
                await using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    minDate = DateOnly.FromDateTime(reader.GetDateTime(0));
                    maxDate = DateOnly.FromDateTime(reader.GetDateTime(1));
                }
                else
                {
                    result.Error = "No price data found in database";
                    return result;
                }
            }

            await using (var cmd = new SqlCommand(@"
                SELECT DISTINCT EffectiveDate FROM data.Prices ORDER BY EffectiveDate", connection))
            {
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    existingDates.Add(DateOnly.FromDateTime(reader.GetDateTime(0)));
                }
            }

            result.DataStartDate = minDate;
            result.DataEndDate = maxDate;
            result.TotalDatesWithData = existingDates.Count;

            // Find holidays in range that are missing data
            progress?.Report("Analyzing holidays...");

            var holidays = UsMarketCalendar.GetHolidaysBetween(minDate, maxDate).ToList();

            foreach (var holiday in holidays)
            {
                // Only consider holidays that fall on weekdays (markets would normally be open)
                if (!holiday.IsWeekday) continue;

                // Check if we have data for this holiday
                if (existingDates.Contains(holiday.Date)) continue;

                // Find prior trading day
                var priorDay = UsMarketCalendar.GetPreviousTradingDay(holiday.Date);

                // Check if prior day has data
                var hasPriorData = existingDates.Contains(priorDay);

                result.MissingHolidays.Add(new MissingHolidayInfo
                {
                    Holiday = holiday,
                    PriorTradingDay = priorDay,
                    HasPriorDayData = hasPriorData
                });
            }

            result.Success = true;
            progress?.Report($"Found {result.MissingHolidays.Count} holidays needing forward-fill");
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
        }

        return result;
    }

    /// <summary>
    /// Forward-fills price data for all holidays missing data.
    /// Copies prior day's close price as OHLC with volume=0.
    /// </summary>
    public async Task<ForwardFillResult> ForwardFillHolidaysAsync(
        TargetEnvironment environment,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // Use API for production, direct SQL for local
        if (environment == TargetEnvironment.Production)
        {
            return await ForwardFillHolidaysViaApiAsync(progress, cancellationToken);
        }

        return await ForwardFillHolidaysViaSqlAsync(environment, progress, cancellationToken);
    }

    private async Task<ForwardFillResult> ForwardFillHolidaysViaApiAsync(
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var result = new ForwardFillResult();

        try
        {
            progress?.Report("Calling API to forward-fill holidays...");
            _apiClient.CurrentEnvironment = TargetEnvironment.Production;

            var apiResult = await _apiClient.ForwardFillHolidaysAsync(cancellationToken);

            if (!apiResult.Success)
            {
                result.Error = apiResult.Error ?? "API call failed";
                return result;
            }

            result.Success = true;
            result.Message = apiResult.Message;
            result.HolidaysProcessed = apiResult.HolidaysProcessed;
            result.TotalRecordsInserted = apiResult.TotalRecordsInserted;

            // Convert API result holidays to our format
            foreach (var h in apiResult.HolidaysFilled)
            {
                if (DateTime.TryParse(h.Date, out var holidayDate))
                {
                    result.HolidaysFilled.Add((
                        new MarketHoliday(h.Name, DateOnly.FromDateTime(holidayDate)),
                        h.RecordsInserted));
                }
            }

            progress?.Report(result.Message ?? "Forward-fill complete");
        }
        catch (OperationCanceledException)
        {
            result.Error = "Operation cancelled";
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
            Log($"Error: {ex.Message}");
        }

        return result;
    }

    private async Task<ForwardFillResult> ForwardFillHolidaysViaSqlAsync(
        TargetEnvironment environment,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var result = new ForwardFillResult();

        try
        {
            // First analyze to find missing holidays
            var analysis = await AnalyzeHolidaysViaSqlAsync(environment, progress);
            if (!analysis.Success)
            {
                result.Error = analysis.Error;
                return result;
            }

            var toFill = analysis.MissingHolidays.Where(h => h.HasPriorDayData).ToList();
            if (toFill.Count == 0)
            {
                result.Success = true;
                result.Message = "No holidays need forward-fill (all prior days have data or holidays already filled)";
                return result;
            }

            var connectionString = environment == TargetEnvironment.Local
                ? _config.LocalConnectionString
                : _config.ProductionConnectionString;

            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);

            int totalInserted = 0;
            int holidaysProcessed = 0;

            foreach (var missing in toFill)
            {
                cancellationToken.ThrowIfCancellationRequested();

                progress?.Report($"Filling {missing.Holiday.Name} ({missing.Holiday.Date:yyyy-MM-dd})...");
                Log($"Forward-filling {missing.Holiday.Name} ({missing.Holiday.Date:yyyy-MM-dd}) from {missing.PriorTradingDay:yyyy-MM-dd}");

                // Insert records: copy Close as OHLC, Volume=0
                var sql = @"
                    INSERT INTO data.Prices (SecurityAlias, EffectiveDate, [Open], High, Low, [Close], Volume, AdjustedClose)
                    SELECT
                        SecurityAlias,
                        @HolidayDate,
                        [Close], [Close], [Close], [Close],
                        0,
                        AdjustedClose
                    FROM data.Prices
                    WHERE EffectiveDate = @PriorDate
                      AND NOT EXISTS (
                          SELECT 1 FROM data.Prices p2
                          WHERE p2.SecurityAlias = data.Prices.SecurityAlias
                            AND p2.EffectiveDate = @HolidayDate
                      )";

                await using var cmd = new SqlCommand(sql, connection);
                cmd.Parameters.AddWithValue("@HolidayDate", missing.Holiday.Date.ToDateTime(TimeOnly.MinValue));
                cmd.Parameters.AddWithValue("@PriorDate", missing.PriorTradingDay.ToDateTime(TimeOnly.MinValue));
                cmd.CommandTimeout = 120; // 2 minutes for large inserts

                var inserted = await cmd.ExecuteNonQueryAsync(cancellationToken);
                totalInserted += inserted;
                holidaysProcessed++;

                Log($"  Inserted {inserted:N0} records");
                result.HolidaysFilled.Add((missing.Holiday, inserted));
            }

            result.Success = true;
            result.TotalRecordsInserted = totalInserted;
            result.HolidaysProcessed = holidaysProcessed;
            result.Message = $"Forward-filled {holidaysProcessed} holidays with {totalInserted:N0} total records";

            progress?.Report(result.Message);
        }
        catch (OperationCanceledException)
        {
            result.Error = "Operation cancelled";
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
            Log($"Error: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// Generates SQL script for forward-filling holidays (for manual execution).
    /// </summary>
    public async Task<string> GenerateSqlScriptAsync(TargetEnvironment environment, IProgress<string>? progress = null)
    {
        var analysis = await AnalyzeHolidaysAsync(environment, progress);
        if (!analysis.Success)
        {
            return $"-- Error: {analysis.Error}";
        }

        var toFill = analysis.MissingHolidays.Where(h => h.HasPriorDayData).ToList();
        if (toFill.Count == 0)
        {
            return "-- No holidays need forward-fill";
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("-- US Market Holiday Forward-Fill Script");
        sb.AppendLine($"-- Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"-- Database: {environment}");
        sb.AppendLine($"-- Holidays to fill: {toFill.Count}");
        sb.AppendLine();
        sb.AppendLine("-- This script copies prior trading day's Close price as OHLC for market holidays.");
        sb.AppendLine("-- Volume is set to 0 (no trading occurred).");
        sb.AppendLine();
        sb.AppendLine("BEGIN TRANSACTION;");
        sb.AppendLine();

        foreach (var missing in toFill.OrderBy(h => h.Holiday.Date))
        {
            sb.AppendLine($"-- {missing.Holiday.Name} {missing.Holiday.Date.Year}");
            sb.AppendLine($"INSERT INTO data.Prices (SecurityAlias, EffectiveDate, [Open], High, Low, [Close], Volume, AdjustedClose)");
            sb.AppendLine($"SELECT SecurityAlias, '{missing.Holiday.Date:yyyy-MM-dd}', [Close], [Close], [Close], [Close], 0, AdjustedClose");
            sb.AppendLine($"FROM data.Prices");
            sb.AppendLine($"WHERE EffectiveDate = '{missing.PriorTradingDay:yyyy-MM-dd}'");
            sb.AppendLine($"  AND NOT EXISTS (SELECT 1 FROM data.Prices p2 WHERE p2.SecurityAlias = data.Prices.SecurityAlias AND p2.EffectiveDate = '{missing.Holiday.Date:yyyy-MM-dd}');");
            sb.AppendLine();
        }

        sb.AppendLine("-- Verify counts before committing");
        sb.AppendLine("SELECT COUNT(*) AS TotalRecords FROM data.Prices;");
        sb.AppendLine();
        sb.AppendLine("-- COMMIT TRANSACTION;  -- Uncomment to commit");
        sb.AppendLine("-- ROLLBACK TRANSACTION;  -- Uncomment to rollback");

        return sb.ToString();
    }

    private void Log(string message)
    {
        LogMessage?.Invoke(this, message);
    }
}

public class HolidayAnalysisResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public DateOnly DataStartDate { get; set; }
    public DateOnly DataEndDate { get; set; }
    public int TotalDatesWithData { get; set; }
    public List<MissingHolidayInfo> MissingHolidays { get; } = new();

    public int HolidaysWithPriorData => MissingHolidays.Count(h => h.HasPriorDayData);
    public int HolidaysWithoutPriorData => MissingHolidays.Count(h => !h.HasPriorDayData);
}

public class MissingHolidayInfo
{
    public required MarketHoliday Holiday { get; init; }
    public DateOnly PriorTradingDay { get; init; }
    public bool HasPriorDayData { get; init; }
}

public class ForwardFillResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? Message { get; set; }
    public int HolidaysProcessed { get; set; }
    public int TotalRecordsInserted { get; set; }
    public List<(MarketHoliday Holiday, int RecordsInserted)> HolidaysFilled { get; } = new();
}
