# Price Data Operations Design

## Summary

This work cleans up corrupted price history in the stock analyzer's Azure SQL database and hardens the existing `PriceRefreshService` so it runs reliably every day in production. The cleanup phase deletes rows with future-dated `EffectiveDate` values, audits the full price history against the business calendar to find missing trading days, and backfills those gaps using EODHD's per-ticker historical API.

The hardening work makes the daily refresh do three things it currently does not: look back up to 14 days to catch missed trading days, forward-fill weekends and holidays with the prior day's closing price, and reject future-dated records at every insert path so corrupted data cannot re-enter. The service already exists and works locally; the primary production blocker is a missing EODHD API key in the Azure App Service configuration, which is resolved in the final deployment phase.

## Definition of Done

Clean up corrupted price data (future-dated rows, March 2026 gaps, other discontinuities), then harden the daily PriceRefreshService to fetch current-day prices, forward-fill weekends/holidays (never future dates), look back 1-2 weeks for missed days, and validate all inserts. ForwardFillHolidaysAsync gets a date cap. Failures logged via Application Insights; Slack alerting deferred to a future feature.

**Deliverables:**
1. Delete all price rows with EffectiveDate > 2026-04-08; backfill March gaps; audit full history for discontinuities across all securities.
2. PriceRefreshService runs daily, fetches current day from EODHD, forward-fills past weekends/holidays via BusinessCalendar, looks back 1-2 weeks for missed days. Never fills future dates.
3. BulkInsertAsync and all insert paths reject future dates and validate data quality.
4. ForwardFillHolidaysAsync capped to never fill beyond today.
5. Failure logging via Application Insights.

**Out of scope:** New ticker historical backfill (already handled by gap-fill on user request), international exchanges, desktop EODHD loader removal, Slack alerting.

## Acceptance Criteria

### price-data-ops.AC1: Daily refresh fetches and inserts current-day prices
- **price-data-ops.AC1.1 Success:** Daily refresh fetches > 10K records from EODHD bulk API and matches > 50% to SecurityMaster
- **price-data-ops.AC1.2 Success:** Matched records are inserted into Prices table with correct OHLCV and AdjustedClose values
- **price-data-ops.AC1.3 Failure:** When EODHD API returns empty/error, service logs warning and retries after 1 hour
- **price-data-ops.AC1.4 Edge:** When service starts after multiple missed days, lookback fills all missed trading days (up to 14 days)

### price-data-ops.AC2: Forward-fill populates weekends and holidays correctly
- **price-data-ops.AC2.1 Success:** After Monday refresh, Saturday and Sunday rows exist with Friday's close and Volume=0
- **price-data-ops.AC2.2 Success:** After Tuesday refresh following Monday holiday, Saturday/Sunday/Monday rows all filled with Friday's close
- **price-data-ops.AC2.3 Failure:** Forward-fill never creates rows with EffectiveDate > today
- **price-data-ops.AC2.4 Edge:** Forward-fill uses BusinessCalendar to determine non-business days (not hardcoded weekend logic)

### price-data-ops.AC3: Insert validation rejects bad data
- **price-data-ops.AC3.1 Success:** BulkInsertAsync silently filters out records with EffectiveDate > DateTime.UtcNow.Date and logs rejected count
- **price-data-ops.AC3.2 Success:** ForwardFillHolidaysAsync respects maxFillDate parameter (defaults to today)
- **price-data-ops.AC3.3 Edge:** Validation applies to all insert paths: daily refresh, user gap-fill, admin endpoints

### price-data-ops.AC4: Historical gaps are identified and backfilled
- **price-data-ops.AC4.1 Success:** SQL gap audit identifies all missing business days for tracked securities
- **price-data-ops.AC4.2 Success:** Backfill fetches missing dates from EODHD and inserts them, respecting rate limits
- **price-data-ops.AC4.3 Success:** Post-backfill audit shows no missing business days for securities with EODHD data
- **price-data-ops.AC4.4 Failure:** Securities without EODHD data are flagged (not treated as gaps)

### price-data-ops.AC5: Service runs reliably in production
- **price-data-ops.AC5.1 Success:** App Service has "Always On" enabled and EODHD API key configured via Key Vault
- **price-data-ops.AC5.2 Success:** First production refresh cycle logs > 10K matched records in Application Insights
- **price-data-ops.AC5.3 Failure:** When EODHD key is missing, service logs clear error message (not silent exit)

## Glossary

