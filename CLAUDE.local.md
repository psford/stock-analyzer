# stock-analyzer — project-specific

<!-- Project-specific rules. Universal rules + git flow (develop→main) + web/Azure -->
<!-- stack rules above are assembled from claude-env/shared/claude-md/ by -->
<!-- sync-claude-md.sh. Edit THIS file (or the shared fragments) — never edit the -->
<!-- generated CLAUDE.md. eodhd-loader/CLAUDE.md is a separate module-context file, -->
<!-- intentionally NOT part of the shared layer. -->

Last verified: 2026-06-14

A rule written as `specs/<file>.md#<section>` is held by that section of the spec corpus, in claude-harness's `plugins/psford-tickets/specs/`. Read the section before acting on the rule.

## Project Checkpoints (stock-analyzer-specific)

The universal behavioral checkpoints, git-flow checkpoints, and the deploy gate come
from the shared fragments above. These are the stock-analyzer-specific ones:

- **SPECS, updated in the same commits as the code:** `specs/code.md#docs-move-with-the-code`. `spec_staleness_guard.py` reminds; it does not block.
- **EF CORE MIGRATIONS, never raw SQL:** `specs/database.md#migrations`
- **DTU EXHAUSTION, one heavy query at a time:** `specs/database.md#stock-analyzer-sql-budget`

| Checkpoint | Rule | Enforcement |
|------------|------|-------------|
| **EODHD-LOADER REBUILD** | After committing eodhd-loader changes: kill → rebuild → relaunch. Zero effect until rebuilt. | `eodhd_rebuild_guard.py` reminds |

---

## About

**User:** Patrick — business analyst background, experience with Matlab, Python, Ruby, C# (.NET).
**Project:** Stock Analyzer (.NET) — web application for stock market analysis.

---

## Deployment

### Production Deploy

- **The pre-deploy checklist, and Patrick's run of "Deploy to Azure Production":** `specs/deployment.md#stock-analyzer-deploys`
- The workflow deploys to https://psfordtaurus.com. Rollback: see `docs/RUNBOOK.md`.

### Localhost API Testing

- **Kill by name, build, start with redirected output, check port 5000, hit a real endpoint, run `test_dtu_endpoints.py`:** `specs/testing.md#stock-analyzer-local-api-checks`

### EODHD-Loader Rebuild

After committing eodhd-loader changes:
1. `Get-Process -Name EodhdLoader | Stop-Process -Force`
2. `dotnet build eodhd-loader/src/EodhdLoader/EodhdLoader.csproj -c Release`
3. Relaunch the exe
4. Verify new behavior is visible before claiming "done"

---

## Azure SQL

- **Consolidated heavy queries, no scan of Prices, counts in C#, `NOLOCK` for read-only analytics, and re-entrancy guarded:** `specs/database.md#stock-analyzer-sql-budget`
- The Prices table holds 43M+ rows. The coverage tables (`data.SecurityPriceCoverage`, `data.SecurityPriceCoverageByYear`) are updated incrementally by `BulkInsertAsync` and can be bootstrapped via `POST /api/admin/prices/backfill-coverage`.
- Coverage table updates are eventually consistent — failures log warnings and do not block price inserts
- **Future-date guard:** `BulkInsertAsync`, `CreateAsync`, and `ForwardFillHolidaysAsync` reject dates beyond `DateTime.UtcNow.Date`. Prevents bad data from entering the Prices table.

### Database Migrations

- **EF Core only, applied locally with the command below and on startup in production; an index-attribution schema change also rebuilds `eodhd-loader`:** `specs/database.md#migrations`

The local command:
```powershell
cd src/StockAnalyzer.Api
dotnet ef database update --project ../StockAnalyzer.Core/StockAnalyzer.Core.csproj --startup-project . --connection "Server=.\SQLEXPRESS;Database=StockAnalyzer;Trusted_Connection=True;TrustServerCertificate=True"
```
Start local SQL Express: `net start MSSQL$SQLEXPRESS`

**Cross-project entities:** Index attribution tables (`IndexDefinition`, `IndexConstituent`, `SecurityIdentifier`, `SecurityIdentifierHist`) and the `MicExchangeEntity` reference table (ISO 10383, ~2,817 rows) live in `StockAnalyzer.Core` but are populated by `eodhd-loader` or admin endpoints. `SecurityMasterEntity.MicCode` is a char(4) FK to `MicExchangeEntity`. MIC codes are backfilled via `POST /api/admin/securities/backfill-mic-codes` (EODHD exchange-symbol mapping).

**Coverage metadata tables:** `SecurityPriceCoverage` and `SecurityPriceCoverageByYear` live in `StockAnalyzer.Core` (`data` schema) and are populated by `SqlPriceRepository.BulkInsertAsync` (incremental) and the backfill endpoint (bootstrap). These replace direct Prices table scans in gap and refresh-summary endpoints.

---

## Infrastructure Hygiene

- **The live Azure state over the Bicep file, no guessed resource names, and periodic cleanup keeping the latest five registry tags:** `specs/deployment.md#azure`
- **Azure CLI path:** `& 'C:\Program Files\Microsoft SDKs\Azure\CLI2\wbin\az.cmd'`

### Endpoint Registry

