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
