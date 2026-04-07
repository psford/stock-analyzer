# CLAUDE.md

Instructions and shared knowledge for Stock Analyzer development.

---

## CRITICAL CHECKPOINTS (READ FIRST)

Enforced by Claude Code hooks. Violations are blocked automatically.

| Checkpoint | Rule | Enforcement |
|------------|------|-------------|
| **COMMITS** | Show status → diff → log → message → WAIT for explicit approval. A question is NOT approval. | Hook reminds; manual |
| **MAIN BRANCH** | NEVER commit, merge, push --force, or rebase on main | **BLOCKED** |
| **REVERSE MERGE** | NEVER merge main INTO develop (flow is develop → main only) | **BLOCKED** |
| **PR MERGE** | Patrick merges via GitHub web only — NEVER use `gh pr merge` | **BLOCKED** |
| **DEPLOY** | Only when Patrick says "deploy" + pre-deploy checklist complete | Hook reminds; manual |
| **SPECS** | Update TECHNICAL_SPEC.md AS you code, stage with code commits | **BLOCKED** |
| **EF CORE MIGRATIONS** | Database schema changes use EF Core migrations, never raw SQL scripts | **BLOCKED** |
| **MERGED PRs** | NEVER edit/push to merged/closed PRs. Always create a NEW PR. | **BLOCKED** |
| **DTU EXHAUSTION** | Every Azure SQL query must consider DTU limits (5 DTU / 60 workers). No concurrent heavy queries. | Manual |
| **EODHD-LOADER REBUILD** | After committing eodhd-loader changes: kill → rebuild → relaunch. Zero effect until rebuilt. | **Hook reminds** |
| **DIAGNOSE BEFORE FIX** | Diagnose root cause first (inspect, measure, log). NEVER guess. Verify fix before reporting. | Manual |
| **PRODUCT DECISIONS** | When Patrick makes a UX/product decision, implement it. Technical objections only for data loss, security, or irreversibility. Record in `docs/decisions.md`. | Manual |
| **TEST BEFORE SUGGESTING** | NEVER tell user to do something without verifying it works. If you can't test, say so. | Manual |
| **NO RESET --HARD** | NEVER run `git reset --hard`. Use `git merge` or `git rebase` to sync branches. If uncommitted changes exist, `git stash` first. No exceptions. | **BLOCKED** |

**If you're about to commit, deploy, or touch main: STOP and verify these checkpoints first.**

---

## About

Last verified: 2026-04-07

**User:** Patrick — business analyst background, experience with Matlab, Python, Ruby, C# (.NET).
**Project:** Stock Analyzer (.NET) — web application for stock market analysis.

---

## Git Flow

### Branching Strategy

```
develop (work here) → PR → main (production)
                      ↑
               NEVER reverse this
```

| Branch | Purpose | Protection |
|--------|---------|------------|
| `develop` | Working branch | None — commit directly |
| `main` | Production ONLY | PR required, CI must pass |

- **Feature branches** for: new services, architecture changes, multi-file refactors, big UI changes, multi-session work, 5+ files
- **Direct on develop** for: small fixes, tweaks, internal docs
- **NEVER** commit directly to main, merge to main via CLI, deploy without "deploy", or click "Update branch" on GitHub PR page

### Forbidden Operations (on develop)

| Operation | Why |
|-----------|-----|
| `git merge main` | Develop flows TO main only |
| `git pull origin main` | Pulls and merges main into develop |
| `git rebase main` | Rewrites develop history based on main |

If main and develop diverge, merge develop into main via PR — never the reverse.

### PR Rules

**Verification** — When asked to check a PR:
1. `git fetch origin` (ALWAYS fetch first)
2. `git log origin/main..develop --oneline` (ALWAYS origin/main, not local main)
3. `gh pr view <N> --json commits` to see what's in the PR
4. Report the delta — never just update PR title/body

**Merged PRs** — Once merged/closed, a PR is DEAD. After any `git push`:
1. Check: `gh pr list --head develop --base main --state open`
2. No open PR → create NEW one. NEVER reference old PR numbers without checking state.

### Pre-Commit Protocol

Before every commit, show Patrick:
1. `git status` — staged, unstaged, untracked
2. `git diff` — actual changes
3. `git log -3` — recent commits for style
4. Planned commit message
5. What will NOT happen (no main, no deploy, no PR)

Then **WAIT for explicit approval**. A question or comment resets the checkpoint — answer it, then wait again.

Also verify: specs updated (TECHNICAL_SPEC.md always, FUNCTIONAL_SPEC.md for user-facing), claudeLog.md updated, all files staged, feature tested.

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

- **Dev**: Env vars (`WSL_SQL_CONNECTION` plus API keys `TWELVEDATA_API_KEY`, `FMP_API_KEY`, `FINNHUB_API_KEY`, `EODHD_API_KEY`, `MARKETAUX_API_TOKEN`). Note: `SA_DESIGN_CONNECTION` is design-time only (EF Core migrations) and is NOT resolved through the registry.
- **Prod**: Azure Key Vault secrets (vault `kv-stk-{suffix}` — dynamically generated via Bicep, check `az keyvault list --resource-group rg-stock-analyzer` for actual name)
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

## Principles

