using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace EodhdLoader.Services;

/// <summary>
/// Analyzes price data coverage to identify gaps and missing data.
/// Prioritizes by relevance: recent dates and popular securities first.
/// </summary>
public class PriceCoverageAnalyzer
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ConfigurationService _config;

    public PriceCoverageAnalyzer(IHttpClientFactory httpClientFactory, ConfigurationService config)
    {
        _httpClientFactory = httpClientFactory;
        _config = config;
    }

    /// <summary>
    /// Analyzes price coverage and returns a detailed report of gaps.
    /// </summary>
    public async Task<CoverageReport> AnalyzeCoverageAsync(
        TargetEnvironment environment,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var report = new CoverageReport();
        var baseUrl = _config.GetApiUrl(environment);

        if (string.IsNullOrEmpty(baseUrl))
        {
            report.Error = "API URL not configured";
            return report;
        }

        var httpClient = _httpClientFactory.CreateClient();
        httpClient.BaseAddress = new Uri(baseUrl);

        try
        {
            // Step 1: Get overall status
            progress?.Report("Fetching database status...");
            var status = await GetStatusAsync(httpClient, ct);
            if (status == null)
            {
                report.Error = "Could not connect to API";
                return report;
            }

            report.TotalPriceRecords = status.TotalPriceRecords;
            report.ActiveSecurities = status.ActiveSecurities;
            report.LatestPriceDate = status.LatestPriceDate;

            // Step 2: Get coverage dates for full history (back to 1980)
            progress?.Report("Analyzing date coverage (full history)...");
            var endDate = DateTime.Today.AddDays(-1);
            var startDate = new DateTime(1980, 1, 1); // EODHD has data back to early 80s

            var coverageDates = await GetCoverageDatesAsync(httpClient, startDate, endDate, ct);
            var existingDates = coverageDates?.DatesWithData?
                .Select(d => DateTime.Parse(d))
                .ToHashSet() ?? new HashSet<DateTime>();

            report.DatesWithData = existingDates.Count;

            // Step 3: Calculate expected trading days
            var expectedTradingDays = GetExpectedTradingDays(startDate, endDate);
            report.ExpectedTradingDays = expectedTradingDays.Count;

            // Step 4: Find missing dates by priority tier
            progress?.Report("Identifying missing dates by priority...");

            // Tier 1: Last 30 days (critical - users need current data)
            var tier1End = endDate;
            var tier1Start = endDate.AddDays(-30);
            var tier1Expected = expectedTradingDays.Where(d => d >= tier1Start && d <= tier1End).ToList();
            var tier1Missing = tier1Expected.Where(d => !existingDates.Contains(d)).OrderByDescending(d => d).ToList();

            report.Tier1_Last30Days = new CoverageTier
            {
                Name = "Last 30 Days (Critical)",
                ExpectedDays = tier1Expected.Count,
                MissingDays = tier1Missing.Count,
                MissingDates = tier1Missing.Take(10).Select(d => d.ToString("yyyy-MM-dd")).ToList()
            };

            // Tier 2: Last 90 days (important)
            var tier2End = tier1Start.AddDays(-1);
            var tier2Start = endDate.AddDays(-90);
            var tier2Expected = expectedTradingDays.Where(d => d >= tier2Start && d < tier1Start).ToList();
            var tier2Missing = tier2Expected.Where(d => !existingDates.Contains(d)).OrderByDescending(d => d).ToList();

            report.Tier2_Last90Days = new CoverageTier
            {
                Name = "31-90 Days Ago (Important)",
                ExpectedDays = tier2Expected.Count,
                MissingDays = tier2Missing.Count,
                MissingDates = tier2Missing.Take(10).Select(d => d.ToString("yyyy-MM-dd")).ToList()
            };

            // Tier 3: Last year
            var tier3End = tier2Start.AddDays(-1);
            var tier3Start = endDate.AddYears(-1);
            var tier3Expected = expectedTradingDays.Where(d => d >= tier3Start && d < tier2Start).ToList();
            var tier3Missing = tier3Expected.Where(d => !existingDates.Contains(d)).OrderByDescending(d => d).ToList();

            report.Tier3_LastYear = new CoverageTier
            {
                Name = "91 Days - 1 Year Ago",
                ExpectedDays = tier3Expected.Count,
                MissingDays = tier3Missing.Count,
                MissingDates = tier3Missing.Take(10).Select(d => d.ToString("yyyy-MM-dd")).ToList()
            };

            // Tier 4: 1-5 years ago
            var tier4End = tier3Start.AddDays(-1);
            var tier4Start = endDate.AddYears(-5);
            var tier4Expected = expectedTradingDays.Where(d => d >= tier4Start && d < tier3Start).ToList();
            var tier4Missing = tier4Expected.Where(d => !existingDates.Contains(d)).OrderByDescending(d => d).ToList();

            report.Tier4_Historical = new CoverageTier
            {
                Name = "1-5 Years Ago",
                ExpectedDays = tier4Expected.Count,
                MissingDays = tier4Missing.Count,
                MissingDates = tier4Missing.Take(10).Select(d => d.ToString("yyyy-MM-dd")).ToList()
            };

            // Tier 5: 6-10 years ago
            var tier5End = tier4Start.AddDays(-1);
            var tier5Start = endDate.AddYears(-10);
            var tier5Expected = expectedTradingDays.Where(d => d >= tier5Start && d < tier4Start).ToList();
            var tier5Missing = tier5Expected.Where(d => !existingDates.Contains(d)).OrderByDescending(d => d).ToList();

            report.Tier5_6To10Years = new CoverageTier
            {
                Name = "6-10 Years Ago",
                ExpectedDays = tier5Expected.Count,
                MissingDays = tier5Missing.Count,
                MissingDates = tier5Missing.Take(10).Select(d => d.ToString("yyyy-MM-dd")).ToList()
            };

            // Tier 6: 11-20 years ago
            var tier6End = tier5Start.AddDays(-1);
            var tier6Start = endDate.AddYears(-20);
            var tier6Expected = expectedTradingDays.Where(d => d >= tier6Start && d < tier5Start).ToList();
            var tier6Missing = tier6Expected.Where(d => !existingDates.Contains(d)).OrderByDescending(d => d).ToList();

            report.Tier6_11To20Years = new CoverageTier
            {
                Name = "11-20 Years Ago",
                ExpectedDays = tier6Expected.Count,
                MissingDays = tier6Missing.Count,
                MissingDates = tier6Missing.Take(10).Select(d => d.ToString("yyyy-MM-dd")).ToList()
            };

            // Tier 7: 21+ years ago (back to 1980)
            var tier7Expected = expectedTradingDays.Where(d => d < tier6Start).ToList();
            var tier7Missing = tier7Expected.Where(d => !existingDates.Contains(d)).OrderByDescending(d => d).ToList();

            report.Tier7_21PlusYears = new CoverageTier
            {
                Name = "21+ Years Ago (1980-" + (endDate.Year - 20) + ")",
                ExpectedDays = tier7Expected.Count,
                MissingDays = tier7Missing.Count,
                MissingDates = tier7Missing.Take(10).Select(d => d.ToString("yyyy-MM-dd")).ToList()
            };

            // Step 5: Calculate coverage percentages
            report.OverallCoveragePercent = report.ExpectedTradingDays > 0
                ? (double)report.DatesWithData / report.ExpectedTradingDays * 100
                : 0;

            // Step 6: Estimate security coverage
            progress?.Report("Estimating security coverage...");
            AnalyzeSecurityCoverage(report);

            report.AnalyzedAt = DateTime.Now;
            report.Success = true;
        }
        catch (Exception ex)
        {
            report.Error = ex.Message;
        }

        return report;
    }

    private async Task<PriceStatus?> GetStatusAsync(HttpClient client, CancellationToken ct)
    {
        try
        {
            var response = await client.GetAsync("/api/admin/prices/status", ct);
            if (!response.IsSuccessStatusCode) return null;
            return await response.Content.ReadFromJsonAsync<PriceStatus>(ct);
        }
        catch
        {
            return null;
        }
    }

    private async Task<CoverageDatesResponse?> GetCoverageDatesAsync(
        HttpClient client, DateTime start, DateTime end, CancellationToken ct)
    {
        try
        {
            var url = $"/api/admin/prices/coverage-dates?startDate={start:yyyy-MM-dd}&endDate={end:yyyy-MM-dd}";
            var response = await client.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode) return null;
            return await response.Content.ReadFromJsonAsync<CoverageDatesResponse>(ct);
        }
        catch
        {
            return null;
        }
    }

    private List<DateTime> GetExpectedTradingDays(DateTime start, DateTime end)
    {
        var days = new List<DateTime>();
        for (var date = start; date <= end; date = date.AddDays(1))
        {
            if (IsTradingDay(date))
                days.Add(date);
        }
        return days;
    }

    private static bool IsTradingDay(DateTime date)
    {
        // Skip weekends
        if (date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday)
            return false;

        // Skip major US holidays (approximate - doesn't account for observed dates)
        var month = date.Month;
        var day = date.Day;

        // New Year's Day
        if (month == 1 && day == 1) return false;

        // MLK Day (3rd Monday of January)
        if (month == 1 && date.DayOfWeek == DayOfWeek.Monday && day >= 15 && day <= 21) return false;

        // Presidents Day (3rd Monday of February)
        if (month == 2 && date.DayOfWeek == DayOfWeek.Monday && day >= 15 && day <= 21) return false;

        // Good Friday (varies - skip for now, hard to calculate)

        // Memorial Day (last Monday of May)
        if (month == 5 && date.DayOfWeek == DayOfWeek.Monday && day >= 25) return false;

        // Juneteenth (June 19)
        if (month == 6 && day == 19) return false;

        // Independence Day
        if (month == 7 && day == 4) return false;

        // Labor Day (1st Monday of September)
        if (month == 9 && date.DayOfWeek == DayOfWeek.Monday && day <= 7) return false;

        // Thanksgiving (4th Thursday of November)
        if (month == 11 && date.DayOfWeek == DayOfWeek.Thursday && day >= 22 && day <= 28) return false;

        // Christmas
        if (month == 12 && day == 25) return false;

        return true;
    }

    private static void AnalyzeSecurityCoverage(CoverageReport report)
    {
        // Estimate: if we have X dates with data and Y active securities,
        // ideal would be X * Y records
        if (report.DatesWithData > 0 && report.ActiveSecurities > 0)
        {
            var idealRecords = (long)report.DatesWithData * report.ActiveSecurities;
            report.EstimatedIdealRecords = idealRecords;
            report.RecordCoveragePercent = idealRecords > 0
                ? (double)report.TotalPriceRecords / idealRecords * 100
                : 0;
        }
    }
}

