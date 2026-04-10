# Stock Data Compositing Design

## Summary

The stock data aggregator currently uses sequential fallback — it calls providers one at a time and returns the first complete result, which means a provider that has price data but is missing market cap can silently block a lower-priority provider that has market cap from contributing its value. This design replaces that approach with parallel fetch plus best-source-per-field compositing: all available providers are called simultaneously via `Task.WhenAll`, and a composite builder then walks a static priority matrix to select the highest-priority non-null value for each field independently. The result is a single `StockInfo` object assembled from the best available source for each piece of data, rather than the best available source overall.

The change also fixes a display issue with dividend yield: currently a null or zero yield renders as "N/A" in Key Metrics, which implies the data is missing rather than that the company does not pay dividends. The frontend will omit the dividend yield row entirely when the value is null or zero. Both changes are minimally invasive — existing rate-limiting, caching, error handling, and Yahoo dividend normalization logic are all preserved as-is.

## Definition of Done

1. **Market Cap & P/E Ratio fixed** — The aggregator composites data across providers, filling null fields from lower-priority providers so these values are populated when any provider has them.

2. **Dividend Yield conditional display** — Dividend yield appears in Key Metrics only when the value is non-null and > 0 (indicating dividends were paid in the trailing 12 months). Hidden entirely otherwise — no "N/A" row.

3. **Provider-agnostic** — The compositing approach works generically for all fields, not just these three, so future gaps are automatically filled.

## Acceptance Criteria

### stock-data-compositing.AC1: Market Cap & P/E populated via compositing
- **stock-data-compositing.AC1.1 Success:** When TwelveData returns null for MarketCap and FMP returns a value, the composite StockInfo contains FMP's MarketCap
- **stock-data-compositing.AC1.2 Success:** When TwelveData returns null for PeRatio and FMP returns a value, the composite StockInfo contains FMP's PeRatio
- **stock-data-compositing.AC1.3 Success:** When only Yahoo is available, MarketCap and PeRatio come from Yahoo
- **stock-data-compositing.AC1.4 Failure:** When no provider returns MarketCap, the composite field is null (displayed as N/A)
- **stock-data-compositing.AC1.5 Edge:** When multiple providers return MarketCap, the highest-priority provider's value wins (not the largest number)

### stock-data-compositing.AC2: Dividend Yield conditional display
- **stock-data-compositing.AC2.1 Success:** Stock with positive trailing dividend yield shows the dividend yield row in Key Metrics
- **stock-data-compositing.AC2.2 Success:** Dividend yield is formatted as percentage (e.g., "4.00%")
- **stock-data-compositing.AC2.3 Failure:** Stock with null dividend yield does not show a dividend yield row at all (no N/A)
- **stock-data-compositing.AC2.4 Failure:** Stock with zero dividend yield does not show a dividend yield row
- **stock-data-compositing.AC2.5 Edge:** Yahoo's inflated yield values (>10%) are normalized before display (existing ValidateDividendYield behavior preserved)

### stock-data-compositing.AC3: Provider-agnostic compositing
- **stock-data-compositing.AC3.1 Success:** All nullable fields on StockInfo are composited using the priority matrix, not just MarketCap/PeRatio/DividendYield
- **stock-data-compositing.AC3.2 Success:** Identity fields (Symbol, ShortName, LongName) come from a single primary provider, never mixed across providers
- **stock-data-compositing.AC3.3 Failure:** If a provider throws or times out, its result is null and other providers fill in without error
- **stock-data-compositing.AC3.4 Failure:** If all providers fail, GetStockInfoAsync returns null
- **stock-data-compositing.AC3.5 Edge:** If only one provider is available, its result is used as-is (no composite needed)

## Glossary

