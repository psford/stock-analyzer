using StockAnalyzer.Core.Data.Entities;

namespace StockAnalyzer.Core.Services;

/// <summary>
/// Calculates period returns for a security using DB price data.
/// Returns are cumulative for periods under 1 year, annualized for 1 year+.
/// </summary>
public class ReturnCalculationService
{
    private readonly IPriceRepository _priceRepo;
    private readonly ISecurityMasterRepository _securityRepo;

    public ReturnCalculationService(IPriceRepository priceRepo, ISecurityMasterRepository securityRepo)
    {
        _priceRepo = priceRepo;
        _securityRepo = securityRepo;
    }

    public record PeriodReturn(string Label, decimal ReturnPct, bool IsAnnualized);

    public record ReturnTableResult(
        List<PeriodReturn> Returns,
        DateTime EndDate,
        DateTime? EarliestPriceDate);

    /// <summary>
    /// Compute returns for all standard periods, as of the given end date.
    /// Periods without sufficient data are omitted.
    ///
    /// Performance note: Uses targeted ±7-day window queries per period instead of
    /// loading the security's entire price history into memory. AdjustedClose already
    /// incorporates splits and dividends, so we only need the boundary prices for each
    /// period — not the prices in between. On Azure SQL Standard S0 with a cold buffer
    /// pool, the full-history approach was doing 150+ physical reads per request and
    /// taking 30-60 seconds; the targeted approach does ~15 × 2-page seeks and returns
    /// in under 2 seconds.
    /// </summary>
    public async Task<ReturnTableResult?> CalculateReturnsAsync(
        string ticker, DateTime endDate, string? ipoDate = null)
    {
        var security = await _securityRepo.GetByTickerAsync(ticker);
        if (security == null) return null;

        // Get the end price: closest trading day on or before endDate
        var endPrice = await GetClosestPriceOnOrBefore(security.SecurityAlias, endDate);
        if (endPrice == null) return null;

        // Use AdjustedClose for total return (reflects splits + dividends); fall back to Close
        var endClose = endPrice.AdjustedClose ?? endPrice.Close;
        if (endClose == 0) return null;

        var actualEndDate = endPrice.EffectiveDate;

        // Find the earliest date this security has data for (indexed TOP 1 seek, not a full scan).
        // Used to skip periods that pre-date the security's data and for the inception check.
        var (earliestDate, _) = await _priceRepo.GetDateRangeAsync(security.SecurityAlias);
        if (earliestDate == null) return null;

        var periods = BuildPeriods(actualEndDate);
        var returns = new List<PeriodReturn>();

        foreach (var (label, targetDate, annualize) in periods)
        {
            // Skip if data doesn't go back far enough
            if (targetDate < earliestDate.Value) continue;

            var startPriceEntity = await FindClosestPriceInRangeAsync(
                security.SecurityAlias, targetDate, maxDaysSearch: 7);
            if (startPriceEntity == null) continue;

            var startClose = startPriceEntity.AdjustedClose ?? startPriceEntity.Close;
            if (startClose == 0) continue;

            var cumReturn = (endClose - startClose) / startClose;

            if (annualize)
            {
                var years = (actualEndDate - startPriceEntity.EffectiveDate).TotalDays / 365.25;
                if (years < 0.5) continue;
                var annReturn = Math.Pow((double)(1 + cumReturn), 1.0 / years) - 1;
                returns.Add(new PeriodReturn(label, Math.Round((decimal)annReturn * 100, 2), true));
            }
            else
            {
                returns.Add(new PeriodReturn(label, Math.Round(cumReturn * 100, 2), false));
            }
        }

        // Since Inception: only if we have data back to IPO date
        if (!string.IsNullOrEmpty(ipoDate) && DateTime.TryParse(ipoDate, out var ipo))
        {
            // Data must start within 7 days of IPO
            if (Math.Abs((earliestDate.Value - ipo).TotalDays) < 7)
            {
                // Fetch the earliest price row for the inception starting value.
                // Single TOP 1 seek on (SecurityAlias, EffectiveDate ASC).
                var earliestPriceWindow = await _priceRepo.GetPricesAsync(
                    security.SecurityAlias,
                    earliestDate.Value,
                    earliestDate.Value);
                var startPriceEntity = earliestPriceWindow.FirstOrDefault();
                if (startPriceEntity != null)
                {
                    var inceptionStartClose = startPriceEntity.AdjustedClose ?? startPriceEntity.Close;
                    if (inceptionStartClose > 0)
                    {
                        var cumReturn = (endClose - inceptionStartClose) / inceptionStartClose;
                        var years = (actualEndDate - startPriceEntity.EffectiveDate).TotalDays / 365.25;

                        if (years >= 1)
                        {
                            var annReturn = Math.Pow((double)(1 + cumReturn), 1.0 / years) - 1;
                            returns.Add(new PeriodReturn("Since Inception",
                                Math.Round((decimal)annReturn * 100, 2), true));
                        }
                        else
                        {
                            returns.Add(new PeriodReturn("Since Inception",
                                Math.Round(cumReturn * 100, 2), false));
                        }
                    }
                }
            }
        }

        return new ReturnTableResult(returns, actualEndDate, earliestDate);
    }

