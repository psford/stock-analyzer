using Microsoft.Data.SqlClient;

namespace EodhdLoader.Services;

/// <summary>
/// Analyzes existing data coverage in the price database.
/// </summary>
public class DataAnalysisService
{
    private readonly ConfigurationService _config;

    public DataAnalysisService(ConfigurationService config)
    {
        _config = config;
    }

    public async Task<DataCoverageStats> GetCoverageStatsAsync()
    {
        var stats = new DataCoverageStats();

        await using var conn = new SqlConnection(_config.LocalConnectionString);
        await conn.OpenAsync();

        // Get SecurityMaster stats
        var securityQuery = @"
            SELECT
                COUNT(*) AS TotalSecurities,
                SUM(CASE WHEN IsActive = 1 THEN 1 ELSE 0 END) AS ActiveSecurities,
                SUM(CASE WHEN Country IS NOT NULL THEN 1 ELSE 0 END) AS WithCountry,
                SUM(CASE WHEN Currency IS NOT NULL THEN 1 ELSE 0 END) AS WithCurrency,
                SUM(CASE WHEN Isin IS NOT NULL THEN 1 ELSE 0 END) AS WithIsin
            FROM [data].[SecurityMaster]";

        await using (var cmd = new SqlCommand(securityQuery, conn))
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            if (await reader.ReadAsync())
            {
                stats.TotalSecurities = reader.GetInt32(0);
                stats.ActiveSecurities = reader.GetInt32(1);
                stats.WithCountry = reader.GetInt32(2);
                stats.WithCurrency = reader.GetInt32(3);
                stats.WithIsin = reader.GetInt32(4);
            }
        }

        // Get Price stats
        var priceQuery = @"
            SELECT
                COUNT(*) AS TotalPrices,
                COUNT(DISTINCT SecurityAlias) AS SecuritiesWithPrices,
                MIN(EffectiveDate) AS EarliestDate,
                MAX(EffectiveDate) AS LatestDate
            FROM [data].[Prices]";

        await using (var cmd = new SqlCommand(priceQuery, conn))
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            if (await reader.ReadAsync())
            {
                stats.TotalPriceRecords = reader.GetInt32(0);
                stats.SecuritiesWithPrices = reader.GetInt32(1);
                stats.EarliestPriceDate = reader.IsDBNull(2) ? null : reader.GetDateTime(2);
                stats.LatestPriceDate = reader.IsDBNull(3) ? null : reader.GetDateTime(3);
            }
        }

        stats.SecuritiesWithoutPrices = stats.ActiveSecurities - stats.SecuritiesWithPrices;

        return stats;
    }

    public async Task<List<SecurityGap>> GetRecentGapsAsync(int limit = 20)
    {
        var gaps = new List<SecurityGap>();

        await using var conn = new SqlConnection(_config.LocalConnectionString);
        await conn.OpenAsync();

        // Find securities with price data that are missing recent dates
        var query = @"
            WITH LatestPrices AS (
                SELECT
                    p.SecurityAlias,
                    s.TickerSymbol,
                    s.IssueName,
                    MAX(p.EffectiveDate) AS LastPriceDate
                FROM [data].[Prices] p
                JOIN [data].[SecurityMaster] s ON p.SecurityAlias = s.SecurityAlias
                WHERE s.IsActive = 1
                GROUP BY p.SecurityAlias, s.TickerSymbol, s.IssueName
            )
            SELECT TOP (@limit)
                SecurityAlias,
                TickerSymbol,
                IssueName,
                LastPriceDate,
                DATEDIFF(DAY, LastPriceDate, GETDATE()) AS DaysMissing
            FROM LatestPrices
            WHERE LastPriceDate < DATEADD(DAY, -1, GETDATE())
            ORDER BY DaysMissing DESC";

        await using var cmd = new SqlCommand(query, conn);
        cmd.Parameters.AddWithValue("@limit", limit);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            gaps.Add(new SecurityGap
            {
                SecurityAlias = reader.GetInt32(0),
                Ticker = reader.GetString(1),
                Name = reader.GetString(2),
                LastPriceDate = reader.GetDateTime(3),
                DaysMissing = reader.GetInt32(4)
            });
        }

        return gaps;
    }

    public async Task<List<SecurityTypeCoverage>> GetCoverageByTypeAsync()
    {
        var coverage = new List<SecurityTypeCoverage>();

        await using var conn = new SqlConnection(_config.LocalConnectionString);
        await conn.OpenAsync();

        var query = @"
            SELECT
                ISNULL(s.SecurityType, 'Unknown') AS SecurityType,
                COUNT(DISTINCT s.SecurityAlias) AS TotalCount,
                COUNT(DISTINCT p.SecurityAlias) AS WithPrices
            FROM [data].[SecurityMaster] s
            LEFT JOIN [data].[Prices] p ON s.SecurityAlias = p.SecurityAlias
            WHERE s.IsActive = 1
            GROUP BY s.SecurityType
            ORDER BY TotalCount DESC";

        await using var cmd = new SqlCommand(query, conn);
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var type = new SecurityTypeCoverage
            {
                SecurityType = reader.GetString(0),
                TotalCount = reader.GetInt32(1),
                WithPrices = reader.GetInt32(2)
            };
            type.CoveragePercent = type.TotalCount > 0
                ? (double)type.WithPrices / type.TotalCount * 100
                : 0;
            coverage.Add(type);
        }

        return coverage;
    }
}

public class DataCoverageStats
{
    public int TotalSecurities { get; set; }
    public int ActiveSecurities { get; set; }
    public int WithCountry { get; set; }
    public int WithCurrency { get; set; }
    public int WithIsin { get; set; }
    public int TotalPriceRecords { get; set; }
    public int SecuritiesWithPrices { get; set; }
    public int SecuritiesWithoutPrices { get; set; }
    public DateTime? EarliestPriceDate { get; set; }
    public DateTime? LatestPriceDate { get; set; }
}

public class SecurityGap
{
    public int SecurityAlias { get; set; }
    public string Ticker { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTime LastPriceDate { get; set; }
    public int DaysMissing { get; set; }
}

public class SecurityTypeCoverage
{
    public string SecurityType { get; set; } = string.Empty;
    public int TotalCount { get; set; }
    public int WithPrices { get; set; }
    public double CoveragePercent { get; set; }
}