- **EODHD**: "End of Day Historical Data" — a third-party financial data API providing bulk and per-ticker end-of-day price feeds. Used via a paid API key configured in Key Vault.
- **OHLCV**: Open, High, Low, Close, Volume — the five standard fields in an end-of-day price record.
- **AdjustedClose**: Closing price adjusted for stock splits and dividends. More accurate than raw close for return calculations.
- **SecurityMaster**: The application's internal registry of tracked securities. Contains canonical identifiers and ticker aliases used to match incoming price data to known securities.
- **BusinessCalendar**: A database table (`data.BusinessCalendar`) listing US market trading days. Used to determine which dates are non-business days (weekends, holidays) and which dates are gaps.
- **Forward-fill**: The practice of copying the most recent known price forward to fill non-trading days (weekends, holidays) so that every calendar date has a price row.
- **BulkInsertAsync**: The repository method that writes price records to the `data.Prices` table in bulk, with deduplication logic to prevent double-inserts.
- **ForwardFillHolidaysAsync**: The repository method that performs the forward-fill operation. This design adds a `maxFillDate` parameter to cap fills at today's date.
- **PriceRefreshService**: A .NET `BackgroundService` that runs on a configurable UTC schedule (default 2:30 AM) and orchestrates the daily price ingestion pipeline.
- **BackgroundService**: A .NET base class for long-running in-process services. Runs alongside the main web application inside the same App Service instance.
- **Always On**: An Azure App Service setting that keeps the application process running continuously rather than shutting down after idle periods. Required for scheduled background tasks to fire reliably.
- **Key Vault**: Azure Key Vault — a secrets management service. The EODHD API key is stored here and referenced in `endpoints.json`.
- **Application Insights**: Azure's application performance monitoring (APM) service. Used here for structured logging and failure alerting from the refresh pipeline.
- **Defense-in-depth**: A reliability principle where validation is applied at multiple independent layers so no single failure allows bad data through.
- **Lookback**: The pattern of re-checking recent dates (up to 14 days) after a daily fetch to catch days where fewer records than expected were received.
- **Gap audit**: A SQL query that joins the price history against `data.BusinessCalendar` to identify (security, date) pairs where a price row is expected but absent.
- **`endpoints.json`**: A configuration file that maps service dependencies (APIs, databases) to their sources — literal values, environment variables, or Key Vault references.

## Architecture

The price data pipeline has three layers: ingestion (EODHD bulk API), processing (matching, validation, forward-fill), and storage (Azure SQL via EF Core). PriceRefreshService orchestrates the daily flow as an in-process BackgroundService.

**Daily refresh flow (2:30 AM UTC):**

1. Fetch bulk EOD data from EODHD for the previous trading day
2. Match returned tickers against SecurityMaster's active ticker-alias map
3. Validate: reject future dates, reject zero-close records
4. Insert matched records via BulkInsertAsync (deduplicates against existing data)
5. Look back 14 days — for any business day with fewer matched records than expected, re-fetch from EODHD
6. Forward-fill non-business days (weekends + holidays) from BusinessCalendar, capped at today's date
7. Log summary metrics: records fetched, matched, inserted, gaps detected, forward-fills applied

**Forward-fill logic:**

When the service runs on Monday, it forward-fills Saturday and Sunday with Friday's close. When it runs on Tuesday after a Monday holiday (e.g., Memorial Day), it fills Saturday, Sunday, and Monday. Forward-fill queries BusinessCalendar (SourceId=1, US market) for non-business days between the last known price date and today. It never fills dates beyond `DateTime.UtcNow.Date`.

**Insert validation (defense-in-depth):**

BulkInsertAsync rejects any record with `EffectiveDate > DateTime.UtcNow.Date`. This guard applies to all insert paths: daily refresh, gap-fill on user request, admin endpoints, and ForwardFillHolidaysAsync.

## Existing Patterns

Investigation found the existing PriceRefreshService at `src/StockAnalyzer.Core/Services/PriceRefreshService.cs`. The service architecture is correct — BackgroundService with scheduled execution, EODHD bulk API, SecurityMaster matching, BulkInsertAsync. The code works locally but has never successfully executed in production because the EODHD API key is likely not configured in the App Service environment variables.

This design follows the existing patterns:
- BackgroundService with configurable schedule (`PriceDatabase:RefreshHourUtc/MinuteUtc`)
- Scoped DI for database access (`_serviceProvider.CreateScope()`)
- EODHD bulk endpoint for efficiency (`/api/eod-bulk-last-day/US`)
- `GetActiveTickerAliasMapAsync()` projected query for ticker matching
- `BulkInsertAsync` with deduplication for insert

ForwardFillHolidaysAsync at `src/StockAnalyzer.Core/Data/SqlPriceRepository.cs` (lines 603-795) already uses BusinessCalendar correctly. The only change is adding a date cap parameter.

The gap-fill on user request in `AggregatedStockDataService.cs` (lines 163-336) already persists API-fetched prices to the DB. No changes needed — this path is working correctly.

## Implementation Phases

<!-- START_PHASE_1 -->
### Phase 1: Data Cleanup
**Goal:** Remove corrupted data and establish a clean baseline.

