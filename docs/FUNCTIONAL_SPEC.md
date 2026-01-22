# Functional Specification: Stock Analyzer Dashboard (.NET)

**Version:** 2.4
**Last Updated:** 2026-01-22
**Author:** Claude (AI Assistant)
**Status:** Production
**Audience:** Business Users, Product Owners, QA Testers

---

## 1. Executive Summary

### 1.1 Purpose

The Stock Analyzer Dashboard is a web-based tool that allows users to research publicly traded stocks through interactive charts, company information, and news analysis. It helps investors understand price movements by correlating significant market events with relevant news stories.

This document covers the **C#/.NET 8 implementation** with a custom HTML/CSS/JavaScript frontend.

### 1.2 Business Objectives

| Objective | Description |
|-----------|-------------|
| **Inform investment decisions** | Provide clear, visual stock data to support research |
| **Explain price movements** | Connect large price changes with news events |
| **Simplify technical analysis** | Make moving averages and trends accessible to non-experts |
| **Save research time** | Consolidate stock info, charts, and news in one place |

### 1.3 Target Users

| User Type | Description | Primary Use Case |
|-----------|-------------|------------------|
| Individual Investor | Person managing personal portfolio | Research stocks before buying/selling |
| Financial Analyst | Professional reviewing securities | Quick visual analysis of price history |
| Student | Learning about markets | Educational exploration of stock behavior |

---

## 2. Product Overview

### 2.1 What the System Does

The Stock Analyzer Dashboard allows users to:

1. **Search** for any publicly traded stock by company name or ticker symbol
2. **View** interactive price charts with customizable time periods
3. **Analyze** price trends using moving average overlays
4. **Identify** significant price movements (days with ±3% change or custom threshold)
5. **Read** news headlines associated with major price swings
6. **Review** key company metrics (P/E ratio, dividend yield, market cap, etc.)

### 2.2 What the System Does NOT Do

- Does not provide real-time streaming prices (data has ~15 minute delay)
- Does not execute trades or connect to brokerage accounts
- Does not provide investment recommendations or advice
- Does not require user registration or login (single-user mode; multi-user ready)

---

## 3. Functional Requirements

### 3.1 Stock Search (FR-001)

| ID | Requirement |
|----|-------------|
| FR-001.1 | The system must allow users to search for stocks by typing in a search box |
| FR-001.2 | The system must display search results as the user types (autocomplete) |
| FR-001.3 | The system must show both ticker symbol and company name in search results |
| FR-001.4 | The system must show the stock exchange for each search result |
| FR-001.5 | The system must require at least 2 characters before showing results |
| FR-001.6 | The system must display up to 10 search results at a time |
| FR-001.7 | The system must debounce search input (300ms delay) |
| FR-001.8 | The system must return search results instantly (sub-millisecond) via client-side search |
| FR-001.9 | The system must load symbol data (~30K symbols) into the browser at page load |
| FR-001.10 | The symbol data file must be served as a static asset (~315KB gzipped) |
| FR-001.11 | The system must fall back to server API if client-side data hasn't loaded yet |
| FR-001.12 | If client-side search returns no results, the system must wait 5 seconds then query the server |
| FR-001.13 | The 5-second server fallback must be debounced (resets if user keeps typing) |
| FR-001.14 | When no local results found, the system must display "No local results. Checking server in a few seconds..." |

**User Story:** *As an investor, I want to search for stocks by company name so that I don't need to memorize ticker symbols.*

**Client-Side Search Architecture:**

```
Page Load                    User Types Query
    │                              │
    ▼                              ▼
┌────────────────┐         ┌─────────────────┐
│ Fetch          │         │ 300ms debounce  │
│ /data/symbols.txt │      │ (input)         │
│ (~315KB gzip)  │         └────────┬────────┘
└───────┬────────┘                  │
        │                          ▼
        ▼                  ┌─────────────────┐
┌────────────────┐         │ Client-side     │
│ Parse into     │         │ SymbolSearch    │
│ 30K symbol     │◄────────│ (instant)       │
│ array          │         └────────┬────────┘
└────────────────┘                  │
                                    ▼
                           ┌─────────────────┐
                           │ Results found?  │
                           └────────┬────────┘
                              Yes   │   No
                           ┌────────┴────────┐
                           │                 │
                           ▼                 ▼
                    ┌────────────┐   ┌─────────────────┐
                    │ Show       │   │ Show "checking  │
                    │ results    │   │ server..." msg  │
                    └────────────┘   └────────┬────────┘
                                              │
                                              ▼
                                     ┌─────────────────┐
                                     │ 5-second wait   │
                                     │ (debounced)     │
                                     └────────┬────────┘
                                              │
                                              ▼
                                     ┌─────────────────┐
                                     │ Server API call │
                                     │ /api/search     │
                                     └────────┬────────┘
                                              │
                                              ▼
                                     ┌─────────────────┐
                                     │ Show server     │
                                     │ results (if any)│
                                     └─────────────────┘
```

**Symbol Data Format:**

| Field | Format | Example |
|-------|--------|---------|
| File | Pipe-delimited text | `AAPL|APPLE INC\n` |
| Location | `/data/symbols.txt` | Static asset in wwwroot |
| Size | ~857KB uncompressed, ~315KB gzipped | Regenerated at startup |
| Refresh | Updated daily at 2 AM UTC (Finnhub refresh) | Server regenerates file |

### 3.2 Chart Display (FR-002)

