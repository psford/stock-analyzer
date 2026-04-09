-- gap_summary.sql
-- Categorizes gaps by severity: recent vs historical, single vs multi-day stretches
-- Run after gap_audit.sql to understand scope

WITH TrackedSecurities AS (
    SELECT sm.SecurityAlias, sm.TickerSymbol
    FROM data.SecurityMaster sm
    WHERE sm.IsTracked = 1
      AND sm.IsActive = 1
      AND sm.IsEodhdUnavailable = 0
),
PriceRanges AS (
    SELECT p.SecurityAlias,
           MIN(p.EffectiveDate) AS FirstDate,
           MAX(p.EffectiveDate) AS LastDate
    FROM data.Prices p
    INNER JOIN TrackedSecurities ts ON ts.SecurityAlias = p.SecurityAlias
    GROUP BY p.SecurityAlias
),
ExpectedDates AS (
    SELECT ts.SecurityAlias,
           ts.TickerSymbol,
           bc.EffectiveDate
    FROM TrackedSecurities ts
    INNER JOIN PriceRanges pr ON pr.SecurityAlias = ts.SecurityAlias
    INNER JOIN data.BusinessCalendar bc
        ON bc.SourceId = 1
       AND bc.IsBusinessDay = 1
       AND bc.EffectiveDate BETWEEN pr.FirstDate AND pr.LastDate
),
Gaps AS (
    SELECT ed.SecurityAlias,
           ed.TickerSymbol,
           ed.EffectiveDate AS MissingDate
    FROM ExpectedDates ed
    LEFT JOIN data.Prices p
        ON p.SecurityAlias = ed.SecurityAlias
       AND p.EffectiveDate = ed.EffectiveDate
    WHERE p.Id IS NULL
)
-- Summary: per-security gap count, recent gaps (last 30 days), total
SELECT
    g.TickerSymbol,
    g.SecurityAlias,
    COUNT(*) AS TotalGapDays,
    SUM(CASE WHEN g.MissingDate >= DATEADD(DAY, -30, GETUTCDATE()) THEN 1 ELSE 0 END) AS RecentGaps_Last30Days,
    MIN(g.MissingDate) AS EarliestGap,
    MAX(g.MissingDate) AS LatestGap
FROM Gaps g
GROUP BY g.TickerSymbol, g.SecurityAlias
ORDER BY TotalGapDays DESC;