**Components:**
- SQL script to delete all rows from `data.Prices` where `EffectiveDate > '2026-04-08'`
- SQL gap audit query joining `data.Prices` against `data.BusinessCalendar` for all tracked securities — produces a report of (SecurityAlias, TickerSymbol, MissingDate) for every missing business day
- Gap report analysis: categorize gaps by severity (recent vs historical, single day vs multi-day stretches)

**Dependencies:** None (first phase)

**Done when:** Future-dated rows deleted. Gap audit report generated showing scope of missing data across all tracked securities.
<!-- END_PHASE_1 -->

<!-- START_PHASE_2 -->
### Phase 2: Insert Validation
**Goal:** Prevent bad data from entering the database regardless of insert path.

**Covers:** price-data-ops.AC3

**Components:**
- Date guard in `BulkInsertAsync` at `src/StockAnalyzer.Core/Data/SqlPriceRepository.cs` — filter out records with `EffectiveDate > DateTime.UtcNow.Date` before insert, log count of rejected records
- Date cap parameter on `ForwardFillHolidaysAsync` — default `maxFillDate = DateTime.UtcNow.Date`, filter `fillTargets` to exclude dates beyond cap
- Tests verifying future dates are rejected and cap is enforced

**Dependencies:** Phase 1 (clean data)

**Done when:** BulkInsertAsync rejects future-dated records. ForwardFillHolidaysAsync respects date cap. Tests pass.
<!-- END_PHASE_2 -->

<!-- START_PHASE_3 -->
### Phase 3: Daily Refresh Hardening
**Goal:** Make PriceRefreshService reliably execute daily with lookback and forward-fill.

**Covers:** price-data-ops.AC1, price-data-ops.AC2

**Components:**
- Modified `RefreshPreviousDayAsync` in `src/StockAnalyzer.Core/Services/PriceRefreshService.cs` — after daily fetch, look back 14 days for dates with fewer matched records than expected, re-fetch those dates
- Inline forward-fill after daily fetch — call `ForwardFillHolidaysAsync` (with date cap) for non-business days between last known price and today
- Remove weekend skip (lines 84-90) — service runs every day, uses lookback + forward-fill to handle weekends and holidays
- Structured log summary after each refresh cycle with metrics

**Dependencies:** Phase 2 (validation guards in place)

**Done when:** Service runs daily, fetches current day, fills gaps going back 14 days, forward-fills weekends/holidays up to today. Tests verify the flow.
<!-- END_PHASE_3 -->

<!-- START_PHASE_4 -->
### Phase 4: Historical Backfill
**Goal:** Fill all gaps identified in Phase 1 audit using EODHD per-ticker historical API.

**Covers:** price-data-ops.AC4

**Components:**
- Backfill orchestrator that reads gap report from Phase 1, groups gaps by security, fetches missing date ranges from EODHD via `GetHistoricalDataAsync`
- Throttling: max 10 concurrent requests, 100ms spacing (existing `BackfillTickersParallelAsync` pattern)
- Progress logging: securities processed, gaps filled, failures

**Dependencies:** Phase 2 (validation guards), Phase 3 (service hardened — prevents new gaps while backfilling old ones)

**Done when:** Gap audit re-run shows no missing business days for tracked securities (excluding securities where EODHD has no data — marked via existing `mark-eodhd-unavailable` endpoint).
<!-- END_PHASE_4 -->

<!-- START_PHASE_5 -->
### Phase 5: Deployment Verification
**Goal:** Confirm the service runs in production Azure App Service.

**Covers:** price-data-ops.AC5

**Components:**
- Verify App Service "Always On" is enabled
- Verify EODHD API key is set in App Service environment variables (via Key Vault reference in `endpoints.json` prod config)
- Deploy updated code
- Monitor Application Insights for the first daily refresh cycle — confirm logs show records fetched > 0, match rate > 50%
- Run ForwardFillHolidaysAsync on production data with date cap to fill historical weekends/holidays

**Dependencies:** Phase 4 (all code changes deployed together)

**Done when:** First production daily refresh completes successfully with > 10K matched records. Application Insights shows refresh log entries.
<!-- END_PHASE_5 -->

## Additional Considerations

**EODHD rate limits:** Paid plans allow ~100K API calls/day. The daily bulk endpoint counts as 1 call. Historical per-ticker backfill (Phase 4) may require multiple days if gaps are extensive across 48K securities. Backfill should be resumable (track progress, skip already-filled gaps on restart).

**AdjustedClose availability:** EODHD's bulk endpoint returns `adjusted_close`. Some older records may not have this field. The existing code handles this (nullable `AdjustedClose` on `PriceEntity`). ReturnCalculationService already falls back to `Close` when `AdjustedClose` is null.

**Audit scope for "all active":** Phase 1 audits tracked securities. Per user request, a follow-up audit of all 66K active securities should be planned as a separate effort after tracked securities are clean.