    private static List<(string Label, DateTime TargetDate, bool Annualize)> BuildPeriods(DateTime endDate)
    {
        return new List<(string, DateTime, bool)>
        {
            ("1 Day", endDate.AddDays(-1), false),
            ("5 Days", endDate.AddDays(-5), false),
            ("MTD", new DateTime(endDate.Year, endDate.Month, 1), false),
            ("1 Month", endDate.AddMonths(-1), false),
            ("3 Months", endDate.AddMonths(-3), false),
            ("6 Months", endDate.AddMonths(-6), false),
            ("YTD", new DateTime(endDate.Year, 1, 1), false),
            ("1 Year", endDate.AddYears(-1), true),
            ("2 Years", endDate.AddYears(-2), true),
            ("5 Years", endDate.AddYears(-5), true),
            ("10 Years", endDate.AddYears(-10), true),
            ("15 Years", endDate.AddYears(-15), true),
            ("20 Years", endDate.AddYears(-20), true),
            ("30 Years", endDate.AddYears(-30), true),
        };
    }

    private async Task<PriceEntity?> GetClosestPriceOnOrBefore(int securityAlias, DateTime date)
    {
        // Search up to 7 days back to find the nearest trading day
        var prices = await _priceRepo.GetPricesAsync(
            securityAlias, date.AddDays(-7), date);
        return prices.LastOrDefault();
    }

    /// <summary>
    /// Fetch the closest trading day within ±maxDaysSearch of target, preferring
    /// forward dates (target + offset) over backward dates (target - offset), matching
    /// the original in-memory FindClosestPrice semantics.
    /// Does a single indexed seek on (SecurityAlias, EffectiveDate) for a tiny date window,
    /// returning at most ~10 rows.
    /// </summary>
    private async Task<PriceEntity?> FindClosestPriceInRangeAsync(
        int securityAlias, DateTime target, int maxDaysSearch)
    {
        var prices = await _priceRepo.GetPricesAsync(
            securityAlias,
            target.AddDays(-maxDaysSearch),
            target.AddDays(maxDaysSearch));

        if (prices.Count == 0) return null;

        // Prefer forward, then backward, matching the original offset loop:
        //   offset 0: target
        //   offset 1: target+1, then target-1
        //   offset 2: target+2, then target-2
        //   ...
        var priceByDate = prices.ToDictionary(p => p.EffectiveDate.Date, p => p);
        for (int offset = 0; offset <= maxDaysSearch; offset++)
        {
            if (priceByDate.TryGetValue(target.AddDays(offset).Date, out var fwd))
                return fwd;
            if (offset > 0 && priceByDate.TryGetValue(target.AddDays(-offset).Date, out var bwd))
                return bwd;
        }
        return null;
    }
}
