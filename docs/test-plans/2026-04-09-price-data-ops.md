# Human Test Plan: Price Data Operations

## Prerequisites
- Stock Analyzer deployed to production (App Service with Always On enabled)
- Application Insights configured and receiving telemetry
- Azure CLI authenticated with access to `rg-stockanalyzer-prod`
- SQL Express available locally for integration tests (optional -- tests skip gracefully)
- All automated tests passing: `dotnet test tests/StockAnalyzer.Core.Tests/`

## Phase 1: Daily Refresh Verification (AC1.1, AC5.2)

| Step | Action | Expected |
|------|--------|----------|
| 1 | Wait for first scheduled refresh cycle (2:30 AM UTC) or trigger startup backfill by restarting the App Service | Refresh cycle begins within 5 minutes of startup |
| 2 | Run KQL in Application Insights: `traces \| where message contains "Refresh summary" \| project timestamp, message \| order by timestamp desc \| take 5` | At least one trace appears. `Fetched` value exceeds 10,000. `MatchRate` exceeds 50%. |
| 3 | Run KQL: `traces \| where level == "Error" and message contains "PriceRefresh" \| order by timestamp desc \| take 5` | Zero error traces from the refresh cycle |
| 4 | Run KQL: `traces \| where message contains "Daily refresh cycle complete" \| order by timestamp desc \| take 1` | Trace exists with `Inserted > 10000` |

## Phase 2: Gap Audit and Backfill Verification (AC4.3)

| Step | Action | Expected |
|------|--------|----------|
| 1 | Call `POST /api/admin/prices/backfill-gaps` against the production API (with admin auth) | Response returns 200 OK with GapBackfillResult JSON. `Success` is true. |
| 2 | Run gap audit SQL against production database: query BusinessCalendar for business days, LEFT JOIN Prices, WHERE price is NULL, for all tracked/active/non-flagged securities | Zero rows returned (excluding securities with `IsEodhdUnavailable = 1`) |
| 3 | Verify `SecuritiesFlagged` count in the backfill result | If any securities were flagged, confirm they are tickers with no EODHD coverage (e.g., delisted or non-US securities) |

## Phase 3: Infrastructure Verification (AC5.1)

| Step | Action | Expected |
|------|--------|----------|
| 1 | Run: `az webapp show --resource-group rg-stockanalyzer-prod --name stockanalyzer-prod --query "siteConfig.alwaysOn" -o tsv` | Output: `true` |
| 2 | Run: `az keyvault secret show --vault-name kv-stockanalyzer-prod --name EodhdApiKey --query "id" -o tsv` | Returns a valid secret ID URL |
| 3 | Run: `az webapp identity show --resource-group rg-stockanalyzer-prod --name stockanalyzer-prod` | Returns managed identity with `principalId` present |
| 4 | Verify Key Vault access policy includes the App Service managed identity | Identity has `Get` permission on secrets |

## Phase 4: API Key Guard Verification (AC5.3)

| Step | Action | Expected |
|------|--------|----------|
| 1 | Code review: Open `src/StockAnalyzer.Core/Services/PriceRefreshService.cs` lines 50-57 | Early-return guard checks for null/empty EODHD API key, logs "EODHD API key not configured", returns without scheduling cycles |
| 2 | (Optional, destructive) Temporarily remove `EodhdApiKey` from App Service configuration, restart the service | Application Insights shows "EODHD API key not configured" log entry within 60 seconds of startup |
| 3 | (If step 2 was performed) Immediately restore `EodhdApiKey` in App Service configuration and restart | Service resumes normal operation, next refresh cycle completes successfully |

## End-to-End: Full Refresh Cycle Validation

**Purpose:** Verify the complete daily refresh lifecycle -- missing day detection, EODHD fetch, SecurityMaster matching, insert, forward-fill, and gap audit -- runs without errors in production.

**Steps:**
1. Note the current timestamp in UTC
2. Restart the App Service to trigger the startup backfill cycle
3. Monitor Application Insights for 10 minutes using KQL: `traces | where timestamp > datetime('NOTED_TIMESTAMP') and message contains "PriceRefresh" | order by timestamp asc`
4. Verify the following sequence appears in logs:
   - "Checking for missing business days" (lookback detection)
   - "Refresh summary" with fetched/matched/inserted counts (daily refresh)
   - "Forward-fill" activity for any non-business days in the lookback window
   - "Daily refresh cycle complete" (successful completion)
