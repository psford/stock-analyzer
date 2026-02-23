# EODHD Loader - Technical Specification

## ISharesConstituentService (Phase 1)

### Overview
`ISharesConstituentService` downloads iShares ETF holdings from the iShares API, parses JSON (auto-detecting Format A/B), and persists constituent data to SQL Server via EF Core.

### Constructor & Dependency Injection
- **Pattern**: Typed HttpClient (`AddHttpClient<IISharesConstituentService, ISharesConstituentService>()`)
- **Constructor**: `public ISharesConstituentService(HttpClient httpClient, StockAnalyzerDbContext dbContext)`
- **DI Config**: `App.xaml.cs` line 32
- **Timeout**: 60 seconds per AC1.4
- **User-Agent**: `"StockAnalyzer/1.0 (academic-research; single-concurrency; 2s-gap)"`

### JSON Download (AC1)
- **Endpoint**: `https://www.ishares.com/us/products/{productId}/{slug}/1467271812596.ajax?fileType=json&tab=all&asOfDate={YYYYMMDD}`
- **Error Handling**: Network errors, timeouts, malformed JSON all return null gracefully (no exceptions)
- **BOM Handling**: UTF-8 BOM (`\uFEFF`) stripped before parsing
- **Date Adjustment**: Weekend dates adjusted to previous Friday (AC1.5)

### Holdings Parsing (AC2)
- **Auto-Detection**: Format A (17 cols, broad ETFs) vs Format B (19 cols, S&P style)
  - Format A: `col[4]` is object `{display, raw}`
  - Format B: `col[4]` is string (asset class)
- **Non-Equity Filtering**: Excludes "Cash", "Cash Collateral and Margins", "Cash and/or Derivatives", "Futures", "Money Market"
- **Null/Empty Handling**: Rows with missing asset class are included (not filtered)
- **Error Tracking**: Malformed rows counted and logged via `ParseHoldings` return tuple `(List<ISharesHolding>, int SkippedRows)`
- **Return Type**: Tuple of holdings list and skip count for diagnostics

### Database Persistence (AC3)
- **3-Level Matching**: Ticker → CUSIP → ISIN (in that priority)
- **Security Creation**: New securities created with proper fields (PrimaryAssetId, IssueName, etc.)
- **Identifier Upsert**: SCD Type 2 history tracking when identifier values change
- **Constituent Insert**: Idempotent via composite unique key `(IndexId, SecurityAlias, EffectiveDate, SourceId)`
- **Error Isolation**: Individual holding failures don't abort batch; stats track failures separately
- **Return Value**: `IngestStats` includes IdentifiersSet count for audit trail

### Statistics Tracking (IngestStats)
- `Parsed`: Holdings extracted from JSON
- `Matched`: Existing securities matched via 3-level lookup
- `Created`: New securities created
- `Inserted`: IndexConstituent rows inserted
- `SkippedExisting`: Duplicate constituents skipped
- `Failed`: Holdings that failed to process
- `IdentifiersSet`: Count of security identifiers upserted (new or updated)

### Stale ETF Detection
- **Method**: `GetStaleEtfsAsync()`
- **Logic**: Compares latest IndexConstituent date against previous month-end
- **Month-End Calculation**: `firstOfMonth.AddDays(-1)` for previous month, then adjusted to last business day
- **Return**: List of ETF tickers missing latest month-end data

### Rate Limiting
- **Constant**: `RequestDelayMs = 2000` (2 seconds between requests)
- **Implementation**: `IngestAllEtfsAsync` inserts delay between ETF downloads
- **External Reference**: `CrawlerViewModel` references this constant instead of hardcoding

### Test Coverage
- **Download Tests**: 6 tests covering AC1 (success, BOM, unknown ETF, timeout, date adjustment)
- **Parsing Tests**: 12 tests covering AC2 (Format A/B, filtering, malformed, null values)
- **Persistence Tests**: 8 tests covering AC3 (matching, SCD Type 2, idempotency, error isolation)
- **Test Framework**: xUnit + Moq + EF Core InMemory provider
- **All Tests**: 26 passing

### Exception Handling
- **Network/Parse**: Caught and logged, return null/empty gracefully
- **Path Resolution**: IOException, SecurityException, UnauthorizedAccessException caught specifically
- **Holding Processing**: Individual failures logged and skipped; batch continues
- **Never Throws**: Public methods return error via stats/return values, never propagate

### Version History
- **Phase 1 (2026-02-23)**: Initial implementation with AC1-AC3 coverage, code review fixes applied
