namespace EodhdLoader.Services;

using System;
using System.IO;
using System.Net.Http;
using System.Security;
using System.Text.Json;
using EodhdLoader.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StockAnalyzer.Core.Data;
using StockAnalyzer.Core.Data.Entities;

/// <summary>
/// Implements IISharesConstituentService with JSON download, parsing, and database persistence.
/// </summary>
public class ISharesConstituentService : IISharesConstituentService
{
    private readonly HttpClient _httpClient;
    private readonly StockAnalyzerDbContext _dbContext;
    private readonly Dictionary<string, EtfConfig> _etfConfigs;

    /// <summary>
    /// Rate limiting constant: minimum milliseconds between consecutive iShares API requests.
    /// Public so CrawlerViewModel and other consumers can reference it instead of hardcoding.
    /// </summary>
    public const int RequestDelayMs = 2000;

    /// <summary>
    /// iShares source ID (from seed_index_attribution.sql).
    /// </summary>
    private const int ISharesSourceId = 10;

    /// <summary>
    /// User-Agent header for iShares API requests (academic research, rate-limited).
    /// </summary>
    private const string UserAgent = "StockAnalyzer/1.0 (academic-research; single-concurrency; 2s-gap)";

    public event Action<string>? LogMessage;
    public event Action<IngestProgress>? ProgressUpdated;

    /// <summary>
    /// Creates a new ISharesConstituentService instance.
    /// </summary>
    /// <param name="httpClient">Typed HttpClient injected by DI with AddHttpClient<></param>
    /// <param name="dbContext">EF Core database context</param>
    public ISharesConstituentService(HttpClient httpClient, StockAnalyzerDbContext dbContext)
    {
        _httpClient = httpClient;
        _httpClient.Timeout = TimeSpan.FromSeconds(60); // AC1.4: 60-second timeout
        // Set User-Agent header on the client instance for all requests
        _httpClient.DefaultRequestHeaders.Add("User-Agent", UserAgent);
        _dbContext = dbContext;

        // Load ETF configs from bundled JSON resource
        _etfConfigs = LoadEtfConfigs();
    }

    /// <summary>
    /// All configured ETF tickers and their metadata.
    /// </summary>
    public IReadOnlyDictionary<string, EtfConfig> EtfConfigs => _etfConfigs.AsReadOnly();