| ID | Requirement |
|----|-------------|
| FR-002.1 | The system must display an interactive price chart for the selected stock |
| FR-002.2 | The system must support Candlestick chart type |
| FR-002.3 | The system must support Line chart type |
| FR-002.4 | The system must allow users to switch between chart types |
| FR-002.5 | The system must allow zooming via mouse scroll or touch pinch |
| FR-002.6 | The system must allow panning via mouse drag or touch swipe |
| FR-002.7 | The system must show price details on hover (Open, High, Low, Close) |
| FR-002.8 | The system must display volume bars below candlestick charts |

**User Story:** *As an investor, I want to see an interactive chart so that I can zoom into specific time periods of interest.*

### 3.3 Time Period Selection (FR-003)

| ID | Requirement |
|----|-------------|
| FR-003.1 | The system must allow users to select the chart time period |
| FR-003.2 | The system must support: YTD, 1 month, 3 months, 6 months, 1 year, 2 years, 5 years, 10 years |
| FR-003.3 | The system must default to 1 year when first loading a stock |
| FR-003.4 | The system must refresh the chart when the period is changed |

**Supported Periods:**

| Selection | Data Range |
|-----------|------------|
| YTD | January 1st of current year to today |
| 1mo | Last 30 calendar days |
| 3mo | Last 90 calendar days |
| 6mo | Last 180 calendar days |
| 1y | Last 365 calendar days |
| 2y | Last 730 calendar days |
| 5y | Last 1,825 calendar days |
| 10y | Last 3,650 calendar days |

### 3.4 Moving Averages (FR-004)

| ID | Requirement |
|----|-------------|
| FR-004.1 | The system must support overlay of 20-day moving average |
| FR-004.2 | The system must support overlay of 50-day moving average |
| FR-004.3 | The system must support overlay of 200-day moving average |
| FR-004.4 | The system must allow users to toggle each moving average independently |
| FR-004.5 | The system must display MA-20 and MA-50 by default |
| FR-004.6 | The system must display MA-200 only when user enables it |
| FR-004.7 | The system must use distinct colors for each moving average line |

**Moving Average Colors:**