5. Query the Prices table: `SELECT TOP 5 EffectiveDate, COUNT(*) as SecurityCount FROM data.Prices GROUP BY EffectiveDate ORDER BY EffectiveDate DESC`
6. Confirm the most recent business day has price data for the expected number of tracked securities

## End-to-End: Weekend/Holiday Forward-Fill Verification

**Purpose:** Validate that weekends and holidays are forward-filled with the previous business day's closing price.

**Steps:**
1. After a Monday refresh cycle completes, query: `SELECT p.EffectiveDate, p.Open, p.High, p.Low, p.Close, p.Volume FROM data.Prices p WHERE p.SecurityAlias = (SELECT TOP 1 SecurityAlias FROM data.SecurityMaster WHERE IsTracked = 1) AND p.EffectiveDate BETWEEN DATEADD(day, -3, GETUTCDATE()) AND GETUTCDATE() ORDER BY p.EffectiveDate`
2. Verify Saturday and Sunday rows exist
3. Confirm Saturday/Sunday OHLC values all equal Friday's Close
4. Confirm Saturday/Sunday Volume = 0
5. If a recent holiday exists in BusinessCalendar, verify its row follows the same pattern (OHLC = prior business day Close, Volume = 0)

## Traceability

| Acceptance Criterion | Automated Test | Manual Step |
|----------------------|----------------|-------------|
| AC1.1 (match rate) | `RefreshDateResult_WithKnownCounts_MatchRateIsCalculable`, `RefreshDateAsync_WithEodhdAndSecurityMaster_CalculatesMatchRate` | Phase 1, Step 2 |
| AC1.2 (OHLCV mapping) | `EodhdRecordToPriceCreateDto_MapsAllOhlcvFields` | -- |
| AC1.3 (empty response) | `RefreshDateResult_WithEmptyEodhdResponse_ReturnsZeroFetched` | -- |
| AC1.4 (missing days + lookback) | `RunDailyRefreshCycleAsync_QueryPattern_IdentifiesMissingBusinessDaysCorrectly`, `RunDailyRefreshCycleAsync_QueryPattern_Uses14DayLookbackWindow` | -- |
| AC2.1 (weekend fill) | `BusinessCalendar_WeekendDates_IdentifiedAsNonBusiness` | E2E Weekend/Holiday, Steps 1-4 |
| AC2.2 (holiday fill) | `BusinessCalendar_WithHoliday_ExcludesHolidayFromBusinessDaysQuery` | E2E Weekend/Holiday, Step 5 |
| AC2.3 (maxFillDate cap) | `ForwardFillQuery_WithMaxFillDateCap_NeverIncludesFutureDates` | -- |
| AC2.4 (calendar-based detection) | `BusinessCalendar_WithWednesdayHoliday_IdentifiedForForwardFill` | -- |
| AC3.1 (future date rejection) | `BulkInsertAsync_FilteringLogic_RejectsAllFutureDates`, `BulkInsertAsync_WithAllFutureDateRecords_WritesNothingToDatabase` | -- |
| AC3.2 (forward-fill date cap) | `ForwardFillQuery_WithExplicitPastMaxFillDate_ExcludesDatesAfterCap` | -- |
| AC3.3 (CreateAsync guard) | `CreateAsync_WithFutureDate_ThrowsArgumentExceptionWithFutureDate` | -- |
| AC4.1 (gap detection) | `RunGapAuditAsync_WithKnownGaps_IdentifiesAllMissingDates`, `GapAudit_WithSecurityFirstPriceAfterCalendarStart_ExcludesDatesBeforeFirstPrice` | Phase 2, Step 2 |
| AC4.2 (backfill contract) | `BackfillGapsAsync_HasCorrectSignatureAndReturnType`, `BackfillGapsAsync_AcceptsCancellationToken_InPublicSignature` | Phase 2, Step 1 |
| AC4.3 (zero gaps after backfill) | `GapDetection_AfterBackfill_ReturnsZeroGaps` | Phase 2, Steps 1-2 |
| AC4.4 (unavailable flagging) | `BackfillGapsAsync_WhenEodhdReturnsEmpty_FlagsSecurityAsUnavailable`, `GapAudit_ExcludesFlaggedSecurities_FromResults` | Phase 2, Step 3 |
| AC5.1 (infrastructure) | -- | Phase 3, Steps 1-4 |
| AC5.2 (live cycle) | -- | Phase 1, Steps 1-4 |
| AC5.3 (API key guard) | -- | Phase 4, Steps 1-3 |