    /// <summary>
    /// Downloads and parses holdings for a single ETF, persists to database.
    /// Implements full orchestration: 3-level matching, SCD Type 2 identifier upsert, idempotent constituent insert, error isolation per holding.
    /// </summary>
    public async Task<IngestStats> IngestEtfAsync(string etfTicker, DateTime? asOfDate = null, CancellationToken ct = default)
    {
        // Look up EtfConfig
        if (!_etfConfigs.TryGetValue(etfTicker.ToUpperInvariant(), out var config))
        {
            LogMessage?.Invoke($"Unknown ETF: {etfTicker}");
            return new IngestStats(Parsed: 0, Matched: 0, Created: 0, Inserted: 0, SkippedExisting: 0, Failed: 0, IdentifiersSet: 0);
        }

        // Adjust date to last business day if weekend
        var requestDate = asOfDate?.Date ?? DateTime.UtcNow.Date;
        var adjustedDate = AdjustToLastBusinessDay(requestDate);
        var effectiveDate = adjustedDate;

        // Download JSON
        var data = await DownloadAsync(etfTicker, adjustedDate, ct);
        if (data == null || data.Value.ValueKind == JsonValueKind.Undefined)
        {
            return new IngestStats(Parsed: 0, Matched: 0, Created: 0, Inserted: 0, SkippedExisting: 0, Failed: 0, IdentifiersSet: 0);
        }

        // Parse holdings
        var (holdings, skippedRows) = ParseHoldings(data.Value);
        if (skippedRows > 0)
        {
            LogMessage?.Invoke($"{etfTicker}: {skippedRows} rows skipped due to parse errors");
        }
        if (holdings.Count == 0)
        {
            LogMessage?.Invoke($"{etfTicker}: No equity holdings found");
            return new IngestStats(Parsed: holdings.Count, Matched: 0, Created: 0, Inserted: 0, SkippedExisting: 0, Failed: 0, IdentifiersSet: 0);
        }

        // Look up IndexId
        var indexId = await _dbContext.IndexDefinitions
            .AsNoTracking()
            .Where(i => i.IndexCode == config.IndexCode)
            .Select(i => i.IndexId)
            .FirstOrDefaultAsync(ct);

        if (indexId == 0)
        {
            LogMessage?.Invoke($"IndexDefinition not found for {config.IndexCode}");
            return new IngestStats(Parsed: holdings.Count, Matched: 0, Created: 0, Inserted: 0, SkippedExisting: 0, Failed: 0, IdentifiersSet: 0);
        }

        var stats = new IngestStats(Parsed: holdings.Count, Matched: 0, Created: 0, Inserted: 0, SkippedExisting: 0, Failed: 0, IdentifiersSet: 0);

        // Process each holding
        foreach (var holding in holdings)
        {
            try
            {
                // 3-level security matching
                var securityResult = await MatchOrCreateSecurityAsync(holding, ct);
                if (securityResult.SecurityAlias == 0)
                {
                    stats = stats with { Failed = stats.Failed + 1 };
                    LogMessage?.Invoke($"{holding.Ticker}: Failed to match/create security");
                    continue;
                }

                if (securityResult.IsNew)
                    stats = stats with { Created = stats.Created + 1 };
                else
                    stats = stats with { Matched = stats.Matched + 1 };

                // Upsert identifiers with SCD Type 2
                int identifiersSet = await UpsertSecurityIdentifiersAsync(securityResult.SecurityAlias, holding, ct);
                stats = stats with { IdentifiersSet = stats.IdentifiersSet + identifiersSet };

                // Check if constituent already exists (idempotent)
                var exists = await _dbContext.IndexConstituents
                    .AsNoTracking()
                    .AnyAsync(c => c.IndexId == indexId &&
                                   c.SecurityAlias == securityResult.SecurityAlias &&
                                   c.EffectiveDate == effectiveDate &&
                                   c.SourceId == ISharesSourceId, ct);

                if (exists)
                {
                    stats = stats with { SkippedExisting = stats.SkippedExisting + 1 };
                    continue;
                }

                // Insert constituent
                var constituent = new IndexConstituentEntity
                {
                    IndexId = indexId,
                    SecurityAlias = securityResult.SecurityAlias,
                    EffectiveDate = effectiveDate,
                    Weight = holding.Weight,
                    MarketValue = holding.MarketValue,
                    Shares = holding.Shares,
                    Sector = holding.Sector,
                    Location = holding.Location,
                    Currency = holding.Currency,
                    SourceId = ISharesSourceId,
                    SourceTicker = holding.Ticker
                };

                _dbContext.IndexConstituents.Add(constituent);
                await _dbContext.SaveChangesAsync(ct);
                stats = stats with { Inserted = stats.Inserted + 1 };
            }
            catch (Exception ex)
            {
                // Error isolation: log and continue (AC3.6)
                LogMessage?.Invoke($"{holding.Ticker}: Error during ingest — {ex.Message}");
                stats = stats with { Failed = stats.Failed + 1 };
            }
        }

        LogMessage?.Invoke($"{etfTicker} as of {effectiveDate:yyyy-MM-dd}: {stats.Parsed} parsed, {stats.Matched} matched, {stats.Created} created, {stats.Inserted} inserted, {stats.SkippedExisting} skipped, {stats.Failed} failed");

        return stats;
    }

