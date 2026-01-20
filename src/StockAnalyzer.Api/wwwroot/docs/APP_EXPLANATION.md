# Stock Analyzer: Application Explanation

**Version:** 1.0
**Last Updated:** 2026-01-18
**Author:** Claude (AI Assistant) with Patrick Ford

---

## Executive Summary

Stock Analyzer is a web-based stock research tool that combines real-time market data, technical analysis, news correlation, and portfolio tracking into a single, privacy-respecting application. It runs on Azure infrastructure behind Cloudflare, serving users at https://psfordtaurus.com.

This document explains what the application does, how it was built, the architectural decisions we made, and why we made them. It's intended for anyone who wants to understand the system—whether you're evaluating it for your own use, considering contributing, or simply curious about how a modern .NET web application is structured.

---

## What Does Stock Analyzer Do?

At its core, Stock Analyzer answers a simple question: **"What happened to this stock, and why?"**

### The Problem It Solves

When a stock makes a significant move—say, up 7% in a single day—investors naturally want to know what caused it. Was it an earnings announcement? A product launch? Broader market conditions? Finding this information typically requires opening multiple tabs: a charting site, a news aggregator, company filings, and so on.

Stock Analyzer consolidates this research workflow into a single interface:

1. **Search for any stock** by ticker symbol or company name
2. **View interactive price charts** with technical indicators
3. **Identify significant price movements** (days with ≥3% change)
4. **See correlated news** that appeared around those movements
5. **Track portfolios** with multiple watchlists and weighting schemes
6. **Compare performance** against benchmarks like S&P 500 or NASDAQ

### Key Features

#### Interactive Charting
The charting system uses Plotly.js to render candlestick and line charts with smooth zoom, pan, and hover interactions. Users can overlay technical indicators including:

- **Moving Averages** (20-day, 50-day, 200-day SMA)
- **RSI** (Relative Strength Index) for momentum analysis
- **MACD** (Moving Average Convergence Divergence) for trend signals
- **Bollinger Bands** for volatility assessment

Each chart period is configurable: 1 month, 3 months, 6 months, 1 year, 2 years, 5 years, or 10 years of historical data.

#### Significant Move Detection
The application scans historical price data for days where the closing price changed by a configurable threshold (default 3%) from the opening price. These days are marked on the chart with triangle markers.

Hovering over a marker displays a Wikipedia-style popup card containing:
- The price change amount and percentage
- News headlines from around that date (±2 days)
- Article thumbnails and links to full articles
- A whimsical cat or dog image (user-toggleable) to add personality

This feature transforms passive chart viewing into active research—each significant move becomes an entry point for understanding what happened.

#### News Integration
News comes from Finnhub's financial news API, which aggregates content from major financial publications. The application fetches:

- **Company-specific news** for the stock being analyzed
- **Market news** by category (general market, forex, crypto, mergers)
- **Date-correlated news** matched to significant price movements

Each news item includes the headline, source, publication date, article link, and thumbnail image when available.

#### Watchlist Management
Users can create multiple named watchlists to track different stock groups—perhaps one for tech stocks, another for dividend plays, a third for speculative positions.

The watchlist system supports three weighting modes:

1. **Equal Weight** - All stocks contribute equally to portfolio performance
2. **Shares Mode** - Weight by number of shares owned
3. **Dollars Mode** - Weight by dollar value invested

The combined portfolio view aggregates performance across all holdings, displays it on a single chart, and identifies ±5% significant moves in the portfolio as a whole. Users can compare their portfolio against SPY (S&P 500) or QQQ (NASDAQ 100) benchmarks.

#### Company Information
For each stock, the application displays:

- Current price, daily change, volume
- 52-week high and low
- Market capitalization
- P/E ratio and dividend yield
- Sector and industry classification
- Security identifiers (ISIN, CUSIP, SEDOL)

Security identifiers are particularly useful for professional investors who need to cross-reference securities across different systems and jurisdictions.

---

## Technical Architecture

### Why .NET?

Stock Analyzer began as a Python/Streamlit prototype, then migrated to C#/.NET 8. The migration decision was driven by several factors:

1. **Type Safety** - C#'s static typing catches entire categories of bugs at compile time
2. **Performance** - .NET 8 offers excellent throughput for web APIs
3. **Ecosystem** - Mature libraries for finance, ML, and enterprise deployment
4. **Deployment Flexibility** - Easy containerization and cloud deployment
5. **Future-Proofing** - .NET's active development and long-term support