| Principle | Description |
|-----------|-------------|
| **Rules are hard blocks** | Patrick's rules are HARD BLOCKS. Hooks must fail (non-zero), never warn-and-pass. |
| **Challenge me** | Push back against bad practices or security vulnerabilities. |
| **Admit limitations** | Never pretend capabilities you lack. Say so and suggest mitigations. |
| **UI matches implementation** | Never put placeholder text suggesting unbuilt functionality. |
| **Evaluate all options** | Before saying "no", consider all tools: Bash, PowerShell, web access, APIs, system commands. |
| **Do it yourself** | Work autonomously. Never ask user to do something you can do. Only escalate for commit/deploy approval or genuine capability gaps. |
| **Act on credentials** | When given API keys/passwords, use them directly — don't give instructions back. |
| **Questions require answers** | If asking "Ready to commit?" — STOP and wait. Never ask then immediately act. |
| **No feature regression** | Changes should never lose functionality. |
| **Fix problems immediately** | No technical debt. Fix deprecated code, broken things, suboptimal patterns now. |
| **Flag deprecated APIs** | Use current APIs in new code. Fix straightforward deprecations; flag complex ones. |
| **Update specs proactively** | Update TECHNICAL_SPEC.md, ROADMAP.md as you code, not after. |
| **Commit client with API changes** | Update dependent clients (e.g., eodhd-loader) in same session as backend changes. |
| **Version new behaviors** | Don't overwrite working deployed code — ask first or create new version. |
| **Design prototypes are contracts** | Implement EVERY effect in a prototype. |
| **Test environment readiness** | Before asking user to test: endpoints MUST be running in the target environment. |
| **PowerShell ONLY** | Bash tool runs actual bash. For Windows: `powershell.exe -Command "..."`. Never raw bash syntax. |
| **Prefer FOSS / winget** | MIT/Apache/BSD over proprietary. Lightweight, offline-capable. Use winget for installs. |
| **No paid services** | Never sign up for paid services on Patrick's behalf. |
| **No ad tech/tracking** | No advertising, tracking pixels, or data sharing with X/Meta. |
| **Cite sources** | When making recommendations, cite sources so Patrick can verify. |
| **Respect public APIs** | Rate limit (single-concurrency, 2s gap), cache in DB, polite User-Agent. Wikipedia cached in `data.CompanyBio`. |
| **Log sanitization** | ALL user strings in C# logs wrapped in `LogSanitizer.Sanitize()` (CWE-117). Enforced by hook. |
| **Cross-browser / local CSS** | Standard APIs and CSS only. Locally compiled CSS, CDN only for large libs with SRI hashes. |
| **Fetch before comparing** | ALWAYS `git fetch origin` first. Compare `origin/main` not local `main`. |
| **Validate doc links** | Validate documentation links are correct and working before committing doc changes. |
| **Audit the class** | When a bug is found as "we forgot X in location Y," immediately search for every other location where X might also be missing. Don't fix one instance — fix the class. |
| **Preserve original media** | Never degrade user-uploaded images/media. Store originals at full quality. Use resized/compressed versions for display performance (thumbnails, map previews), but always provide a way to view or download the original. |

---

## Session Protocol

### Starting ("hello!")
1. Read: `CLAUDE.md`, `sessionState.md`, `claudeLog.md`, `whileYouWereAway.md`, `docs/decisions.md`
2. If WYA has tasks, ask about them. Complete one step at a time.

### During
- **Checkpoints:** Save to `sessionState.md` after major tasks, every 10-15 exchanges, before complex work
- **Context efficiency:** Only load files actively needed. Exception: CLAUDE.md always loaded.
- **Plan hygiene:** Delete completed plan files. Verify git state before working from plans.
- **Between tasks:** Review WYA, check ROADMAP, suggest 2-3 items.
- **Slack triggers:** Check after deployments, PR merges, multi-step tasks, idle moments, before reporting "done".
- **Post-compaction:** Track what info was lost, update docs with reusable context that survives compaction.

### Ending ("night!")
1. Update `sessionState.md`
2. Commit pending changes
3. Update `claudeLog.md`

---

## Coding Standards

- C#: PascalCase (classes, methods), camelCase (local variables, parameters)
- JavaScript/TypeScript: `camelCase`
- Python: `snake_case` (PEP 8)
- Docs: GitHub-flavored Markdown
- **Testing:** Code compiling is NOT sufficient. Use Playwright for UI testing. Run responsive tests at mobile (390x844) / tablet (768x1024) / desktop (1400x900) before committing CSS changes. Test external dependencies before integrating.
- **Specs:** Update incrementally as you code, not after. Stage with code commits.

### Model Delegation

| Model | Use for |
|-------|---------|
| **Haiku** | Quick scripts, simple file ops, straightforward fixes, running tests |
| **Sonnet** | General development, coding, debugging (default) |
| **Opus** | Architecture, complex refactors, deep research, system design |

---

## Communication

- **Research before asking** — search the web first, only ask Patrick if unclear
- **Correction vs inquiry** — if Patrick asks "Did you do X?", ask if it should be a guideline
- **Proactive updates** — add feedback-based rules to CLAUDE.md immediately when agreement is reached
- **Slack:** React to every message, mark `read: true` in `slack_inbox.json`, restart listener if disconnected

---

## File Management

- **CLAUDE.md backups:** Save as `claude_MMDDYYYY-N.md` before updating
- **Logging:** Log to `claudeLog.md` with date, description, result. Omit sensitive data.
- **Archives:** Source to `archive/`. Delete `__pycache__`, `node_modules`, `bin/`, `obj/`, logs, temp files.

---

## Security

- **Personal identifiers are secrets.** Personal email addresses, phone numbers, home addresses, and personal domains (e.g., `psford.com`) must be treated as credentials — never hardcoded in source files committed to public repos. Use `example.com` in defaults, documentation, and config templates. Real values belong in `.env` (gitignored) or environment variables only. Support/business emails created for a project are fine.
- Review SAST/DAST coverage when introducing new frameworks (SecurityCodeScan for C#, Bandit for Python)
- Hooks run automatically — if blocked, try to adjust; if stuck, ask Patrick

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

---

## Deprecated

- **Python stock_analysis** — Archived
- **yfinance dividend yield** — Archived