    /// <summary>
    /// Loads all configured ETFs with rate limiting.
    /// </summary>
    public async Task IngestAllEtfsAsync(DateTime? asOfDate = null, CancellationToken ct = default)
    {
        var etfTickers = EtfConfigs.Keys.ToList();
        int current = 0;

        foreach (var ticker in etfTickers)
        {
            ct.ThrowIfCancellationRequested();
            current++;

            try
            {
                var stats = await IngestEtfAsync(ticker, asOfDate, ct);
                ProgressUpdated?.Invoke(new IngestProgress(ticker, current, etfTickers.Count, 0, 0, stats));
                LogMessage?.Invoke($"{ticker}: {stats.Inserted} inserted, {stats.SkippedExisting} skipped, {stats.Failed} failed");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogMessage?.Invoke($"{ticker}: FAILED — {ex.Message}");
                ProgressUpdated?.Invoke(new IngestProgress(ticker, current, etfTickers.Count, 0, 0,
                    new IngestStats(0, 0, 0, 0, 0, 1, 0)));
            }

            // Rate limiting — minimum 2s between iShares requests
            if (current < etfTickers.Count)
                await Task.Delay(RequestDelayMs, ct);
        }
    }

    /// <summary>
    /// Returns ETFs with stale constituent data (missing latest month-end).
    /// </summary>
    public async Task<IReadOnlyList<(string EtfTicker, string IndexCode)>> GetStaleEtfsAsync(CancellationToken ct = default)
    {
        // Find the latest date in IndexConstituent table
        var latestDate = await _dbContext.IndexConstituents
            .AsNoTracking()
            .MaxAsync(c => (DateTime?)c.EffectiveDate, ct);

        if (latestDate == null)
            return EtfConfigs.Select(kvp => (kvp.Key, kvp.Value.IndexCode)).ToList();

        var latestDateTime = (DateTime)latestDate;

        // Compute the last business day of the PREVIOUS month-end
        var today = DateTime.UtcNow.Date;
        var firstOfMonth = new DateTime(today.Year, today.Month, 1);
        var previousMonthEnd = firstOfMonth.AddDays(-1); // Last day of previous month
        var lastBusinessDay = AdjustToLastBusinessDay(previousMonthEnd);

        // If latest data is before last business day of previous month, consider stale
        if (latestDateTime.Date < lastBusinessDay)
        {
            return EtfConfigs.Select(kvp => (kvp.Key, kvp.Value.IndexCode)).ToList();
        }

        return new List<(string, string)>();
    }