public class CoverageReport
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public DateTime AnalyzedAt { get; set; }

    // Database stats
    public long TotalPriceRecords { get; set; }
    public int ActiveSecurities { get; set; }
    public string? LatestPriceDate { get; set; }

    // Date coverage
    public int DatesWithData { get; set; }
    public int ExpectedTradingDays { get; set; }
    public double OverallCoveragePercent { get; set; }

    // Record coverage estimate
    public long EstimatedIdealRecords { get; set; }
    public double RecordCoveragePercent { get; set; }

    // Tiered analysis
    public CoverageTier? Tier1_Last30Days { get; set; }
    public CoverageTier? Tier2_Last90Days { get; set; }
    public CoverageTier? Tier3_LastYear { get; set; }
    public CoverageTier? Tier4_Historical { get; set; }
    public CoverageTier? Tier5_6To10Years { get; set; }
    public CoverageTier? Tier6_11To20Years { get; set; }
    public CoverageTier? Tier7_21PlusYears { get; set; }

    public int TotalMissingDays =>
        (Tier1_Last30Days?.MissingDays ?? 0) +
        (Tier2_Last90Days?.MissingDays ?? 0) +
        (Tier3_LastYear?.MissingDays ?? 0) +
        (Tier4_Historical?.MissingDays ?? 0) +
        (Tier5_6To10Years?.MissingDays ?? 0) +
        (Tier6_11To20Years?.MissingDays ?? 0) +
        (Tier7_21PlusYears?.MissingDays ?? 0);

    public string GetSummary()
    {
        if (!Success)
            return $"Analysis failed: {Error}";

        var lines = new List<string>
        {
            $"=== Price Coverage Analysis ({AnalyzedAt:yyyy-MM-dd HH:mm}) ===",
            "",
            $"Database Status:",
            $"  Total Records: {TotalPriceRecords:N0}",
            $"  Active Securities: {ActiveSecurities:N0}",
            $"  Latest Price Date: {LatestPriceDate ?? "N/A"}",
            "",
            $"Date Coverage (since 1980):",
            $"  Days with Data: {DatesWithData:N0} / {ExpectedTradingDays:N0} ({OverallCoveragePercent:F1}%)",
            $"  Missing Days: {TotalMissingDays:N0}",
            ""
        };

        if (Tier1_Last30Days != null)
        {
            lines.Add($"📊 {Tier1_Last30Days.Name}:");
            lines.Add($"   Coverage: {Tier1_Last30Days.ExpectedDays - Tier1_Last30Days.MissingDays}/{Tier1_Last30Days.ExpectedDays} days ({Tier1_Last30Days.CoveragePercent:F1}%)");
            if (Tier1_Last30Days.MissingDays > 0)
                lines.Add($"   Missing: {string.Join(", ", Tier1_Last30Days.MissingDates)}");
            lines.Add("");
        }

        if (Tier2_Last90Days != null)
        {
            lines.Add($"📈 {Tier2_Last90Days.Name}:");
            lines.Add($"   Coverage: {Tier2_Last90Days.ExpectedDays - Tier2_Last90Days.MissingDays}/{Tier2_Last90Days.ExpectedDays} days ({Tier2_Last90Days.CoveragePercent:F1}%)");
            if (Tier2_Last90Days.MissingDays > 0)
                lines.Add($"   Missing: {string.Join(", ", Tier2_Last90Days.MissingDates)}");
            lines.Add("");
        }

        if (Tier3_LastYear != null)
        {
            lines.Add($"📉 {Tier3_LastYear.Name}:");
            lines.Add($"   Coverage: {Tier3_LastYear.ExpectedDays - Tier3_LastYear.MissingDays}/{Tier3_LastYear.ExpectedDays} days ({Tier3_LastYear.CoveragePercent:F1}%)");
            if (Tier3_LastYear.MissingDays > 0)
                lines.Add($"   Missing (sample): {string.Join(", ", Tier3_LastYear.MissingDates)}");
            lines.Add("");
        }

        if (Tier4_Historical != null)
        {
            lines.Add($"📜 {Tier4_Historical.Name}:");
            lines.Add($"   Coverage: {Tier4_Historical.ExpectedDays - Tier4_Historical.MissingDays}/{Tier4_Historical.ExpectedDays} days ({Tier4_Historical.CoveragePercent:F1}%)");
            if (Tier4_Historical.MissingDays > 0)
                lines.Add($"   Missing (sample): {string.Join(", ", Tier4_Historical.MissingDates)}");
            lines.Add("");
        }

        if (Tier5_6To10Years != null)
        {
            lines.Add($"🕰️ {Tier5_6To10Years.Name}:");
            lines.Add($"   Coverage: {Tier5_6To10Years.ExpectedDays - Tier5_6To10Years.MissingDays}/{Tier5_6To10Years.ExpectedDays} days ({Tier5_6To10Years.CoveragePercent:F1}%)");
            if (Tier5_6To10Years.MissingDays > 0)
                lines.Add($"   Missing (sample): {string.Join(", ", Tier5_6To10Years.MissingDates)}");
            lines.Add("");
        }

        if (Tier6_11To20Years != null)
        {
            lines.Add($"⏳ {Tier6_11To20Years.Name}:");
            lines.Add($"   Coverage: {Tier6_11To20Years.ExpectedDays - Tier6_11To20Years.MissingDays}/{Tier6_11To20Years.ExpectedDays} days ({Tier6_11To20Years.CoveragePercent:F1}%)");
            if (Tier6_11To20Years.MissingDays > 0)
                lines.Add($"   Missing (sample): {string.Join(", ", Tier6_11To20Years.MissingDates)}");
            lines.Add("");
        }

        if (Tier7_21PlusYears != null)
        {
            lines.Add($"🏛️ {Tier7_21PlusYears.Name}:");
            lines.Add($"   Coverage: {Tier7_21PlusYears.ExpectedDays - Tier7_21PlusYears.MissingDays}/{Tier7_21PlusYears.ExpectedDays} days ({Tier7_21PlusYears.CoveragePercent:F1}%)");
            if (Tier7_21PlusYears.MissingDays > 0)
                lines.Add($"   Missing (sample): {string.Join(", ", Tier7_21PlusYears.MissingDates)}");
            lines.Add("");
        }

        if (EstimatedIdealRecords > 0)
        {
            lines.Add($"Record Density:");
            lines.Add($"  Actual: {TotalPriceRecords:N0}");
            lines.Add($"  Ideal (dates × securities): {EstimatedIdealRecords:N0}");
            lines.Add($"  Density: {RecordCoveragePercent:F1}%");
        }

        return string.Join(Environment.NewLine, lines);
    }
}

public class CoverageTier
{
    public string Name { get; set; } = "";
    public int ExpectedDays { get; set; }
    public int MissingDays { get; set; }
    public List<string> MissingDates { get; set; } = [];

    public double CoveragePercent => ExpectedDays > 0
        ? (double)(ExpectedDays - MissingDays) / ExpectedDays * 100
        : 100;
}

public class PriceStatus
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("totalPriceRecords")]
    public long TotalPriceRecords { get; set; }

    [JsonPropertyName("activeSecurities")]
    public int ActiveSecurities { get; set; }

    [JsonPropertyName("latestPriceDate")]
    public string? LatestPriceDate { get; set; }

    [JsonPropertyName("eodhdApiConfigured")]
    public bool EodhdApiConfigured { get; set; }
}