We use ASP.NET Core's Minimal APIs pattern rather than traditional MVC controllers. This keeps the codebase lean—all 25+ API endpoints are defined in a single `Program.cs` file with clear, readable route definitions.

### Project Structure

```
stock_analyzer_dotnet/
├── src/
│   ├── StockAnalyzer.Api/          # Web API project
│   │   ├── Program.cs              # Configuration + all endpoints
│   │   ├── wwwroot/                # Static frontend files
│   │   │   ├── index.html          # Main application UI
│   │   │   ├── docs.html           # Documentation viewer
│   │   │   └── js/                 # Client-side JavaScript
│   │   └── MLModels/               # ONNX machine learning models
│   │
│   └── StockAnalyzer.Core/         # Business logic library
│       ├── Models/                 # Data transfer objects
│       ├── Services/               # Business logic services
│       └── Data/                   # Database context + repositories
│
├── tests/
│   └── StockAnalyzer.Core.Tests/   # Unit test suite
│
└── infrastructure/
    └── azure/                      # Bicep IaC templates
```

This separation—API in one project, business logic in another—enables unit testing without spinning up a web server and keeps concerns cleanly separated.

### Data Flow

When a user searches for a stock, here's what happens:

1. **Frontend** sends an HTTP request to `/api/search?q=AAPL`
2. **API layer** receives the request and delegates to `StockDataService`
3. **StockDataService** queries Yahoo Finance's search API
4. **Results** are transformed into a consistent `SearchResult` model
5. **JSON response** is returned to the frontend
6. **Frontend JavaScript** renders the autocomplete dropdown

For historical data, the flow includes additional processing:

1. User selects a stock and time period
2. API fetches OHLCV data from Yahoo Finance
3. `AnalysisService` calculates technical indicators (moving averages, RSI, MACD)
4. `NewsService` fetches correlated news from Finnhub
5. Combined response is sent to frontend
6. Plotly.js renders the interactive chart

### Frontend Architecture

The frontend is deliberately simple: vanilla HTML, CSS (via Tailwind), and JavaScript. No React, no Vue, no build step. This choice prioritizes:

- **Fast load times** - No JavaScript framework overhead
- **Easy debugging** - What you write is what runs
- **Long-term maintainability** - No framework deprecation concerns
- **Accessibility** - Semantic HTML with progressive enhancement

JavaScript is organized into focused modules:
- `app.js` - Main application orchestration
- `api.js` - HTTP client for backend communication
- `charts.js` - Plotly.js chart configuration and rendering
- `watchlist.js` - Watchlist UI and state management

All styling uses Tailwind CSS loaded from a CDN. Dark mode is supported via a class toggle on the root element, with user preference persisted to localStorage.

### Database Strategy

The application supports two storage modes:

**JSON File Storage** (development/single-user):
- Watchlists stored as JSON in `data/watchlists.json`
- No database setup required
- Thread-safe file access with locking
- Simple to inspect and debug

**SQL Server Storage** (production/multi-user):
- Entity Framework Core with SQL Server provider
- Automatic migrations on startup
- Proper relational integrity with foreign keys
- Ready for multi-user authentication when needed

The repository pattern (`IWatchlistRepository`) abstracts storage, letting us swap implementations without changing business logic. The `WatchlistService` class doesn't know or care whether it's talking to a JSON file or SQL Server.

---

## Machine Learning Component

One of the more unusual features is the ML-powered image processing system. Here's the story behind it.

### The Problem
When displaying news hover cards, we wanted thumbnail images. News articles sometimes have relevant images, but often they're generic stock photos, paywalled, or missing entirely. We needed a fallback.

### The Solution
Rather than showing boring placeholder images, we fetch random cat or dog images from public APIs (cataas.com and dog.ceo). But raw images vary wildly in size, aspect ratio, and composition. We wanted consistent, visually appealing thumbnails.

### How It Works

1. **Model**: We use YOLOv8n (You Only Look Once, version 8, nano variant), a state-of-the-art object detection model exported to ONNX format
2. **Detection**: The model identifies cats or dogs in images and returns bounding boxes
3. **Cropping**: We crop to the detected animal's face/body, centering the subject
4. **Caching**: Processed images are stored in an in-memory queue

The ONNX runtime enables running neural networks without heavy ML frameworks. The model is just 12MB and processes images in milliseconds.

