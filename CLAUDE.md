<!-- GENERATED FILE — DO NOT EDIT. -->
<!-- Shared rules: claude-env/shared/claude-md/. Project rules: CLAUDE.local.md. -->
<!-- Regenerate: helpers/sync-claude-md.sh <repo> -->


# Git Flow (develop → main)

<!-- Canonical source: claude-env/shared/claude-md/git-flow-develop-main.md. -->
<!-- Branch names are parameterized: develop / main. -->
<!-- Repos that do not follow this flow (e.g. a single-trunk `master` model) should -->
<!-- omit this fragment and document their flow in CLAUDE.local.md. -->

## Critical Git Checkpoints

| Checkpoint | Rule | Enforcement |
|------------|------|-------------|
| **COMMITS** | Show status → diff → log → message → WAIT for explicit approval. A question is NOT approval. | Hook reminds; manual |
| **main BRANCH** | NEVER commit, merge, push --force, or rebase on `main`. | **BLOCKED** |
| **REVERSE MERGE** | NEVER merge `main` INTO `develop` (flow is `develop` → `main` only). | **BLOCKED** |
| **PR MERGE** | Patrick merges via GitHub web only — NEVER use `gh pr merge`. | **BLOCKED** |
| **MERGED PRs** | NEVER edit/push to merged/closed PRs. Always create a NEW PR. | **BLOCKED** |
| **NO RESET --HARD** | NEVER run `git reset --hard` (it destroyed uncommitted work once). Use `git merge`/`git rebase` to sync; `git stash` first if the tree is dirty. | **BLOCKED** |

## Branching Strategy

```
develop (work here) → PR → main (production)
                                  ↑
                           NEVER reverse this
```

- **Feature branches** for: new services, architecture changes, multi-file refactors, big UI changes, multi-session work, 5+ files.
- **Direct on `develop`** for: small fixes, tweaks, internal docs.
- **NEVER** commit directly to `main`, merge to it via CLI, deploy without an explicit "deploy", or click "Update branch" on the GitHub PR page.
- Before branching: `git fetch origin` and check `git log origin/main..develop` — never assume branches are in sync, and never offer to reuse the current branch without confirming it isn't `main`.

### Forbidden Operations (on develop)
| Operation | Why |
|-----------|-----|
| `git merge main` | `develop` flows TO `main` only |
| `git pull origin main` | Pulls and merges `main` into `develop` |
| `git rebase main` | Rewrites `develop` history based on `main` |

If the branches diverge, merge `develop` into `main` via PR — never the reverse.

## PR Rules

**Verification — when asked to check a PR:**
1. `git fetch origin` (ALWAYS fetch first).
2. `git log origin/main..develop --oneline` (ALWAYS `origin/main`, not local).
3. `gh pr view <N> --json commits` to see what's in the PR.
4. Report the delta — never just update PR title/body. Never assert PR state from memory; confirm with `gh pr view`.

**Merged PRs** — once merged/closed, a PR is DEAD. After any `git push`:
1. Check `gh pr list --head develop --base main --state open`.
2. No open PR → create a NEW one. Never reference old PR numbers without checking state. If Patrick is deploying, the previous PR is already merged — create a new PR for any follow-up fix.

## Pre-Commit Protocol

Before every commit, show Patrick:
1. `git status` — staged, unstaged, untracked
2. `git diff` — actual changes
3. `git log -3` — recent commits for style
4. Planned commit message
5. What will NOT happen (no `main`, no deploy, no PR)

Then **WAIT for explicit approval**. A question or comment resets the checkpoint — answer it, then wait again. Also verify: `claudeLog.md` updated, all files staged, feature tested.

# stock-analyzer — project-specific

<!-- Project-specific rules. Universal rules + git flow (develop→main) + web/Azure -->
<!-- stack rules above are assembled from claude-env/shared/claude-md/ by -->
<!-- sync-claude-md.sh. Edit THIS file (or the shared fragments) — never edit the -->
<!-- generated CLAUDE.md. eodhd-loader/CLAUDE.md is a separate module-context file, -->
<!-- intentionally NOT part of the shared layer. -->

Last verified: 2026-06-14

## Project Checkpoints (stock-analyzer-specific)

The universal behavioral checkpoints, git-flow checkpoints, and the deploy gate come
from the shared fragments above. These are the stock-analyzer-specific ones:

| Checkpoint | Rule | Enforcement |
|------------|------|-------------|
| **SPECS** | Update TECHNICAL_SPEC.md AS you code; stage with code commits. | Advisory — `spec_staleness_guard.py` injects a reminder, it does NOT block. (Previously mislabeled "BLOCKED" — the hook only exits 0.) |
| **EF CORE MIGRATIONS** | DB schema changes use EF Core migrations, never raw SQL scripts. | **BLOCKED** |
| **DTU EXHAUSTION** | Every Azure SQL query must consider DTU limits (5 DTU / 60 workers). No concurrent heavy queries. | Manual |
| **EODHD-LOADER REBUILD** | After committing eodhd-loader changes: kill → rebuild → relaunch. Zero effect until rebuilt. | `eodhd_rebuild_guard.py` reminds |

---

## About

**User:** Patrick — business analyst background, experience with Matlab, Python, Ruby, C# (.NET).
**Project:** Stock Analyzer (.NET) — web application for stock market analysis.

---

## Deployment

### Production Deploy

Pre-deploy checklist:
1. Show Patrick the Bicep file (`infrastructure/azure/main.bicep`)
2. TECHNICAL_SPEC.md + FUNCTIONAL_SPEC.md updated
3. Docs updated in /docs folder
4. Version history updated in specs
5. Security scans passed (CI)
6. User tested on localhost and approved

