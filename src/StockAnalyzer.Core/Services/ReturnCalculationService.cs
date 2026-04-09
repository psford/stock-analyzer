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

        // Get the earliest price to know how far back data goes
        var allPrices = await _priceRepo.GetPricesAsync(
            security.SecurityAlias, DateTime.MinValue.AddYears(1), actualEndDate);
        if (allPrices.Count == 0) return null;

        var earliestDate = allPrices[0].EffectiveDate;

        // Build a date-indexed lookup for fast closest-date searches
        var priceByDate = allPrices.ToDictionary(p => p.EffectiveDate.Date, p => p);

        var periods = BuildPeriods(actualEndDate);
        var returns = new List<PeriodReturn>();

        foreach (var (label, targetDate, annualize) in periods)
        {
            // Skip if data doesn't go back far enough
            if (targetDate < earliestDate) continue;

            var startPriceEntity = FindClosestPrice(priceByDate, targetDate, maxDaysSearch: 7);
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
            if (Math.Abs((earliestDate - ipo).TotalDays) < 7)
            {
                var startPriceEntity = allPrices[0];
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

    private static PriceEntity? FindClosestPrice(
        Dictionary<DateTime, PriceEntity> priceByDate, DateTime target, int maxDaysSearch)
    {
        // Search forward and backward from target date to find nearest trading day
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