- **Every connection string and API key resolves through `EndpointRegistry.Resolve("name")` over `endpoints.json`, never a direct env var read:** `specs/api-design.md#endpoint-registry`
- **Dev**: Env vars (`WSL_SQL_CONNECTION` plus API keys `TWELVEDATA_API_KEY`, `FMP_API_KEY`, `FINNHUB_API_KEY`, `EODHD_API_KEY`, `MARKETAUX_API_TOKEN`). Note: `SA_DESIGN_CONNECTION` is design-time only (EF Core migrations) and is NOT resolved through the registry. `APPLICATIONINSIGHTS_CONNECTION_STRING` is auto-discovered by the App Insights SDK at startup (not resolved through EndpointRegistry, not listed in `endpoints.json` — the SDK gracefully no-ops when unset).
- **Prod**: Azure Key Vault secrets (vault `kv-stockanalyzer-prod`, in resource group `rg-stockanalyzer-prod`). Application Insights connection string injected by Bicep (`appi-stockanalyzer-prod`).
- **Resolution**: `EndpointRegistry.Resolve("database")`, `EndpointRegistry.Resolve("twelveData.apiKey")`, etc.
- **Enforcement**: none today. claude-env ships `endpoint_registry_guard.py`, but no settings file wires it (checked 2026-09-26).

### WSL2 Claude Code Sandbox

WSL2 provides an isolated Linux environment for Claude Code.

**Environment variables (set in `.env`):** Referenced by `endpoints.json` for dev environment resolution.

| Variable | Purpose |
|----------|---------|
| `WSL_SQL_CONNECTION` | TCP connection string to Windows SQL Express (`wsl_claude` login) |
| `SA_DESIGN_CONNECTION` | TCP connection string for EF Core migrations (`wsl_claude_admin` login, DDL permissions) |

Both fall back to Windows defaults (appsettings / localdb) when unset, so Windows development is unaffected.

**SQL logins:** `wsl_claude` (read/write, no DDL) and `wsl_claude_admin` (DDL for migrations). Created on Windows SQL Express for TCP access from WSL2.

**Hooks:** `.claude/hooks/eodhd_rebuild_guard.py` detects WSL2 (`/proc/version`) and adjusts its message (cannot rebuild WPF app from Linux).

---

## Project Files

| File | Purpose |
|------|---------|
| `CLAUDE.md` | Rules and shared knowledge |
| `sessionState.md` | Current session context |
| `claudeLog.md` | Action log |
| `whileYouWereAway.md` | Task queue |
| `ROADMAP.md` | Feature roadmap |
| `FUNCTIONAL_SPEC.md` | User requirements in `docs/` |
| `TECHNICAL_SPEC.md` | Technical details in `docs/` |
| `endpoints.json` | Single source of truth for all remote resource endpoints (DB, APIs, blob) |
| `src/StockAnalyzer.Api/EndpointRegistry.cs` | Static resolver for endpoints.json (env vars, Key Vault) |
| `helpers/` | Python scripts (theme management, DTU testing, CI helpers) |
| `docs/RUNBOOK.md` | Deployment and rollback procedures |
| `docs/decisions.md` | Product and architecture decisions |
| `.env` | API keys — not committed |

---

## Stock Analyzer Specific

**GitHub Pages docs:** Served from https://psford.github.io/stock-analyzer/. App's /docs.html fetches from there.

**Version bumps in ROADMAP.md, and the footer that follows them:** `specs/code.md#docs-move-with-the-code`

**±5% Significant Move Markers:** Include: triangle markers, toggle checkbox, Wikipedia-style hover cards, cat/dog image toggle, news content.

**Themes:** JSON files on Azure Blob (`stockanalyzerblob.z13.web.core.windows.net/themes/`). Manage with `python helpers/theme_manager.py` (list, preview, create, validate, deploy, upload --all). Structure: `variables` (94+ CSS props), `effects` (scanlines, bloom, rain, vignette), `fonts`.

**EODHD Loader:** WPF app in `eodhd-loader/src/EodhdLoader/`. References `StockAnalyzer.Core` at `../../../src/StockAnalyzer.Core/StockAnalyzer.Core.csproj`. Populates index constituents, MIC codes, and daily prices. Must be rebuilt after committing changes — see EODHD-Loader Rebuild section.

**Stock Data Aggregation (AggregatedStockDataService):**
- **Quote fetching** uses parallel fetch + per-field compositing. All available providers are called simultaneously via `Task.WhenAll`. Fields are composited using a static priority matrix -- each field comes from the highest-priority provider that returns a non-null value. Identity fields (Symbol, ShortName, LongName) come from the primary provider only.
- **Priority matrix field groups:** Price, Volume, MarketCapPe, ForwardValuation, Dividend, FiftyTwoWeek, MovingAverages, CompanyInfo. Priority order varies by group (e.g., Price: TwelveData > FMP > Yahoo; ForwardValuation: Yahoo only).
- **Historical data and search** still use sequential fallback for compatibility.
- **Frontend dividend yield:** Only displayed when value is non-null and positive (no "N/A" row for non-dividend stocks).

**Price Data Operations:**
- **PriceRefreshService** runs daily (including weekends). Uses `data.BusinessCalendar` (SourceId=1 = US market) instead of hardcoded weekday logic. `RunDailyRefreshCycleAsync` does a 14-day lookback, fetches missing business days from EODHD, then forward-fills holidays.
- **BackfillGapsAsync** orchestrator audits all securities for missing business days via coverage tables, fetches concurrently from EODHD (configurable concurrency, default 3), and flags securities with `IsEodhdUnavailable = true` when EODHD returns no data after repeated attempts.
- **Admin endpoint:** `POST /api/admin/prices/backfill-gaps?maxConcurrency=N` triggers gap-aware backfill.
- **Application Insights** telemetry enabled via `AddApplicationInsightsTelemetry()`. Infra: Bicep provisions `log-stockanalyzer-prod` (Log Analytics) + `appi-stockanalyzer-prod` (App Insights).

---

## Deprecated

- **Python stock_analysis** — Archived
- **yfinance dividend yield** — Archived