- **AggregatedStockDataService**: The .NET service class responsible for coordinating calls to multiple stock data providers and returning a unified `StockInfo` result. The primary class being modified.
- **StockInfo**: The data model (C# record) representing a snapshot of a stock's metrics — price, market cap, ratios, dividend yield, etc. — returned to the frontend.
- **Compositing / Composite Builder**: The pattern of merging partial results from multiple sources by selecting the best available value per field, rather than choosing one source wholesale.
- **Priority Matrix**: A static lookup table mapping each field group on `StockInfo` to an ordered list of provider names. The composite builder consults this to decide which provider's value wins when multiple providers have data for the same field.
- **Sequential Fallback**: The current behavior being replaced — providers are tried one at a time in order, and the first to return a non-null result is used for all fields.
- **TwelveData**: Third-party financial data API. Highest-priority provider for price and volume data.
- **FMP (Financial Modeling Prep)**: Third-party financial data API. Highest-priority provider for market cap, P/E ratio, and moving averages.
- **Yahoo Finance**: Third-party financial data API accessed via OoplesFinance.YahooFinanceAPI NuGet package. Primary source for dividend yield and forward-looking valuation ratios.
- **ValidateDividendYield**: Existing method in `YahooFinanceService` that normalizes Yahoo's dividend yield values when returned inflated (>10%). Preserved as-is in this design.
- **Identity Fields**: Symbol, ShortName, LongName — excluded from per-field compositing to prevent mixing names from different providers.
- **Trailing 12 Months (T12M)**: The period used to calculate dividend yield. A yield of zero or null means no dividends were paid in that window.

## Architecture

Best-source-per-field compositing. The aggregator calls all available providers in parallel, collects their results, then builds a single `StockInfo` by selecting the highest-priority non-null value for each field according to a static priority matrix.

### Priority Matrix

| Field Group | Priority 1 | Priority 2 | Priority 3 |
|-------------|-----------|-----------|-----------|
| Price (current, open, high, low) | TwelveData | FMP | Yahoo |
| Volume / Avg Volume | TwelveData | FMP | Yahoo |
| Market Cap, P/E | FMP | Yahoo | — |
| Forward P/E, PEG, Price/Book | Yahoo | — | — |
| Dividend Yield / Rate | Yahoo | — | — |
| 52-week High/Low | TwelveData | FMP | Yahoo |
| Moving Averages (50d, 200d) | FMP | Yahoo | — |
| Identity (name, exchange) | Primary provider (first to return successfully) | — | — |

### Composite Builder

```csharp
StockInfo CompositeStockInfo(Dictionary<string, StockInfo?> providerResults, FieldPriorityMatrix matrix)
```

The `FieldPriorityMatrix` is a static constant on `AggregatedStockDataService` — a dictionary mapping field names to an ordered list of provider names. For each field, the builder iterates the priority list and takes the first non-null value. Identity fields (Symbol, ShortName, LongName) always come from a single primary provider to avoid mixing names from different sources.

### Flow

1. Call all available providers in parallel via `Task.WhenAll`
2. Collect results into `Dictionary<string, StockInfo?>` keyed by `ProviderName`
3. For each field, walk the priority list and take first non-null value
4. Cache the composite `StockInfo` (same cache TTL as today)

### Dividend Yield Display

Frontend conditionally renders the dividend yield row in Key Metrics. When `dividendYield` is null or zero, the row is omitted entirely — no "N/A" displayed. Yahoo's trailing 12-month dividend yield naturally drops to zero/null when a company stops paying dividends.

### Error Handling

Each provider call is wrapped in individual try/catch. If a provider times out or throws, its result is null — other providers fill in. If ALL providers fail, return null as today. No new failure modes introduced.

## Existing Patterns

Investigation found the current `AggregatedStockDataService.GetStockInfoAsync()` uses sequential fallback — iterating providers by priority and returning on first non-null result. This design replaces that with parallel fetch + priority matrix compositing.

The parallel pattern already exists in the codebase: `FetchHistoricalDataCoreAsync` uses `Task.WhenAll` with per-provider error handling for historical data. The compositing approach follows the same async pattern.

Provider-level rate limiting already exists per service (e.g., `TwelveDataService` tracks credits/minute, `FmpService` tracks daily call count). The compositing design relies on these existing mechanisms — no new rate limiting needed.

The `ValidateDividendYield()` method in `YahooFinanceService` (divides by 100 when Yahoo inflates values above 10%) is preserved as-is. The composite builder consumes already-validated values from each provider's mapping.

## Implementation Phases

<!-- START_PHASE_1 -->
### Phase 1: Composite Builder and Priority Matrix
**Goal:** Replace sequential fallback in `AggregatedStockDataService` with parallel fetch + best-source compositing

**Components:**
- Priority matrix constant on `AggregatedStockDataService` in `src/StockAnalyzer.Core/Services/AggregatedStockDataService.cs` — maps field groups to ordered provider lists
- `CompositeStockInfo` private static method on `AggregatedStockDataService` — builds composite `StockInfo` from provider results using the matrix
- Modified `GetStockInfoAsync` — calls all available providers in parallel, passes results to composite builder, caches result

**Dependencies:** None

**Done when:** Composite builder correctly selects highest-priority non-null value per field. When TwelveData returns price but null market cap, and FMP returns market cap, the composite includes both. Tests verify priority selection, null handling, all-providers-fail case, and single-provider-available case. Covers `stock-data-compositing.AC1.*` and `stock-data-compositing.AC3.*`.
<!-- END_PHASE_1 -->

<!-- START_PHASE_2 -->
### Phase 2: Conditional Dividend Yield Display
**Goal:** Hide dividend yield row when no dividend has been paid in the trailing 12 months

**Components:**
- `renderKeyMetrics()` in `src/StockAnalyzer.Api/wwwroot/js/app.js` — conditionally include dividend yield entry only when value is truthy and > 0

**Dependencies:** Phase 1 (compositing ensures Yahoo's dividend yield reaches the frontend)

**Done when:** Dividend yield row appears for dividend-paying stocks, is completely absent for non-payers. No "N/A" row for dividend yield. Covers `stock-data-compositing.AC2.*`.
<!-- END_PHASE_2 -->

## Additional Considerations

**Provider availability:** If a provider's API key is missing or rate limit is exhausted, `IsAvailable` returns false and it's excluded from the parallel call set. The composite still works with whatever providers are available — graceful degradation by design.

**Cache invalidation:** No change to cache behavior. The composite result is cached with the same TTL. Cache misses trigger the full parallel fetch + composite cycle.
