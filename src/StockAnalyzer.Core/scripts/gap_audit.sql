-- gap_audit.sql
-- Joins data.Prices against data.BusinessCalendar (SourceId=1, US market)
-- to find missing business days for all tracked securities.
-- Output: (SecurityAlias, TickerSymbol, MissingDate) per gap

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
)
SELECT ed.SecurityAlias,
       ed.TickerSymbol,
       ed.EffectiveDate AS MissingDate
FROM ExpectedDates ed
LEFT JOIN data.Prices p
    ON p.SecurityAlias = ed.SecurityAlias
   AND p.EffectiveDate = ed.EffectiveDate
WHERE p.Id IS NULL
ORDER BY ed.TickerSymbol, ed.EffectiveDate;