| Indicator | Color |
|-----------|-------|
| SMA-20 | Orange (#FF9800) |
| SMA-50 | Purple (#9C27B0) |
| SMA-200 | Cyan (#00BCD4) |

### 3.5 Significant Move Detection (FR-005)

| ID | Requirement |
|----|-------------|
| FR-005.1 | The system must identify days where the stock moved by a configurable threshold (default ±5%) |
| FR-005.2 | The system must display significant moves in a dedicated panel |
| FR-005.3 | The system must show the date and percentage change for each move |
| FR-005.4 | The system must color-code moves (green for up, red for down) |
| FR-005.5 | The system must display triangle markers on the chart for significant moves |
| FR-005.6 | The system must allow users to adjust the threshold via a slider (3% to 10%) |
| FR-005.7 | The system must display a Wikipedia-style hover popup when hovering on chart markers |
| FR-005.8 | The hover popup must show: date, return %, news headline, summary, source, and thumbnail |
| FR-005.9 | The hover popup headline must be a clickable link to the full news article |
| FR-005.10 | The hover popup must remain visible when the user moves their mouse to interact with it |
| FR-005.11 | The system must allow users to choose between cat or dog images for popup thumbnails |
| FR-005.12 | The system must pre-cache 50 images of each type on page load for instant display |
| FR-005.13 | The system must automatically refill the image cache when it drops below 10 images |
| FR-005.14 | The system must use each cached image only once (no repeats) |
| FR-005.15 | The system must clear the previous image when hiding the popup to prevent flash |

**Calculation Method:** Daily return = (Today's Close - Today's Open) / Today's Open

**Chart Markers:**
| Direction | Symbol | Color |
|-----------|--------|-------|
| Positive (+N%) | Triangle Up | Green (#10B981) |
| Negative (-N%) | Triangle Down | Red (#EF4444) |

### 3.6 Company Information (FR-006)

| ID | Requirement |
|----|-------------|
| FR-006.1 | The system must display the company's full name as the title |
| FR-006.2 | The system must display the stock exchange and currency |
| FR-006.3 | The system must display the sector when available |
| FR-006.4 | The system must display security identifiers (Ticker, ISIN, CUSIP, SEDOL) when available |
| FR-006.5 | The system must display a company description/bio when available |
| FR-006.6 | The system must truncate long descriptions at sentence boundaries |
| FR-006.7 | The system must display the current price with day change |

### 3.7 Key Metrics (FR-007)

| ID | Requirement |
|----|-------------|
| FR-007.1 | The system must display market capitalization |
| FR-007.2 | The system must display the P/E (Price-to-Earnings) ratio |
| FR-007.3 | The system must display 52-week high and low |
| FR-007.4 | The system must display average volume |
| FR-007.5 | The system must display dividend yield as a percentage |
| FR-007.6 | The system must display "N/A" when a metric is unavailable |

### 3.8 Performance Summary (FR-008)

| ID | Requirement |
|----|-------------|
| FR-008.1 | The system must calculate and display total return for the selected period |
| FR-008.2 | The system must calculate and display annualized volatility |
| FR-008.3 | The system must display the highest close during the selected period |
| FR-008.4 | The system must display the lowest close during the selected period |
| FR-008.5 | The system must display average volume for the period |
| FR-008.6 | The system must color-code return (green for positive, red for negative) |

### 3.9 News Integration (FR-009)

| ID | Requirement |
|----|-------------|
| FR-009.1 | The system must display a "Recent News" section |
| FR-009.2 | The system must show up to 5 recent news articles |
| FR-009.3 | The system must display headline, source, and date for each article |
| FR-009.4 | The system must display article summary when available |
| FR-009.5 | The system must make headlines clickable links to the full article |
| FR-009.6 | The system must display "No recent news available" when no news exists |

### 3.10 Dark Mode (FR-010)

| ID | Requirement |
|----|-------------|
| FR-010.1 | The system must provide a dark mode toggle button in the header |
| FR-010.2 | The system must display a moon icon when in light mode (click to switch to dark) |
| FR-010.3 | The system must display a sun icon when in dark mode (click to switch to light) |
| FR-010.4 | The system must persist the user's dark mode preference across page reloads |
| FR-010.5 | The system must respect the user's system preference (prefers-color-scheme) on first visit |
| FR-010.6 | The system must apply dark mode styling to all UI elements (backgrounds, text, borders) |
| FR-010.7 | The system must apply dark mode styling to the Plotly chart (background, gridlines, text) |
| FR-010.8 | The system must provide smooth color transitions when toggling between modes |

**User Story:** *As a user who works at night, I want a dark mode so that the bright interface doesn't strain my eyes.*

**Color Scheme:**

| Element | Light Mode | Dark Mode |
|---------|------------|-----------|
| Background | Gray-50 (#F9FAFB) | Gray-900 (#111827) |
| Card Background | White (#FFFFFF) | Gray-800 (#1F2937) |
| Primary Text | Gray-900 (#111827) | White (#FFFFFF) |
| Secondary Text | Gray-600 (#4B5563) | Gray-300 (#D1D5DB) |
| Borders | Gray-200 (#E5E7EB) | Gray-700 (#374151) |
| Chart Background | White (#FFFFFF) | Gray-800 (#1F2937) |
| Chart Gridlines | Gray-200 (#E5E7EB) | Gray-700 (#374151) |

### 3.11 Technical Indicators (FR-011)

| ID | Requirement |
|----|-------------|
| FR-011.1 | The system must support RSI (Relative Strength Index) indicator |
| FR-011.2 | The system must support MACD (Moving Average Convergence Divergence) indicator |
| FR-011.3 | The system must support Bollinger Bands indicator |
| FR-011.4 | The system must allow users to toggle RSI display independently |
| FR-011.5 | The system must allow users to toggle MACD display independently |
| FR-011.6 | The system must allow users to toggle Bollinger Bands display independently |
| FR-011.7 | The system must display RSI in a separate panel below the price chart |
| FR-011.8 | The system must display MACD in a separate panel below the price chart |
| FR-011.9 | The system must display Bollinger Bands overlaid on the price chart |
| FR-011.10 | The RSI panel must show overbought (70) and oversold (30) reference lines |
| FR-011.11 | The MACD panel must show the MACD line, signal line, and histogram |
| FR-011.12 | The Bollinger Bands must show upper band, middle band (SMA), and lower band |
| FR-011.13 | The Bollinger Bands must include shaded fill between upper and lower bands |
| FR-011.14 | The chart must dynamically resize to accommodate indicator panels |
| FR-011.15 | The system must use 14-period RSI calculation by default |
| FR-011.16 | The system must use standard MACD parameters (12, 26, 9) by default |
| FR-011.17 | The system must use standard Bollinger Bands parameters (20-period SMA, 2 std dev) |

**User Story:** *As a technical trader, I want to see RSI, MACD, and Bollinger Bands indicators so that I can identify overbought/oversold conditions, momentum trends, and volatility.*

**RSI Configuration:**

| Setting | Value |
|---------|-------|
| Default Period | 14 days |
| Range | 0-100 |
| Overbought Level | 70 |
| Oversold Level | 30 |
| Line Color | Purple (#8B5CF6) |

**MACD Configuration:**

| Setting | Value |
|---------|-------|
| Fast EMA Period | 12 days |
| Slow EMA Period | 26 days |
| Signal Period | 9 days |
| MACD Line Color | Blue (#3B82F6) |
| Signal Line Color | Orange (#F59E0B) |
| Histogram (Positive) | Green (rgba(16, 185, 129, 0.7)) |
| Histogram (Negative) | Red (rgba(239, 68, 68, 0.7)) |

**Bollinger Bands Configuration:**

| Setting | Value |
|---------|-------|
| SMA Period | 20 days |
| Standard Deviations | 2 |
| Band Color | Indigo (#6366F1) |
| Fill Color | Indigo with 10% opacity (rgba(99, 102, 241, 0.1)) |
| Middle Band Style | Dashed line |

**Chart Layout (Dynamic):**

| Configuration | Price Panel | RSI Panel | MACD Panel | Total Height |
|---------------|-------------|-----------|------------|--------------|
| No indicators | 100% | - | - | 400px |
| RSI only | 68% | 28% | - | 550px |
| MACD only | 68% | - | 28% | 550px |
| Both indicators | 50% | 21% | 25% | 700px |

### 3.12 Stock Comparison (FR-012)

| ID | Requirement |
|----|-------------|
| FR-012.1 | The system must allow users to compare the primary stock to a second stock or index |
| FR-012.2 | The system must provide a second search box labeled "Compare to (Optional)" |
| FR-012.3 | The system must provide quick benchmark buttons for SPY, QQQ, and ^DJI |
| FR-012.4 | The system must display both stocks as normalized percentage change from period start |
| FR-012.5 | The system must disable technical indicators (RSI/MACD) when comparing |
| FR-012.6 | The system must provide a "Clear Comparison" button to return to single-stock view |
| FR-012.7 | The system must re-fetch comparison data when the time period changes |
| FR-012.8 | The system must prevent comparing a stock to itself |
| FR-012.9 | The comparison chart must show a zero baseline reference line |
| FR-012.10 | The comparison chart title must show both stock symbols |

**User Story:** *As an investor, I want to compare a stock's performance to major indices so that I can evaluate relative performance.*

**Chart Layout (Comparison Mode):**

```
┌────────────────────────────────────────────────────┐
│  AAPL vs SPY - 1Y                                  │
├────────────────────────────────────────────────────┤
│                                                    │
│  % Change                                          │
│     ^                                              │
│     |    /\    /\          ── Primary (blue)       │
│  +10|   /  \  /  \    ___  -- Compare (orange)     │
│     |  /    \/    \  /   \                        │
│    0|------------------\---/--- (baseline)        │
│     |                   \/                        │
│  -10|                                              │
│     +---------------------------------> Date       │
│                                                    │
└────────────────────────────────────────────────────┘
```

**Colors:**

| Element | Color | Hex |
|---------|-------|-----|
| Primary stock line | Blue (solid) | #3B82F6 |
| Comparison stock line | Orange (dashed) | #F59E0B |
| Baseline (0%) | Gray (dotted) | Theme-dependent |

### 3.13 Documentation Page (FR-013)

**Purpose:** Allow users to view project documentation with easy navigation and search.

| ID | Requirement |
|----|-------------|
| FR-013.1 | The system must provide a documentation page accessible via footer link "View Documentation" |
| FR-013.2 | The documentation page must display four tabs: Project Guidelines, Functional Spec, Technical Spec, Architecture |
| FR-013.3 | The system must render Markdown files as formatted HTML using marked.js |
| FR-013.4 | The system must display a Table of Contents (TOC) sidebar generated from document headings |
| FR-013.5 | The system must highlight the currently visible section in the TOC as the user scrolls (scroll spy) |
| FR-013.6 | The system must allow resizing the TOC sidebar via drag handle |
| FR-013.7 | The system must persist the TOC width preference in localStorage |
| FR-013.8 | The system must provide fuzzy search across all documentation using Fuse.js |
| FR-013.9 | The search must display results with highlighted matching terms |
| FR-013.10 | Clicking a search result must navigate to the relevant document and section |
| FR-013.11 | The search must require minimum 2 characters and debounce input (200ms) |
| FR-013.12 | The Architecture tab must display interactive Mermaid.js diagrams |
| FR-013.13 | The system must provide a toggle to show/hide AUTO/MANUAL diagram labels |
| FR-013.14 | The labels toggle must only appear when viewing the Architecture tab |
| FR-013.15 | The labels must be hidden by default |
| FR-013.16 | The documentation page must support dark mode matching the main application |

**User Story:** *As a developer or user, I want to browse project documentation with easy navigation so that I can quickly find the information I need.*

**Page Layout:**

```
┌──────────────────────────────────────────────────────────────────────────┐
│  📄 Documentation              ← Back to Stock Analyzer          [🏷️][🌙]│
├──────────────────────────────────────────────────────────────────────────┤
│  [Guidelines] [Functional] [Technical] [Architecture]    [🔍 Search...]  │
├────────────────┬─────────────────────────────────────────────────────────┤
│                │                                                         │
│  Table of      │   # Document Title                                      │
│  Contents      │                                                         │
│  ───────────   │   ## Section 1                                          │
│  • Section 1   │   Content here...                                       │
│  ▸ Section 2   │                                                         │
│    ∘ Sub 2.1   │   ## Section 2                                          │
│  • Section 3   │   Content here...                                       │
│                │                                                         │
│  (resizable)   │   (scrollable, with scroll spy)                         │
│                │                                                         │
└────────────────┴─────────────────────────────────────────────────────────┘
```

**Architecture Diagrams:**

| Diagram | Type | Description |
|---------|------|-------------|
| Project Structure | AUTO | Solution dependency graph |
| Service Architecture | MANUAL | Backend services and external APIs |
| Data Flow | MANUAL | Sequence diagram for stock lookup |
| Domain Models | MANUAL | Class diagram of core models |
| Image Pipeline | MANUAL | ML-based image processing flow |
| Frontend Architecture | MANUAL | JavaScript modules |
| API Endpoints | MANUAL | REST API reference |

### 3.14 Watchlist Management (FR-014)

**Purpose:** Allow users to save and organize stocks into watchlists for quick access and monitoring, with privacy-first client-side storage.

| ID | Requirement |
|----|-------------|
| FR-014.1 | The system must allow users to create multiple named watchlists |
| FR-014.2 | The system must allow users to rename existing watchlists |
| FR-014.3 | The system must allow users to delete watchlists |
| FR-014.4 | The system must allow users to add tickers to a watchlist |
| FR-014.5 | The system must allow users to remove tickers from a watchlist |
| FR-014.6 | The system must display all watchlists in a collapsible sidebar |
| FR-014.7 | The system must display current price and daily change for each ticker in a watchlist |
| FR-014.8 | The system must allow clicking a ticker in a watchlist to analyze that stock |
| FR-014.9 | The system must provide an "Add to Watchlist" button when a stock is loaded |
| FR-014.10 | The system must persist watchlists in browser localStorage (no server storage) |
| FR-014.11 | The system must NOT collect any personally identifiable information (PII) |
| FR-014.12 | The system must prevent duplicate tickers in the same watchlist |
| FR-014.13 | The system must convert ticker symbols to uppercase |
| FR-014.14 | The system must update the "updatedAt" timestamp when a watchlist is modified |
| FR-014.15 | The system must provide an "Export" button to download watchlists as JSON |
| FR-014.16 | The system must provide an "Import" button to restore watchlists from JSON file |
| FR-014.17 | The system must show storage usage information (percentage of localStorage used) |
| FR-014.18 | The system must warn users if localStorage quota is exceeded |

**User Story:** *As a privacy-conscious investor, I want my watchlist data stored only on my device so that no personal investment information is sent to or stored on any server.*

**Privacy Design Principles:**

| Principle | Implementation |
|-----------|----------------|
| No PII collection | Watchlists stored in browser localStorage only |
| No tracking | No analytics, cookies, or user identification |
| User control | Export/import allows backup and cross-device transfer |
| Transparency | Storage usage displayed to user |

**Sidebar Layout:**

```
┌─────────────────┐
│  MY WATCHLISTS  │
│  ─────────────  │
│  [+ New List]   │
│  [↓ Export] [↑] │
│                 │
│  ▼ Tech Stocks  │
│    AAPL  $150 ↑ │
│    MSFT  $380 ↑ │
│    GOOGL $140 ↓ │
│                 │
│  ▶ Energy       │
│                 │
│  ▶ Financials   │
│─────────────────│
│  Storage: 0.2%  │
└─────────────────┘
```

**Watchlist Data Model (localStorage):**

| Field | Type | Description |
|-------|------|-------------|
| id | string | Unique identifier (e.g., wl_abc123_xyz789) |
| name | string | User-defined watchlist name |
| tickers | string[] | Array of ticker symbols (uppercase) |
| holdings | object[] | Array of {ticker, shares, dollarValue} |
| weightingMode | string | "equal", "shares", or "dollars" |
| createdAt | datetime | Creation timestamp (ISO 8601) |
| updatedAt | datetime | Last modification timestamp (ISO 8601) |

**Storage Structure:**

```json
{
  "version": 1,
  "lastUpdated": "2026-01-18T12:00:00Z",
  "watchlists": [
    {
      "id": "wl_abc123_xyz789",
      "name": "Tech Stocks",
      "tickers": ["AAPL", "MSFT", "GOOGL"],
      "holdings": [],
      "weightingMode": "equal",
      "createdAt": "2026-01-18T10:00:00Z",
      "updatedAt": "2026-01-18T12:00:00Z"
    }
  ]
}
```

**Export/Import Workflow:**

| Action | Description |
|--------|-------------|
| Export | Downloads `stockanalyzer-watchlists-YYYY-MM-DD.json` |
| Import | Opens file picker, merges with existing watchlists (new IDs for conflicts) |

---

### 3.15 Combined Watchlist View (FR-015)

**Purpose:** Allow users to view their watchlist as an aggregated portfolio with combined performance metrics, benchmark comparison, and significant move indicators.

| ID | Requirement |
|----|-------------|
| FR-015.1 | The system must provide a "Combined View" button for each watchlist with tickers |
| FR-015.2 | The system must display a single aggregated performance line representing the portfolio |
| FR-015.3 | The system must support three weighting modes: Equal Weight (default), Number of Shares, Dollar Value |
| FR-015.4 | The system must use historical close prices for each date when calculating portfolio value over time |
| FR-015.5 | The system must persist holdings (shares/dollar values) and weighting mode to storage |
| FR-015.6 | The chart title must display the watchlist name (not individual ticker symbols) |
| FR-015.7 | The system must display portfolio total return and day change in the Combined View header |
| FR-015.8 | The system must allow comparing the portfolio against benchmark indices (SPY, QQQ) |
| FR-015.9 | The system must display general market news instead of stock-specific news in Combined View |
| FR-015.10 | The system must provide a Holdings Editor modal to configure weighting mode and values |
| FR-015.11 | The Holdings Editor must allow adding new tickers via search |
| FR-015.12 | The Holdings Editor must allow removing tickers from the watchlist |
| FR-015.13 | The Holdings Editor must show current price for each ticker |
| FR-015.14 | The system must display ticker weights below the chart |
| FR-015.15 | The system must support period selection (1M, 3M, 6M, 1Y, 2Y) in Combined View |
| FR-015.16 | The system must display significant move markers (±5% days) on the combined portfolio chart |
| FR-015.17 | The system must provide a toggle to show/hide significant move markers |

**User Story:** *As an investor, I want to see my watchlist as an aggregated portfolio so that I can track the combined performance of my holdings without analyzing each stock individually.*

**Combined View Layout:**

```
┌────────────────────────────────────────────────────────────────────────┐
│  [←] Tech Stocks                          +12.5% (1Y)    [Edit Holdings]│
│  Combined View                                                          │
├────────────────────────────────────────────────────────────────────────┤
│  Period: [1M] [3M] [6M] [1Y*] [2Y]     Compare: [SPY] [QQQ] [Clear]    │
│                                         ☑ Show ±5% Moves               │
├────────────────────────────────────────────────────────────────────────┤
│                                                                         │
│  % Change                                                               │
│     ^      ___/\___     ▲ +5.2%                                        │
│  +15|     /        \    /\      ── Tech Stocks (blue)                  │
│  +10|    /          \  /  \     -- SPY (orange, dashed)                │
│   +5|   /            \/    \    ▲▼ Significant moves                   │
│    0|--/----------------------\--------------------------------         │
│   -5|                          \___/  ▼ -6.1%                          │
│     +-------------------------------------------------> Date           │
│                                                                         │
├────────────────────────────────────────────────────────────────────────┤
│  Weights: AAPL 40% | MSFT 35% | GOOGL 25%                              │
├────────────────────────────────────────────────────────────────────────┤
│  Market News                                                            │
│  • Fed signals rate decision coming...        Reuters • 2h ago         │
│  • Tech stocks rally on earnings...           CNBC • 4h ago            │
└────────────────────────────────────────────────────────────────────────┘
```

**Holdings Editor Modal:**

```
┌─────────────────────────────────────────┐
│  Edit Holdings - Tech Stocks        [X] │
├─────────────────────────────────────────┤
│  Weighting Mode:                        │
│  (•) Equal Weight                       │
│  ( ) Number of Shares                   │
│  ( ) Dollar Value                       │
├─────────────────────────────────────────┤
│  Add Ticker: [________________] 🔍      │
├─────────────────────────────────────────┤
│  Current Holdings:                      │
│  [X] AAPL   $185.50   [___10___] shares │
│  [X] MSFT   $415.20   [____5___] shares │
│  [X] GOOGL  $142.80   [___15___] shares │
├─────────────────────────────────────────┤
│              [Cancel]  [Save Holdings]  │
└─────────────────────────────────────────┘
```

**Aggregation Logic:**

| Mode | Calculation |
|------|-------------|
| Equal Weight | Average of normalized percentage returns for each ticker |
| Shares | Σ(shares × close price) for each historical date |
| Dollars | Convert initial dollars to shares at period start, then track value |

---

## 4. User Interface Specifications

### 4.1 Page Layout

```
┌──────────────────────────────────────────────────────────────────────────────────┐
│  📈 Stock Analyzer                    Powered by .NET 8 + Plotly.js    [🌙/☀️]   │
├─────────────────────────────────────────────────────────────────────┬────────────┤
│  ┌───────────────────────────────────────────────────────────────┐  │ WATCHLISTS │
│  │ [Search Box with Autocomplete] [Period ▼] [Chart ▼] [Analyze]│  │ ────────── │
│  │ ☑ SMA 20   ☑ SMA 50   ☐ SMA 200   ☐ RSI   ☐ MACD            │  │ [+ New]    │
│  └───────────────────────────────────────────────────────────────┘  │            │
│                                                                      │ ▼ Tech     │
│  ┌─────────────────────────────┐  ┌────────────────────────────┐   │   AAPL $150│
│  │ SYMBOL                      │  │ Key Metrics                │   │   MSFT $380│
│  │ Company Name                │  │ - Market Cap               │   │   GOOGL$140│
│  │ Exchange • Currency         │  │ - P/E Ratio                │   │            │
│  │         $XXX.XX [⭐ Add]    │  │ - 52W High/Low             │   │ ▶ Energy   │
│  │         +X.XX (+X%)         │  │ - Avg Volume               │   │            │
│  └─────────────────────────────┘  │ - Dividend Yield           │   │ ▶ Finance  │
│                                   └────────────────────────────┘   │            │
│  ┌───────────────────────────────────────────────────────────────┐  │            │
│  │                    INTERACTIVE PLOTLY CHART                    │  │            │
│  └───────────────────────────────────────────────────────────────┘  │            │
│                                                                      │            │
│  ┌─────────────────────────┐  ┌────────────────────────────────┐   │            │
│  │ Performance             │  │ Significant Moves (>3%)        │   │            │
│  │ - Total Return          │  │ Date | +X.XX%                  │   │            │
│  │ - Volatility            │  │ Date | -X.XX%                  │   │            │
│  └─────────────────────────┘  └────────────────────────────────┘   │            │
│                                                                      │            │
│  ┌───────────────────────────────────────────────────────────────┐  │            │
│  │ Recent News - [Headline - Source • Date]                       │  │            │
│  └───────────────────────────────────────────────────────────────┘  │            │
├─────────────────────────────────────────────────────────────────────┴────────────┤
│  Stock Analyzer © 2026 | Data from Yahoo Finance & Finnhub                       │
└──────────────────────────────────────────────────────────────────────────────────┘
```

### 4.2 Search Controls

| Control | Type | Options | Default |
|---------|------|---------|---------|
| Search Stock | Text input with autocomplete dropdown | Any ticker/company | Empty |
| Time Period | Dropdown | 1mo, 3mo, 6mo, 1y, 2y, 5y | 1y |
| Chart Type | Dropdown | Candlestick, Line | Candlestick |
| SMA-20 | Checkbox | On/Off | On |
| SMA-50 | Checkbox | On/Off | On |
| SMA-200 | Checkbox | On/Off | Off |
| RSI (14) | Checkbox | On/Off | Off |
| MACD | Checkbox | On/Off | Off |
| Compare to | Text input with autocomplete | Any ticker/company | Empty |
| Quick Compare | Buttons | SPY, QQQ, ^DJI | - |
| Clear Comparison | Button | Click to remove comparison | Hidden |
| Threshold | Slider | 3% - 10% | 5% |
| Show Markers | Checkbox | On/Off | On |
| Popup Thumbnails | Radio | Cats, Dogs | Cats |
| Analyze | Button | Click to load data | - |

### 4.3 Mobile Responsiveness (FR-016)

The dashboard is fully responsive and adapts to mobile devices.

| ID | Requirement |
|----|-------------|
| FR-016.1 | The system must display a hamburger menu button on screens smaller than 1024px |
| FR-016.2 | The system must hide the watchlist sidebar by default on mobile |
| FR-016.3 | Tapping the hamburger menu must slide the watchlist sidebar in from the right |
| FR-016.4 | A dark overlay must appear behind the sidebar when open on mobile |
| FR-016.5 | Tapping the overlay or pressing Escape must close the sidebar |
| FR-016.6 | The "Powered by" text must be hidden on small screens to save space |
| FR-016.7 | Search controls must stack vertically on mobile |
| FR-016.8 | Charts must scale to fit the viewport width |

**User Story:** *As a mobile user, I want to access my watchlists without the sidebar taking up screen space, so I can focus on the charts and data.*

**Mobile Breakpoints:**

| Breakpoint | Width | Behavior |
|------------|-------|----------|
| Small (sm) | 640px+ | Search controls side-by-side |
| Large (lg) | 1024px+ | Watchlist sidebar always visible |
| Below lg | <1024px | Sidebar hidden, hamburger menu shown |

### 4.4 Color Scheme

| Element | Color | Hex Code |
|---------|-------|----------|
| Primary (buttons, links) | Blue | #3B82F6 |
| Success (positive) | Green | #10B981 |
| Danger (negative) | Red | #EF4444 |
| SMA-20 | Orange | #FF9800 |
| SMA-50 | Purple | #9C27B0 |
| SMA-200 | Cyan | #00BCD4 |

---

## 5. User Workflows

### 5.1 Basic Stock Research

**Goal:** View price chart and company info for a specific stock

**Steps:**
1. User opens application (http://localhost:5000)
2. User types company name in search box (e.g., "Apple")
3. System displays autocomplete suggestions after 300ms
4. User clicks on "AAPL - Apple Inc. (NMS)"
5. Ticker populates in search box
6. User clicks "Analyze" button
7. System loads chart, company info, metrics, and news
8. User reviews displayed information

**Success Criteria:** Chart and all info panels display correctly

### 5.2 Technical Analysis

**Goal:** Analyze stock using moving averages

**Steps:**
1. User searches for and analyzes a stock
2. User enables SMA-200 checkbox
3. System updates chart with 200-day moving average line
4. User changes period to "2y" for longer-term view
5. User clicks "Analyze" to refresh
6. User hovers over chart to see values at specific dates

**Success Criteria:** All selected MAs display with correct values

### 5.3 News Research

**Goal:** Understand recent news affecting a stock

**Steps:**
1. User searches for a stock (e.g., "Tesla")
2. User clicks "Analyze"
3. User scrolls to "Significant Moves" section to see large price changes
4. User scrolls to "Recent News" section
5. User reads headlines and summaries
6. User clicks headline link to read full article

**Success Criteria:** News displays with working links

### 5.4 Watchlist Management

**Goal:** Create a watchlist and add stocks for quick monitoring

**Steps:**
1. User clicks "+ New List" in the watchlist sidebar
2. User enters watchlist name (e.g., "Tech Stocks")
3. User clicks "Create"
4. System creates watchlist and displays it in sidebar
5. User searches for and analyzes a stock (e.g., AAPL)
6. User clicks "Add to Watchlist" button
7. User selects "Tech Stocks" from dropdown
8. System adds ticker to watchlist and displays current price
9. User clicks ticker in watchlist
10. System loads that stock in the analyzer

**Success Criteria:** Watchlist persists after page refresh, ticker prices update

---

## 6. Business Rules

### 6.1 Data Validation Rules

| Rule ID | Rule Description |
|---------|------------------|
| BR-001 | Dividend yield values above 10% are assumed to be data errors and divided by 100 |
| BR-002 | Missing numeric values display as "N/A" rather than 0 or blank |
| BR-003 | Market cap formats as Millions (M), Billions (B), or Trillions (T) based on size |
| BR-004 | All prices display in USD with exactly 2 decimal places |

### 6.2 Significant Move Rules

| Rule ID | Rule Description |
|---------|------------------|
| BR-005 | Default threshold for "significant move" is ±3% (configurable via API) |
| BR-006 | Daily return is calculated using close-to-close prices |
| BR-007 | Top 10 significant moves are displayed in the UI |

### 6.3 Search Rules

| Rule ID | Rule Description |
|---------|------------------|
| BR-008 | Search requires minimum 2 characters |
| BR-009 | Search input is debounced by 300ms before querying |
| BR-010 | Maximum 10 search results are displayed |
| BR-011 | Client-side search is performed instantly using pre-loaded symbol data |
| BR-012 | Server fallback is triggered after 5 seconds of no typing when local results are empty |
| BR-013 | Server fallback is debounced - resets if user types within 5 seconds |
| BR-014 | Symbol data file is regenerated at startup and after daily Finnhub refresh |

---

## 7. Acceptance Criteria

### 7.1 Search Functionality

| Test | Expected Result | Pass/Fail |
|------|-----------------|-----------|
| Type "AAPL" | Shows Apple Inc. in dropdown instantly (no network call) | |
| Type "Apple" | Shows AAPL - Apple Inc. in dropdown | |
| Type "A" (1 char) | No results shown | |
| Select result from dropdown | Ticker populates in search box | |
| Click Analyze after selection | Chart loads for selected ticker | |
| Page load | Console shows "Loaded ~30K symbols in Xms" | |
| Type unknown ticker (e.g., "XYZABC") | Shows "No local results. Checking server..." | |
| Wait 5 seconds after unknown ticker | Server API call made, results updated | |
| Type during 5-second wait | Timer resets, no server call yet | |
| Search with no network | Client-side results still work | |

### 7.2 Chart Functionality

| Test | Expected Result | Pass/Fail |
|------|-----------------|-----------|
| Analyze AAPL with default settings | Candlestick chart with SMA-20, SMA-50 | |
| Switch to Line chart | Chart changes to line format | |
| Change period to 5y | Chart shows 5 years of data after Analyze | |
| Enable SMA-200 | Cyan MA line appears on chart | |
| Zoom with scroll | Chart zooms in/out | |
| Hover on candle | Shows OHLC values | |

### 7.3 Error Handling

| Test | Expected Result | Pass/Fail |
|------|-----------------|-----------|
| Analyze invalid ticker "XXXXX" | Error message displayed | |
| Analyze stock with no dividends | Dividend Yield shows "N/A" | |
| No news available | Shows "No recent news available" | |

### 7.4 Watchlist Functionality

| Test | Expected Result | Pass/Fail |
|------|-----------------|-----------|
| Click "+ New List" | Modal opens for watchlist name | |
| Create watchlist "Tech Stocks" | Watchlist appears in sidebar | |
| Click watchlist name | Watchlist expands to show tickers | |
| Load AAPL, click "Add to Watchlist" | Dropdown shows available watchlists | |
| Select watchlist from dropdown | AAPL added to watchlist, shows price | |
| Click ticker in watchlist | Stock analyzer loads that ticker | |
| Hover ticker, click × button | Ticker removed from watchlist | |
| Click edit (pencil) icon | Modal opens with current name | |
| Rename watchlist to "Technology" | Name updates in sidebar | |
| Click delete (trash) icon | Confirmation dialog appears | |
| Confirm delete | Watchlist removed from sidebar | |
| Refresh page | Watchlists persist from server | |
| Add duplicate ticker | Ticker not added (already exists) | |

---

## 8. Constraints and Limitations

### 8.1 Data Constraints

| Constraint | Description | Impact |
|------------|-------------|--------|
| Data delay | Stock prices have ~15 minute delay | Not suitable for real-time trading |
| Historical limit | Maximum 10 years of daily data | Long-term analysis limited |
| Coverage | US and major international stocks only | Some markets not available |

### 8.2 Technical Constraints

| Constraint | Description | Impact |
|------------|-------------|--------|
| News rate limit | 60 news requests per minute (Finnhub free tier) | Heavy use may cause news delays |
| Browser-based | Requires modern browser with JavaScript | Responsive web app, no native mobile app |
| Single user (current) | No authentication; multi-user architecture ready | Add auth layer for team use |

---

## 9. Version History

| Version | Date | Changes | Author |
|---------|------|---------|--------|
| 2.4 | 2026-01-22 | Client-side instant search (FR-001.8-14): Symbol data loaded to browser at startup (~315KB gzipped), sub-millisecond search, 5-second debounced server fallback for unknown symbols | Claude |
| 2.3 | 2026-01-21 | Added fast search performance requirement (FR-001.8): Sub-10ms search latency via local symbol database | Claude |
| 2.2 | 2026-01-19 | Added Mobile Responsiveness (FR-016): Hamburger menu for sidebar toggle, slide-in watchlist panel on mobile, responsive breakpoints, touch-friendly overlay dismiss | Claude |
| 2.1 | 2026-01-18 | Privacy-first localStorage watchlists (FR-014.10-18): Removed server storage, added export/import JSON, storage usage display. No PII collected. | Claude |
| 2.0 | 2026-01-17 | Added Combined Watchlist View (FR-015): Aggregated portfolio performance, three weighting modes, benchmark comparison, holdings editor with add/remove tickers, significant move markers with toggle, market news | Claude |
| 1.9 | 2026-01-17 | Added Watchlist Management (FR-014): Create/rename/delete watchlists, add/remove tickers, sidebar UI, JSON persistence, multi-user ready | Claude |
| 1.8 | 2026-01-17 | Added Bollinger Bands to Technical Indicators (FR-011): 20-period SMA with 2 std dev bands, overlaid on price chart with shaded fill | Claude |
| 1.7 | 2026-01-17 | Added Documentation Page (FR-013): Tabbed docs viewer, Mermaid.js architecture diagrams, Fuse.js search, scroll spy TOC highlighting, resizable sidebar | Claude |
| 1.6 | 2026-01-17 | Added Stock Comparison (FR-012): Compare to second stock/index with normalized % change | Claude |
| 1.5 | 2026-01-16 | Added Technical Indicators (FR-011): RSI and MACD with dynamic chart panels | Claude |
| 1.4 | 2026-01-16 | Added Dark Mode (FR-010) with system preference detection | Claude |
| 1.1 | 2026-01-16 | Added cats/dogs toggle (FR-005.11), image pre-caching (FR-005.12-15) | Claude |
| 1.0 | 2026-01-16 | Initial .NET functional specification | Claude |

---

## 10. References

- [ASP.NET Core Documentation](https://docs.microsoft.com/en-us/aspnet/core/)
- [Plotly.js Documentation](https://plotly.com/javascript/)
- [Tailwind CSS Documentation](https://tailwindcss.com/docs)
- [Finnhub API Documentation](https://finnhub.io/docs/api)
