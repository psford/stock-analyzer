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
- **Download Tests**: 11 tests covering AC1 (success, BOM, unknown ETF, timeout, date adjustment)
- **Parsing Tests**: 8 tests covering AC2 (Format A/B, filtering, malformed, null values)
- **Persistence Tests**: 7 tests covering AC3 (matching, SCD Type 2, idempotency, error isolation)
- **Test Framework**: xUnit + Moq + EF Core InMemory provider
- **All Tests**: 26 passing

### Exception Handling
- **Network/Parse**: Caught and logged, return null/empty gracefully
- **Path Resolution**: IOException, SecurityException, UnauthorizedAccessException caught specifically
- **Holding Processing**: Individual failures logged and skipped; batch continues
- **Never Throws**: Public methods return error via stats/return values, never propagate

### Version History
- **Phase 1 (2026-02-23)**: Initial implementation with AC1-AC3 coverage, code review fixes applied

---

## IndexManagerViewModel (Phase 2)

### Overview
`IndexManagerViewModel` provides the WPF UI bindings for the iShares constituent loading tab. Replaces Wikipedia-based index manager with ETF-focused constituent ingestion.

### Constructor & Dependency Injection
- **Pattern**: Transient (`AddTransient<IndexManagerViewModel>()`)
- **Constructor**: `public IndexManagerViewModel(IISharesConstituentService constituentService, ConfigurationService config)`
- **Dependencies**: `IISharesConstituentService`, `ConfigurationService`
- **Removed**: `IndexService`, `StockAnalyzerApiClient` (replaced by Phase 1 service)
- **DI Config**: `App.xaml.cs` line 63

### Observable Properties (AC4)
- **AsOfDate** (DateTime): Defaults to last business day of previous calendar month. User-configurable. Passed to service methods.
- **SelectedEtfTicker** (string?): null or "(All)" = load all; specific ticker = load single ETF.
- **AvailableEtfTickers** (ObservableCollection<string>): Populated from `ISharesConstituentService.EtfConfigs.Keys`, sorted. First entry "(All)" sentinel.
- **CurrentEtfLabel** (string): Display text showing current ETF and progress (e.g., "Loading IVV (3 / 277)...").
- **TotalEtfsToLoad** (int): Total ETF count in current batch.
- **CurrentEtfIndex** (int): 1-based position of current ETF.
- **Progress** (double): 0-100, tracking ETF-level progress (currentEtf / totalEtfs * 100).
- **ProgressText** (string): Per-ETF statistics (e.g., "450 inserted, 50 skipped, 0 failed").
- **IsLoading** (bool): True during IngestAllEtfsAsync or IngestEtfAsync.
- **LogMessages** (ObservableCollection<string>): Timestamped activity log, newest-first, capped at 500 entries.

### Removed Properties
- **SelectedEnvironment**, **SelectedIndex**, **BackfillFromDate**, **BackfillToDate**: Environment switching and date range selection removed.
- **ConstituentCount**, **ProcessedCount**, **ErrorCount**, **IsLoadingConstituents**: Per-index stats replaced by per-ETF progress.
- **AvailableIndices**, **Constituents**, **Environments**: Removed; index/constituent tables no longer exposed.

### Commands (AC4)
1. **LoadAllCommand** → `LoadAllAsync()`:
   - If `SelectedEtfTicker` is null or "(All)", calls `IngestAllEtfsAsync(AsOfDate, token)`.
   - Otherwise calls `IngestEtfAsync(SelectedEtfTicker, AsOfDate, token)`.
   - Sets `IsLoading = true`, subscribes to service events, restores on completion.

2. **CancelCommand** → `Cancel()`:
   - Calls `_cts.Cancel()` to request graceful cancellation.
   - Logs "Cancellation requested — finishing current ETF...".

3. **ClearLogCommand** → `ClearLog()`:
   - Clears `LogMessages` collection.

### Event Wiring (AC4)
- **LogMessage event**: Raises when service has a log-worthy event. ViewModel subscribes and feeds to `Log()` helper.
- **ProgressUpdated event**: Raises with `IngestProgress` model. ViewModel updates `CurrentEtfLabel`, `Progress`, `ProgressText`, `TotalEtfsToLoad`, `CurrentEtfIndex`.

### Helper Methods
- **GetLastMonthEnd()**: Calculates last business day of previous calendar month (e.g., today=2026-02-23 → 2026-01-31, skip weekend).
- **PopulateAvailableEtfs()**: Populates `AvailableEtfTickers` from service config, inserts "(All)" sentinel at position 0.
- **Log(string message)**: Prepends timestamp, inserts at position 0 (newest-first), caps at 500 entries. Handles null dispatcher for test context.

### Dispatcher Handling
- `Log()` checks `App.Current?.Dispatcher` before invoking. In test context (no App instance), falls back to direct collection manipulation.

### Test Coverage (AC4)
- **AC4.1**: LoadAllCommand with no ETF selected calls IngestAllEtfsAsync.
- **AC4.2**: LoadAllCommand with specific ETF selected calls IngestEtfAsync.
- **AC4.3**: AsOfDate defaults to last business day of previous month; user-changeable; passed to service.
- **AC4.4**: LogMessage events captured and displayed newest-first with timestamps.
- **AC4.5**: CancelCommand cancels via CancellationToken, logs cancellation message.
- **AC4.6**: Service exceptions caught, logged, and don't crash ViewModel.
- **All Tests**: 11 passing (xUnit + Moq).

### Version History
- **Phase 2 Task 5-6 (2026-02-23)**: Removed deprecated `IndexService` from DI container; legacy Wikipedia-based index manager completely retired.
- **Phase 2 (2026-02-23)**: ViewModel refactor for iShares constituent loading (AC4.1-AC4.6).
