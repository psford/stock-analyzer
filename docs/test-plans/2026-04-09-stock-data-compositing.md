# Human Test Plan: Stock Data Compositing

**Implementation plan:** `docs/implementation-plans/2026-04-09-stock-data-compositing/`
**Date generated:** 2026-04-10

## Prerequisites

- Local development environment running (`dotnet run` from `src/StockAnalyzer.Api`)
- All automated tests passing: `dotnet test tests/StockAnalyzer.Core.Tests/` (15 tests, 0 failures)
- Valid API keys configured in `.env` for at least one provider (TwelveData, FMP, or Yahoo)
- Browser with DevTools available (Chrome or Edge recommended)

## Phase 1: Dividend Yield Display Verification

| Step | Action | Expected |
|------|--------|----------|
| 1 | Start the app locally. Navigate to the stock search page. | App loads without errors. |
| 2 | Search for a known dividend-paying stock: type `JNJ` and submit. | Stock info loads. Key Metrics panel appears. |
| 3 | In the Key Metrics panel, look for a row labeled "Dividend Yield". | Row is present with a percentage value (e.g., "2.85%"). |
| 4 | Confirm the dividend yield value format. | Value follows `X.XX%` format -- two decimal places, followed by a percent sign. No raw decimal (e.g., not "0.0285"). |
| 5 | Search for a non-dividend stock: type `GOOGL` and submit. | Stock info loads. Key Metrics panel appears. |
| 6 | Inspect the Key Metrics panel for "Dividend Yield". | The "Dividend Yield" row is completely absent. It does not appear as "N/A", "0.00%", or blank -- it is not rendered at all. |

## Phase 2: Zero Dividend Yield Edge Case

| Step | Action | Expected |
|------|--------|----------|
| 1 | Search for any stock that returns data (e.g., `AAPL`). | Stock info loads with Key Metrics visible. |
| 2 | Open browser DevTools (F12), go to Console tab. | Console is ready. |
| 3 | Before the page renders Key Metrics, or after load, add a breakpoint at `app.js:2301` and modify `info.dividendYield` to `0` before the condition evaluates. | The "Dividend Yield" row does not appear. The condition `info.dividendYield != null && info.dividendYield > 0` rejects zero values. |

## Phase 3: Yahoo Dividend Normalization Confirmation

| Step | Action | Expected |
|------|--------|----------|
| 1 | Run `dotnet test tests/StockAnalyzer.Core.Tests/ --filter "FullyQualifiedName~StockDataServiceTests"` | All tests pass, including any `ValidateDividendYield` tests. This confirms server-side normalization of Yahoo's inflated dividend yield values is intact. |

## End-to-End: Full Compositing Flow

| Step | Action | Expected |
|------|--------|----------|
| 1 | Start app locally with all three providers configured (TwelveData, FMP, Yahoo API keys in `.env`). | App starts without provider configuration errors in logs. |
| 2 | Search for `AAPL`. | Stock info loads. Key Metrics panel shows MarketCap, PE Ratio, and other fields. |
| 3 | Open browser DevTools, Network tab. Observe the `/api/stock/info/AAPL` response. | Response JSON contains composited fields: `marketCap`, `peRatio`, `currentPrice`, identity fields (`symbol`, `shortName`, `longName`). No field should be unexpectedly null for a major stock like AAPL. |
| 4 | Search for a less-covered stock (e.g., a small-cap or international ticker). | If one provider lacks data for this ticker, the composite should still return fields from whichever providers have data. Some fields may be null, but the response should not be entirely null unless all providers fail. |
| 5 | Intentionally break one provider's API key (e.g., set `TWELVEDATA_API_KEY=invalid` in `.env`, restart). Search for `MSFT`. | App still returns stock info from remaining providers. No 500 error. Key Metrics panel displays data from FMP/Yahoo. Logs may show a warning about TwelveData failure but no crash. |
| 6 | Restore the API key and restart. | Normal operation resumes. |

## Traceability

| Acceptance Criterion | Automated Test | Manual Step |
|----------------------|----------------|-------------|
| AC1.1 - TwelveData null MarketCap filled by FMP | `GetStockInfoAsync_WhenTwelveDataNullMarketCapAndFmpHasValue_CompositeContainsFmpMarketCap` | -- |
| AC1.2 - TwelveData null PeRatio filled by FMP | `GetStockInfoAsync_WhenTwelveDataNullPeRatioAndFmpHasValue_CompositeContainsFmpPeRatio` | -- |
| AC1.3 - Only Yahoo available | `GetStockInfoAsync_WhenOnlyYahooAvailable_CompositeContainsYahooValues` | -- |
| AC1.4 - No provider returns MarketCap | `GetStockInfoAsync_WhenAllProvidersReturnNullMarketCap_CompositeMarketCapIsNull` | -- |
| AC1.5 - Priority wins | `GetStockInfoAsync_WhenMultipleProvidersReturnMarketCap_HighestPriorityProviderWins` | -- |
| AC2.1 - Dividend yield row visible | -- | Phase 1, Steps 2-3 |
| AC2.2 - Formatted as percentage | -- | Phase 1, Step 4 |
| AC2.3 - Null hides row | -- | Phase 1, Steps 5-6 |
| AC2.4 - Zero hides row | -- | Phase 2, Steps 1-3 |
| AC2.5 - Yahoo normalization intact | -- | Phase 3, Step 1 |
| AC3.1 - Per-field compositing | `GetStockInfoAsync_WhenProvidersHaveDifferentFields_CompositeIncludesAllFields` | -- |
| AC3.2 - Identity from primary | `GetStockInfoAsync_WhenIdentityFieldsDifferAcrossProviders_UsesIdentityFromPrimaryProvider` | -- |
| AC3.3 - Provider failure resilience | `GetStockInfoAsync_WhenOneProviderThrows_OtherProvidersFillInWithoutError` | End-to-End Step 5 |
| AC3.4 - All providers fail | `GetStockInfoAsync_WhenAllProvidersFail_ReturnsNull` | -- |
| AC3.5 - Single provider pass-through | `GetStockInfoAsync_WhenOnlyOneProviderAvailable_ReturnsThatProviderResultAsIs` | -- |