    /// <summary>
    /// Matches an existing security or creates a new one (3-level matching: ticker, CUSIP, ISIN).
    /// </summary>
    private async Task<(int SecurityAlias, bool IsNew)> MatchOrCreateSecurityAsync(ISharesHolding holding, CancellationToken ct)
    {
        // Level 1: Ticker lookup
        var security = await _dbContext.SecurityMaster
            .FirstOrDefaultAsync(s => s.TickerSymbol == holding.Ticker, ct);

        if (security != null)
            return (security.SecurityAlias, false);

        // Level 2: CUSIP lookup
        if (holding.Cusip != null)
        {
            var identifier = await _dbContext.SecurityIdentifiers
                .FirstOrDefaultAsync(si => si.IdentifierType == "CUSIP" && si.IdentifierValue == holding.Cusip, ct);
            if (identifier != null)
            {
                var foundSecurity = await _dbContext.SecurityMaster.FindAsync(new object[] { identifier.SecurityAlias }, cancellationToken: ct);
                if (foundSecurity != null)
                    return (foundSecurity.SecurityAlias, false);
            }
        }

        // Level 3: ISIN lookup
        if (holding.Isin != null)
        {
            var identifier = await _dbContext.SecurityIdentifiers
                .FirstOrDefaultAsync(si => si.IdentifierType == "ISIN" && si.IdentifierValue == holding.Isin, ct);
            if (identifier != null)
            {
                var foundSecurity = await _dbContext.SecurityMaster.FindAsync(new object[] { identifier.SecurityAlias }, cancellationToken: ct);
                if (foundSecurity != null)
                    return (foundSecurity.SecurityAlias, false);
            }
        }

        // Create new security
        var newSecurity = new SecurityMasterEntity
        {
            PrimaryAssetId = holding.Cusip ?? holding.Isin,
            IssueName = holding.Name,
            TickerSymbol = holding.Ticker,
            Exchange = holding.Exchange,
            SecurityType = "Common Stock",
            Country = holding.Location,
            Currency = holding.Currency,
            Isin = holding.Isin,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _dbContext.SecurityMaster.Add(newSecurity);
        await _dbContext.SaveChangesAsync(ct);

        return (newSecurity.SecurityAlias, true);
    }

    /// <summary>
    /// Upserts security identifiers with SCD Type 2 history tracking.
    /// Returns the count of identifiers set/updated.
    /// </summary>
    private async Task<int> UpsertSecurityIdentifiersAsync(int securityAlias, ISharesHolding holding, CancellationToken ct)
    {
        int identifiersSet = 0;
        var identifiersToUpsert = new[]
        {
            ("CUSIP", holding.Cusip),
            ("ISIN", holding.Isin),
            ("SEDOL", holding.Sedol)
        };

        foreach (var (idType, idValue) in identifiersToUpsert)
        {
            if (string.IsNullOrWhiteSpace(idValue))
                continue;

            var existing = await _dbContext.SecurityIdentifiers
                .FirstOrDefaultAsync(si => si.SecurityAlias == securityAlias && si.IdentifierType == idType, ct);

            if (existing != null)
            {
                if (existing.IdentifierValue == idValue)
                    continue; // No change

                // Snapshot old value to history (SCD Type 2)
                var hist = new SecurityIdentifierHistEntity
                {
                    SecurityAlias = securityAlias,
                    IdentifierType = idType,
                    IdentifierValue = existing.IdentifierValue,
                    EffectiveFrom = existing.UpdatedAt.Date,
                    EffectiveTo = DateTime.UtcNow.Date,
                    SourceId = ISharesSourceId
                };
                _dbContext.SecurityIdentifierHistory.Add(hist);

                // Update current
                existing.IdentifierValue = idValue;
                existing.SourceId = ISharesSourceId;
                existing.UpdatedAt = DateTime.UtcNow;
                existing.UpdatedBy = "ishares-ingest";
                identifiersSet++;
            }
            else
            {
                // Insert new
                var newIdentifier = new SecurityIdentifierEntity
                {
                    SecurityAlias = securityAlias,
                    IdentifierType = idType,
                    IdentifierValue = idValue,
                    SourceId = ISharesSourceId,
                    UpdatedBy = "ishares-ingest",
                    UpdatedAt = DateTime.UtcNow
                };
                _dbContext.SecurityIdentifiers.Add(newIdentifier);
                identifiersSet++;
            }
        }

        await _dbContext.SaveChangesAsync(ct);
        return identifiersSet;
    }

    /// <summary>
    /// Downloads iShares holdings JSON for a given ETF and optional date.
    /// Handles BOM prefix, network errors, and unknown ETFs gracefully.
    /// Adjusts weekend dates to last business day.
    /// </summary>
    /// <param name="etfTicker">ETF ticker (e.g., "IVV")</param>
    /// <param name="asOfDate">Optional effective date; if null, uses current date</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Parsed JSON data or null on error</returns>
    internal async Task<JsonElement?> DownloadAsync(string etfTicker, DateTime? asOfDate = null, CancellationToken ct = default)
    {
        // AC1.3: Unknown ETF ticker returns null, no exception
        if (!_etfConfigs.TryGetValue(etfTicker.ToUpperInvariant(), out var config))
        {
            LogMessage?.Invoke($"Unknown ETF: {etfTicker}");
            return null;
        }

        // AC1.5: Adjust weekend dates to last business day
        var requestDate = asOfDate?.Date ?? DateTime.UtcNow.Date;
        var adjustedDate = AdjustToLastBusinessDay(requestDate);

        // Construct URL per iShares API format
        var url = $"https://www.ishares.com/us/products/{config.ProductId}/{config.Slug}/1467271812596.ajax?fileType=json&tab=all&asOfDate={adjustedDate:yyyyMMdd}";

        try
        {
            LogMessage?.Invoke($"Downloading {etfTicker} as of {adjustedDate:yyyy-MM-dd}");

            // Create request with User-Agent header
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("User-Agent", UserAgent);

            var response = await _httpClient.SendAsync(request, ct);

            // AC1.4: Network timeout or non-200 status returns null
            if (!response.IsSuccessStatusCode)
            {
                LogMessage?.Invoke($"Download failed: HTTP {response.StatusCode}");
                return null;
            }

            var text = await response.Content.ReadAsStringAsync(ct);

            // AC1.2: Strip UTF-8 BOM if present
            text = text.TrimStart('\uFEFF');

            // Parse JSON
            using var doc = JsonDocument.Parse(text);
            return doc.RootElement.Clone();
        }
        catch (TaskCanceledException)
        {
            // AC1.4: Timeout returns null, no exception propagated
            LogMessage?.Invoke($"Download timeout for {etfTicker}");
            return null;
        }
        catch (HttpRequestException ex)
        {
            // AC1.4: Network error returns null
            LogMessage?.Invoke($"Download failed: {ex.Message}");
            return null;
        }
        catch (JsonException ex)
        {
            // AC1.4: Malformed JSON returns null
            LogMessage?.Invoke($"Failed to parse JSON: {ex.Message}");
            return null;
        }
        catch (Exception ex)
        {
            // Catch-all for unexpected errors
            LogMessage?.Invoke($"Unexpected error downloading {etfTicker}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Loads ETF configurations from bundled ishares_etf_configs.json resource.
    /// Tries multiple paths: executing assembly, entry assembly, current directory.
    /// </summary>
    private Dictionary<string, EtfConfig> LoadEtfConfigs()
    {
        var configs = new Dictionary<string, EtfConfig>(StringComparer.OrdinalIgnoreCase);

        try
        {
            // Try multiple paths to find the config file
            var pathsToTry = new[]
            {
                // Path relative to executing assembly (ISharesConstituentService)
                () =>
                {
                    var assemblyDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                    return Path.Combine(assemblyDir ?? ".", "Resources", "ishares_etf_configs.json");
                },
                // Path relative to entry assembly (e.g., EodhdLoader.exe or test runner)
                () =>
                {
                    var assemblyDir = Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly()?.Location ?? "");
                    return Path.Combine(assemblyDir ?? ".", "Resources", "ishares_etf_configs.json");
                },
                // Path relative to current working directory
                () => Path.Combine(".", "Resources", "ishares_etf_configs.json"),
                // Absolute path up two levels (for running from bin\Debug or bin\Release)
                () => Path.Combine("..", "..", "src", "EodhdLoader", "Resources", "ishares_etf_configs.json")
            };

            string? foundPath = null;
            foreach (var pathFunc in pathsToTry)
            {
                try
                {
                    var path = pathFunc();
                    if (File.Exists(path))
                    {
                        foundPath = path;
                        break;
                    }
                }
                catch (IOException) { /* Path is invalid or file is inaccessible, try next */ }
                catch (SecurityException) { /* Insufficient permissions, try next */ }
                catch (UnauthorizedAccessException) { /* Access denied, try next */ }
            }

            if (foundPath != null && File.Exists(foundPath))
            {
                var json = File.ReadAllText(foundPath);
                // PropertyNameCaseInsensitive handles snake_case to PascalCase mapping
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var parsed = JsonSerializer.Deserialize<Dictionary<string, EtfConfig>>(json, options);

                if (parsed != null)
                {
                    foreach (var kvp in parsed)
                    {
                        configs[kvp.Key.ToUpperInvariant()] = kvp.Value;
                    }
                }
            }
            else
            {
                LogMessage?.Invoke($"Warning: ishares_etf_configs.json not found in any expected location");
            }
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke($"Error loading ETF configs: {ex.Message}");
        }

        return configs;
    }

    /// <summary>
    /// AC1.5: Adjusts weekend dates to last business day (Friday).
    /// </summary>
    private static DateTime AdjustToLastBusinessDay(DateTime date)
    {
        while (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            date = date.AddDays(-1);
        }
        return date;
    }

    /// <summary>
    /// AC2: Parses iShares JSON holdings data, auto-detecting Format A/B and filtering equity only.
    /// Internal so tests can access via InternalsVisibleTo.
    /// </summary>
    /// <param name="data">JSON element with aaData array</param>
    /// <returns>Tuple of (holdings list, count of rows skipped due to parse errors)</returns>
    internal static (List<ISharesHolding> Holdings, int SkippedRows) ParseHoldings(JsonElement data)
    {
        var holdings = new List<ISharesHolding>();
        int skippedRows = 0;

        // Extract aaData array from JSON
        if (!data.TryGetProperty("aaData", out var aaDataElement) || aaDataElement.ValueKind != JsonValueKind.Array)
        {
            return (holdings, 0); // AC2.4: Malformed JSON returns empty list
        }

        var rows = aaDataElement.EnumerateArray().ToList();
        if (rows.Count == 0)
        {
            return (holdings, 0);
        }

        // Detect format (Format A vs Format B)
        var isFormatB = DetectFormatB(rows[0]);
        var colMap = isFormatB ? GetFormatBColumns() : GetFormatAColumns();

        // Parse each row
        foreach (var row in rows)
        {
            if (row.ValueKind != JsonValueKind.Array)
                continue;

            var rowArray = row.EnumerateArray().ToList();

            // Skip rows that are too short
            if (rowArray.Count < 15)
                continue;

            // Get asset class and filter non-equity
            var assetClassValue = GetStringValue(rowArray, colMap["asset_class"]);
            if (IsNonEquityAssetClass(assetClassValue))
                continue;

            // Parse holding
            try
            {
                var holding = new ISharesHolding(
                    Ticker: GetStringValue(rowArray, colMap["ticker"]) ?? "",
                    Name: GetStringValue(rowArray, colMap["name"]) ?? "",
                    Sector: GetStringValue(rowArray, colMap["sector"]),
                    MarketValue: GetDecimalValue(rowArray, colMap["market_value"]),
                    Weight: GetWeightValue(rowArray, colMap["weight_pct"]),
                    Shares: GetDecimalValue(rowArray, colMap["quantity"]),
                    Location: GetStringValue(rowArray, colMap["location"]),
                    Exchange: GetStringValue(rowArray, colMap["exchange"]),
                    Currency: GetStringValue(rowArray, colMap["currency"]),
                    Cusip: CleanIdentifier(GetStringValue(rowArray, colMap["cusip"])),
                    Isin: CleanIdentifier(GetStringValue(rowArray, colMap["isin"])),
                    Sedol: CleanIdentifier(GetStringValue(rowArray, colMap["sedol"]))
                );

                holdings.Add(holding);
            }
            catch
            {
                // AC2.4: Skip malformed rows, track count for diagnostics, continue processing
                skippedRows++;
            }
        }

        return (holdings, skippedRows);
    }

    /// <summary>
    /// Detects if JSON is Format B (19 cols, col[4] is string) vs Format A (17 cols, col[4] is object).
    /// </summary>
    private static bool DetectFormatB(JsonElement firstRow)
    {
        if (firstRow.ValueKind != JsonValueKind.Array)
            return false;

        var rowArray = firstRow.EnumerateArray().ToList();

        // Format B: 19+ columns and col[4] is a string (asset class)
        // Format A: col[4] is an object (market value with {display, raw})
        if (rowArray.Count >= 19)
        {
            var col4 = rowArray[4];
            return col4.ValueKind == JsonValueKind.String;
        }

        return false;
    }

    /// <summary>
    /// Column indices for Format A (17 cols, broad ETFs like IVV, IWB).
    /// </summary>
    private static Dictionary<string, int> GetFormatAColumns()
    {
        return new Dictionary<string, int>
        {
            ["ticker"] = 0,
            ["name"] = 1,
            ["sector"] = 2,
            ["asset_class"] = 3,
            ["market_value"] = 4,
            ["weight_pct"] = 5,
            ["quantity"] = 7,
            ["cusip"] = 8,
            ["isin"] = 9,
            ["sedol"] = 10,
            ["price"] = 11,
            ["location"] = 12,
            ["exchange"] = 13,
            ["currency"] = 14,
        };
    }

    /// <summary>
    /// Column indices for Format B (19 cols, S&P style like IJH, IJK).
    /// </summary>
    private static Dictionary<string, int> GetFormatBColumns()
    {
        return new Dictionary<string, int>
        {
            ["ticker"] = 0,
            ["name"] = 1,
            ["sector"] = 3,
            ["asset_class"] = 4,
            ["market_value"] = 5,
            ["weight_pct"] = 17,
            ["quantity"] = 7,
            ["cusip"] = 8,
            ["isin"] = 9,
            ["sedol"] = 10,
            ["price"] = 11,
            ["location"] = 12,
            ["exchange"] = 13,
            ["currency"] = 14,
        };
    }

    /// <summary>
    /// Non-equity asset classes to filter out.
    /// </summary>
    private static readonly HashSet<string> NonEquityAssetClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Cash",
        "Cash Collateral and Margins",
        "Cash and/or Derivatives",
        "Futures",
        "Money Market",
    };

    /// <summary>
    /// Checks if asset class should be filtered (non-equity).
    /// Only filter known non-equity classes. If asset class is null/empty, include the row (return false).
    /// </summary>
    private static bool IsNonEquityAssetClass(string? assetClass)
    {
        if (string.IsNullOrWhiteSpace(assetClass))
            return false; // Don't filter rows with missing asset class

        return NonEquityAssetClasses.Contains(assetClass);
    }

    /// <summary>
    /// Extracts string value from JSON cell (direct string or from display property).
    /// </summary>
    private static string? GetStringValue(List<JsonElement> row, int colIndex)
    {
        if (colIndex < 0 || colIndex >= row.Count)
            return null;

        var element = row[colIndex];

        // If it's a dict with "display" property, try that first
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty("display", out var displayProp))
        {
            var val = displayProp.GetString();
            return string.IsNullOrWhiteSpace(val) ? null : val.Trim();
        }

        // Direct string
        if (element.ValueKind == JsonValueKind.String)
        {
            var val = element.GetString();
            return string.IsNullOrWhiteSpace(val) ? null : val.Trim();
        }

        return null;
    }

    /// <summary>
    /// Extracts decimal value from JSON cell (handles dicts with raw, strings with commas, nulls).
    /// </summary>
    private static decimal? GetDecimalValue(List<JsonElement> row, int colIndex)
    {
        if (colIndex < 0 || colIndex >= row.Count)
            return null;

        var element = row[colIndex];

        // If it's a dict with "raw" property, extract numeric value
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("raw", out var rawProp))
            {
                if (rawProp.ValueKind == JsonValueKind.Number)
                    return rawProp.GetDecimal();
                if (rawProp.ValueKind == JsonValueKind.String)
                {
                    var str = rawProp.GetString();
                    return ParseNumericString(str);
                }
                // Null raw property
                return null;
            }
        }

