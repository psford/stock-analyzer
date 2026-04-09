-- cleanup_future_prices.sql
-- Deletes all price rows with EffectiveDate in the future (after today)
-- Uses GETUTCDATE() so the script is safe to run on any date
-- Run against data.Prices in Azure SQL or local SQLEXPRESS

BEGIN TRANSACTION;

-- Count before delete for audit
SELECT COUNT(*) AS FutureDatedRowCount
FROM data.Prices
WHERE EffectiveDate > CAST(GETUTCDATE() AS DATE);

-- Delete future-dated rows
DELETE FROM data.Prices
WHERE EffectiveDate > CAST(GETUTCDATE() AS DATE);

-- Verify deletion
SELECT COUNT(*) AS RemainingFutureDatedRows
FROM data.Prices
WHERE EffectiveDate > CAST(GETUTCDATE() AS DATE);

COMMIT TRANSACTION;