### Pre-caching Strategy
To avoid latency when users hover over chart markers, we pre-cache 50 cat images and 50 dog images at application startup. A background service monitors cache levels and refills when they drop below 10 items.

This is a delightful example of using modern ML for something simple—making a stock analysis tool feel more personal and less sterile.

---

## Infrastructure & Deployment

### Production Environment

Stock Analyzer runs on Azure with this architecture:

```
                                   ┌─────────────────┐
                                   │   Cloudflare    │
                                   │   (DNS + SSL)   │
User ──────────────────────────────┤                 │
      https://psfordtaurus.com     │   Free Tier     │
                                   └────────┬────────┘
                                            │
                                            ▼
                                   ┌─────────────────┐
                                   │ Azure Container │
                                   │   Instance      │
                                   │                 │
                                   │  Docker Image   │
                                   │  (.NET 8 API)   │
                                   └────────┬────────┘
                                            │
                                            ▼
                                   ┌─────────────────┐
                                   │   Azure SQL     │
                                   │   Database      │
                                   │                 │
                                   │  Basic (5 DTU)  │
                                   └─────────────────┘
```

**Why Azure Container Instance (ACI)?**
We originally planned to use Azure App Service, but encountered quota limitations on the free tier. ACI provided a quick alternative—a simple container host without the complexity of Kubernetes.

**Why Cloudflare?**
Cloudflare provides free SSL certificates and DDoS protection. Rather than managing Let's Encrypt renewals ourselves, we point our domain's nameservers to Cloudflare and let them handle TLS termination.

### Cost Optimization

This infrastructure runs on approximately $20/month:
- **ACI**: ~$15/month for 1 vCPU, 1.5GB RAM
- **Azure SQL**: ~$5/month for Basic tier (5 DTUs)
- **Cloudflare**: Free tier (SSL, DNS, basic WAF)
- **Container Registry**: GitHub Container Registry (free for public/private repos)

For a personal project or small team, this is economical while remaining production-grade.

### CI/CD Pipeline

We use GitHub Actions for continuous integration and deployment:

**On every push to `develop`:**
1. Restore NuGet packages
2. Build in Release mode
3. Run unit tests
4. Run CodeQL security analysis
5. Run OWASP Dependency Check
6. Build Docker image (but don't deploy)

**On manual trigger (production deploy):**
1. Require confirmation (type "deploy")
2. Log deployment reason for audit trail
3. Run full build and test suite
4. Build and push Docker image with version tag
5. Deploy to Azure Container Instance
6. Run health check to verify deployment
7. Report success/failure in GitHub Summary

The manual trigger for production is intentional. We don't want code automatically going live—a human should make the explicit decision to deploy.

### Rollback Procedure

Every deployment tags the Docker image with `prod-{run_number}`. If a deployment causes problems:

1. Go to GitHub Actions
2. Trigger a new deployment using the previous image tag
3. Or manually run `az container create` with the previous image

This versioning strategy means we can roll back to any previous deployment in under two minutes.

---

## Security Implementation

Security wasn't an afterthought—it was designed in from the start. Here's our defense-in-depth approach:

### Pre-commit Hooks

Before code even leaves a developer's machine:

| Hook | Purpose |
|------|---------|
| detect-secrets | Blocks commits containing API keys, passwords, tokens |
| detect-private-key | Prevents committing private key files |
| Bandit | Python static analysis for security issues |
| YAML/JSON validation | Catches syntax errors before they hit CI |
| Large file blocking | Prevents accidental commit of data files |

These run automatically on every `git commit`. Developers can't bypass them without explicit `--no-verify` (which would be visible in commit metadata).

### GitHub Actions Security

Every push triggers security scanning:

**CodeQL Analysis**
- Runs weekly and on every PR
- Scans C# and Python code
- Uses extended security ruleset (100+ rules)
- Detects SQL injection, XSS, command injection, etc.
- Results visible in GitHub Security tab

**OWASP Dependency Check**
- Scans NuGet packages for known CVEs
- Fails the build on CVSS score ≥7 (high/critical)
- Generates HTML reports for manual review
- Catches vulnerable transitive dependencies

**Dependabot**
- Monitors dependencies for security advisories
- Automatically creates PRs for updates
- Runs weekly on Monday mornings
- Covers NuGet, pip, and GitHub Actions

### Build-time Analysis

The .NET build includes three code analyzers:

1. **Microsoft.CodeAnalysis.NetAnalyzers** - Official .NET security and reliability rules
2. **Roslynator** - Extended code quality analysis
3. **SecurityCodeScan** - OWASP Top 10 detection for C#

These analyzers flag issues at compile time, before code is even committed.

### Application Security Headers

Every HTTP response includes security headers:

```
X-Frame-Options: DENY
X-Content-Type-Options: nosniff
X-XSS-Protection: 1; mode=block
Referrer-Policy: strict-origin-when-cross-origin
Content-Security-Policy: [strict policy]
Permissions-Policy: [restricted]
```

The Content Security Policy (CSP) restricts script sources to our own origin plus specific CDNs (Tailwind, Plotly, Mermaid). This prevents XSS attacks even if an attacker finds an injection point.

### Subresource Integrity

External scripts include SRI hashes:

```html
<script src="https://cdn.plot.ly/plotly-2.27.0.min.js"
        integrity="sha384-..."
        crossorigin="anonymous"></script>
```

If Plotly's CDN were compromised, the browser would refuse to execute the tampered script.

### Transport Security

- **TLS 1.2 minimum** on Azure
- **HTTPS enforced** via Cloudflare
- **Secure cookies** (when authentication is added)

### Secrets Management

Production secrets are stored in GitHub Secrets, never in code:
- `AZURE_CREDENTIALS` - Azure service principal
- `AZURE_SQL_CONNECTION` - Database connection string
- `FINNHUB_API_KEY` - News API authentication
- `ACR_PASSWORD` - Container registry access

The `.secrets.baseline` file tracks known false positives in secret detection, preventing alert fatigue while maintaining vigilance.

---

## Design Philosophy

Several principles guided our decisions:

### Privacy First
We never track users. No analytics. No cookies beyond functional necessities. No data sharing with advertising networks. This is explicit policy, documented in CLAUDE.md.

### Simplicity Over Complexity
We chose vanilla JavaScript over React because we didn't need React's complexity. We chose Minimal APIs over MVC because we didn't need MVC's structure. Every abstraction must earn its place.

### Defense in Depth
Security isn't one thing—it's layers. Pre-commit hooks catch issues early. Code analyzers catch them at build time. CI scanning catches them before merge. Runtime protections catch anything that slips through.

### Observable by Default
Health check endpoints expose application state. Structured logging captures request flows. When something goes wrong, we can diagnose it.

### Rollback-Ready
Every deployment is versioned. Every change is reversible. We can return to any previous state in minutes.

---

## External Dependencies

The application relies on several external services:

| Service | Purpose | Failure Mode |
|---------|---------|--------------|
| Yahoo Finance | Stock prices, fundamentals | App non-functional |
| Finnhub | News articles | News features unavailable |
| dog.ceo | Dog images | Generic placeholder shown |
| cataas.com | Cat images | Generic placeholder shown |
| Azure SQL | Watchlist persistence | Watchlists read-only from cache |
| Cloudflare | SSL/DNS | Direct IP access still works |

Yahoo Finance is the critical dependency—without stock data, the app serves no purpose. Other services degrade gracefully.

---

## Future Considerations

The application is built with extensibility in mind:

**Multi-user Authentication**
The data model includes a `UserId` field on watchlists. Adding authentication (Azure AD B2C, for example) would enable personalized experiences without schema changes.

**Real-time Data**
The current implementation uses request-response patterns. Adding WebSocket support could enable live price streaming and push notifications for significant moves.

**Additional Indicators**
The technical analysis service is modular. Adding Stochastic Oscillator, Average True Range, or other indicators is straightforward.

**Expanded Coverage**
The Yahoo Finance data source covers stocks and ETFs. Adding options chains, futures, or crypto would require additional data providers.

---

## Conclusion

Stock Analyzer represents a modern approach to financial research tooling: focused functionality, clean architecture, rigorous security, and respectful treatment of user data.

It demonstrates that you don't need venture capital or a team of engineers to build production-quality software. With careful choices about architecture, dependencies, and deployment, a solo developer (assisted by AI) can create something genuinely useful.

The code is intentionally straightforward. The infrastructure is deliberately simple. The security is appropriately thorough. This is software built to work reliably and be understood easily—not to impress with complexity.

If you're exploring the codebase, start with `Program.cs` to see how endpoints are defined, then follow the flow into the services. The patterns repeat: receive request, validate input, delegate to service, transform response, return JSON. No magic, no surprises.

Welcome to Stock Analyzer.

---

## Version History

| Date | Version | Changes |
|------|---------|---------|
| 2026-01-18 | 1.0 | Initial document |