        // Direct numeric
        if (element.ValueKind == JsonValueKind.Number)
            return element.GetDecimal();

        // Direct string
        if (element.ValueKind == JsonValueKind.String)
        {
            var str = element.GetString();
            return ParseNumericString(str);
        }

        return null;
    }

    /// <summary>
    /// Extracts weight percentage and converts from percentage to decimal (e.g., 6.5 -> 0.065).
    /// </summary>
    private static decimal? GetWeightValue(List<JsonElement> row, int colIndex)
    {
        var pctValue = GetDecimalValue(row, colIndex);
        if (pctValue == null)
            return null;

        // Weight in source data is percentage (e.g., 6.5 = 6.5%), convert to decimal
        return pctValue / 100;
    }

    /// <summary>
    /// Parses numeric string, handling commas, hyphens, empty, and "N/A".
    /// </summary>
    private static decimal? ParseNumericString(string? str)
    {
        if (string.IsNullOrWhiteSpace(str))
            return null;

        var cleaned = str.Replace(",", "").Trim();

        if (cleaned == "-" || cleaned == "" || cleaned == "N/A")
            return null;

        if (decimal.TryParse(cleaned, System.Globalization.CultureInfo.InvariantCulture, out var result))
            return result;

        return null;
    }

    /// <summary>
    /// Cleans identifier value: strips whitespace, returns null for "-", "", "N/A".
    /// </summary>
    private static string? CleanIdentifier(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var cleaned = value.Trim();

        if (cleaned == "-" || cleaned == "" || cleaned == "N/A")
            return null;

        return cleaned;
    }
}
