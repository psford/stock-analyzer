# EODHD Loader

Last verified: 2026-02-26

## Purpose

Standalone WPF desktop tool for managing stock data pipelines. Primary function: download iShares ETF constituent data, match to SecurityMaster, persist index membership snapshots. Secondary: trigger price backfills via Stock Analyzer API.

## Contracts

- **Exposes**: `IISharesConstituentService` interface (download, parse, persist, staleness detection)
- **Guarantees**: 2-second minimum gap between iShares HTTP requests (`RequestDelayMs = 2000`). Idempotent constituent inserts via composite unique key. SCD Type 2 history for identifier changes. Weekend dates adjusted to last business day.
- **Expects**: Local SQL Express with StockAnalyzer database. EF Core migrations applied (runs against `StockAnalyzerDbContext`). `.env` file with `EODHD_API_KEY`.

## Dependencies

- **Uses**: `StockAnalyzer.Core` (DbContext, entities), iShares public JSON API, EODHD Fundamentals API
- **Used by**: None (standalone desktop app)
- **Shared schema**: 4 entities added to `StockAnalyzerDbContext` -- `IndexDefinition`, `IndexConstituent`, `SecurityIdentifier`, `SecurityIdentifierHist` (all in `data` schema). Also reads `MicExchangeEntity` (ISO 10383 reference table, PK = MicCode char(4)).
- **DbContext also includes**: `SecurityPriceCoverage`, `SecurityPriceCoverageByYear` (populated by Stock Analyzer API, not eodhd-loader -- read-only from this app's perspective)
- **Boundary**: Does NOT write to production database directly. Price backfills go through Stock Analyzer API.
- **DTO contract**: `SecurityMasterCreateDto.Exchange` was renamed to `MicCode` (ISO 10383). `ISharesConstituentService` sets `MicCode = null` when creating securities -- actual MIC codes are backfilled by the Stock Analyzer API's `POST /api/admin/securities/backfill-mic-codes` endpoint.

## Key Decisions

- iShares JSON over Wikipedia scraping: Structured data, weight/sector included, 277+ ETFs vs 10 indices
- Typed HttpClient DI: `AddHttpClient<IISharesConstituentService, ISharesConstituentService>()` for timeout/header control
- Crawler pre-step: Constituent staleness check runs automatically before gap filling (best-effort, failures do not block crawl)
- EF Core InMemory for tests: 61 tests use InMemory provider, no SQL dependency

## Invariants

- Rate limiting: consecutive iShares requests always >= 2 seconds apart
- Constituent uniqueness: `(IndexId, SecurityAlias, EffectiveDate, SourceId)` composite key prevents duplicates
- Security matching priority: Ticker > CUSIP > ISIN (3-level lookup)
- IndexService (Wikipedia scraper) is deleted -- no Wikipedia references remain in src/

## Key Files

- `Services/IISharesConstituentService.cs` -- Public interface (IngestEtfAsync, IngestAllEtfsAsync, GetStaleEtfsAsync)
- `Services/ISharesConstituentService.cs` -- Implementation (843 lines, download + parse + persist)
- `ViewModels/IndexManagerViewModel.cs` -- UI for manual ETF loading
- `ViewModels/CrawlerViewModel.cs` -- Autonomous crawl with constituent pre-step
- `Resources/ishares_etf_configs.json` -- 277+ ETF configurations (productId, slug, indexCode)
- `Utilities/DateUtilities.cs` -- Shared month-end calculation
- `docs/TECHNICAL_SPEC.md` -- Detailed AC-level specification

## Gotchas

- WPF rebuild required: Code changes have zero effect until `dotnet build` and relaunch (no hot reload)
- iShares JSON has two formats: Format A (17 cols, broad ETFs) and Format B (19 cols, S&P-style) -- auto-detected
- JSON may have UTF-8 BOM prefix that must be stripped before parsing
- CrawlerViewModel.AddActivity handles null Dispatcher for test context (no WPF app instance)
- Tests use `InternalsVisibleTo` for testing `CheckAndLoadConstituentsAsync` (internal method)