Deploy: GitHub Actions → "Deploy to Azure Production" → type `deploy` → deploys to https://psfordtaurus.com
Rollback: See `docs/RUNBOOK.md`

### Localhost API Testing

1. Kill ALL dotnet/StockAnalyzer.Api processes and clear port 5000
2. Build: `dotnet build --no-restore -c Release`
3. Start API with redirected stdout/stderr (`dotnet run` spawns child process with different PID)
4. Verify port 5000 listening (check ANY process, not just dotnet PID)
5. Hit an actual endpoint to verify responding
6. Run test suite: `python helpers/test_dtu_endpoints.py`

Pitfalls: Use Python not `Invoke-WebRequest` for HTTP testing. Kill by process name not PID. Write complex PowerShell to `.ps1` files (bash strips `$variable`). Never tell user "start the API" — do it yourself.

### EODHD-Loader Rebuild

After committing eodhd-loader changes:
1. `Get-Process -Name EodhdLoader | Stop-Process -Force`
2. `dotnet build eodhd-loader/src/EodhdLoader/EodhdLoader.csproj -c Release`
3. Relaunch the exe
4. Verify new behavior is visible before claiming "done"

---

## Azure SQL (5 DTU / 60 Workers)

1. Never run multiple sequential heavy queries — consolidate into one
2. **Never scan Prices table (43M+ rows)** — use pre-computed coverage tables (`data.SecurityPriceCoverage`, `data.SecurityPriceCoverageByYear`) for gap analysis and summary aggregation. Coverage is updated incrementally by `BulkInsertAsync` and can be bootstrapped via `POST /api/admin/prices/backfill-coverage`.
3. Compute counts in C#, not SQL
4. Use `WITH (NOLOCK)` for read-only analytics
5. Guard against re-entrancy (timer tick + slow query = cascading exhaustion)
6. Always ask: "What if this runs concurrently with itself?"
7. Coverage table updates are eventually consistent — failures log warnings and do not block price inserts
8. **Future-date guard:** `BulkInsertAsync`, `CreateAsync`, and `ForwardFillHolidaysAsync` reject dates beyond `DateTime.UtcNow.Date`. Prevents bad data from entering the Prices table.

### Database Migrations

EF Core only (never raw SQL). Apply locally after creating:
```powershell
cd src/StockAnalyzer.Api
dotnet ef database update --project ../StockAnalyzer.Core/StockAnalyzer.Core.csproj --startup-project . --connection "Server=.\SQLEXPRESS;Database=StockAnalyzer;Trusted_Connection=True;TrustServerCertificate=True"
```
Production applies on startup. Start local SQL Express: `net start MSSQL$SQLEXPRESS`

**Cross-project entities:** Index attribution tables (`IndexDefinition`, `IndexConstituent`, `SecurityIdentifier`, `SecurityIdentifierHist`) and the `MicExchangeEntity` reference table (ISO 10383, ~2,817 rows) live in `StockAnalyzer.Core` but are populated by `eodhd-loader` or admin endpoints. `SecurityMasterEntity.MicCode` is a char(4) FK to `MicExchangeEntity`. Schema changes to these tables require migration in `StockAnalyzer.Core` and rebuild of `eodhd-loader`. MIC codes are backfilled via `POST /api/admin/securities/backfill-mic-codes` (EODHD exchange-symbol mapping).

**Coverage metadata tables:** `SecurityPriceCoverage` and `SecurityPriceCoverageByYear` live in `StockAnalyzer.Core` (`data` schema) and are populated by `SqlPriceRepository.BulkInsertAsync` (incremental) and the backfill endpoint (bootstrap). These replace direct Prices table scans in gap and refresh-summary endpoints.

---

## Infrastructure Hygiene

- **Verify from source of truth** — check Azure App Service config, never guess resource names
- **Check live Azure state** before recommending changes — Bicep files can be stale
- **Azure CLI path:** `& 'C:\Program Files\Microsoft SDKs\Azure\CLI2\wbin\az.cmd'`
- **Periodic cleanup:** orphaned Azure SQL databases, old container registry tags (keep latest + 5), local orphaned files, storage blobs

### Endpoint Registry

All connection strings and API keys resolve through `EndpointRegistry.Resolve("name")` backed by `endpoints.json` (repo root). Never read env vars directly for endpoint keys.

- **Dev**: Env vars (`WSL_SQL_CONNECTION` plus API keys `TWELVEDATA_API_KEY`, `FMP_API_KEY`, `FINNHUB_API_KEY`, `EODHD_API_KEY`, `MARKETAUX_API_TOKEN`). Note: `SA_DESIGN_CONNECTION` is design-time only (EF Core migrations) and is NOT resolved through the registry. `APPLICATIONINSIGHTS_CONNECTION_STRING` is auto-discovered by the App Insights SDK at startup (not resolved through EndpointRegistry, not listed in `endpoints.json` — the SDK gracefully no-ops when unset).
- **Prod**: Azure Key Vault secrets (vault `kv-stk-{suffix}` — dynamically generated via Bicep, check `az keyvault list --resource-group rg-stock-analyzer` for actual name). Application Insights connection string injected by Bicep (`appi-stockanalyzer-prod`).
- **Resolution**: `EndpointRegistry.Resolve("database")`, `EndpointRegistry.Resolve("twelveData.apiKey")`, etc.
- **Enforcement**: `endpoint_registry_guard.py` (claude-env hook) blocks commits with hardcoded connection strings or direct env var reads for endpoint keys

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

**Version:** When bumping in ROADMAP.md, also update footer in `src/StockAnalyzer.Api/wwwroot/index.html`.

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
