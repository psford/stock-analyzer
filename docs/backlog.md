# Stock Analyzer Backlog

Feature ideas and deferred work items.

---

## Features

- **Chart interaction redesign:** Right-click drag to pan (gaming-style), magnifier icon toggles left-click between measure and bounding box zoom. Designed in `docs/design-plans/2026-04-07-chart-ux.md` brainstorming but deferred to focus on CSS regression fix first.
- **Measurement tool enhancements:** Future iterations could add: pinned measurements that persist across zoom, multi-point measurement, export measurement data.

## Bugs

_(none currently)_

## Technical Debt

- **Remove Tailwind CSS:** The app still uses Tailwind (input.css → npm run build:css → styles.css). Replace with plain CSS to eliminate the Node.js build dependency.
- **Scheduled Azure price refresh:** Replace the desktop crawler tool (built for initial backfill) with a scheduled Azure process that populates daily prices automatically. Ensures the DB stays fresh without manual intervention, which also prevents the staleness-triggered API fallback in `AggregatedStockDataService`.
- **Constrain local SQL Express to match prod buffer pool pressure:** Dev currently hides tier-level performance issues because the local `data.Prices` table (~523 MB / 5.67M rows) fits entirely in SQL Express's 1410 MB default buffer pool, while prod (11.5 GB / 63.8M rows on Azure SQL S0's 1772 MB pool) has ~85% of the table uncacheable. This is how the Performance tile `ReturnCalculationService` bug (PR #11) shipped to prod — the "load entire history" query was instant in dev and catastrophically slow in prod. Fix: set `EXEC sp_configure 'max server memory (MB)', 256; RECONFIGURE` on local SQL Express so the miss ratio roughly matches prod (256 MB cache vs 523 MB table ≈ 49% cacheable, analogous to prod's 1.77 GB vs 11.5 GB ≈ 15%). Requires sysadmin on local instance. Note: this does NOT throttle physical IOPS — prod S0 gets ~20 IOPS while NVMe SSD gets thousands — so it won't perfectly reproduce prod timings, but it WILL catch "query fetches way too much data" bugs by exposing cache misses. Document in `infrastructure/wsl/CLAUDE.md`. Reverse with `sp_configure 'max server memory (MB)', 2147483647`.
- **Extend `prices_scan_guard.py` to catch repo-layer full-history queries:** The existing claude-env hook at `.claude/hooks/prices_scan_guard.py` already flags `_context.Prices.ToListAsync/CountAsync/SumAsync` without a SecurityAlias filter, but it missed the PR #11 bug because the offending code went through `_priceRepo.GetPricesAsync(alias, DateTime.MinValue.AddYears(1), endDate)` — a legitimate-looking repo method call with a "filter" on SecurityAlias. The filter was present but the date range was effectively unbounded. Add two new detection patterns: (1) `DateTime.MinValue` or `DateTime.MaxValue` passed as an argument in any method call (not an assignment, field init, or comparison); (2) calls to a configurable list of "forbidden outside repository layer" method names (e.g., `GetAllPricesAsync`, any `*Async` returning an unbounded list from a known-large table). Config via a new file at repo root like `.claude/large-table-config.json` with `{large_tables, repository_paths, forbidden_outside_repos}`. Hook should skip entirely if config absent, matching `endpoint_registry_guard.py` pattern.

  **Open design decisions** (decide before implementing):
  - **Verify `PRICES_SCAN_ENABLED` is actually set in the stock-analyzer dev environment** — the existing hook is gated on that env var and may have been silently no-op'd. Check `infrastructure/wsl/CLAUDE.md` or session bootstrap scripts. If it's not set, the existing hook has been dead code for however long. Either fix the gating or remove it.
  - **Extend the existing hook vs. write a new one?** Extending keeps surface area small but adds complexity to one file. A new hook focused specifically on "DateTime.MinValue passed as date argument" would be smaller and more targeted. Recommend: extend, with a clear section comment delineating the new patterns.
  - **Config file shape.** Draft: `{large_tables: ["Prices", "SecurityMaster"], repository_paths: ["src/StockAnalyzer.Core/Data/"], forbidden_outside_repos: {"GetAllPricesAsync": "reason", "GetPricesAsync with DateTime.MinValue": "reason"}}`. Consider whether simpler YAML or sentinel markers (e.g., `// LARGE-TABLE-OK: reason`) would work better.
  - **Sanity-check against existing legit `DateTime.MinValue` usage.** Pre-research for today: `grep -rn "DateTime.MinValue" src/` showed ~15 occurrences, all either field initializers, default/fallback values, rate-limiter state, or filter predicates like `.Where(d => d.Date > DateTime.MinValue)`. None were passed as arguments to repo methods. The narrow detection signature ("MinValue in call-argument position, targeting a known-large-table repo method") would have fired on exactly one line — the old ReturnCalculationService code — and nothing else. Good baseline false-positive rate.

- **Runtime test-time guard for unbounded repo queries:** A pre-commit hook only catches bugs written *at commit time*. PR #11's bug was committed in January 2026 and went unnoticed until April because dev hid the symptom and no test exercised the cold-cache path. Complement the pre-commit hook with a runtime check: add a fake `IPriceRepository` test double (or a `DbCommandInterceptor`) that fails any test which issues a query touching more than N rows or spanning more than N years. Wire it into integration tests that exercise production service code paths. This would have caught PR #11's code even if the hook didn't exist, because the `ReturnCalculationService` test would have issued a query with a 30+ year span and failed loudly. Small but high-leverage: a single fake repo plus one test per service that uses it.
