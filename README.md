# Stock Analyzer

Source for [psfordtaurus.com](https://psfordtaurus.com) — an interactive web app for equity research and portfolio analysis, built on .NET 8 and Azure.

Pull up a ticker to get candlestick or line charts across ten time ranges, overlay technical indicators, compare normalized performance against benchmarks, and read deduplicated news from multiple sources. Watchlists and portfolios stay in your browser — nothing about you is stored server-side.

It's a personal project, not an open-source one, but the code is here to read. The parts worth a look: a multi-provider price pipeline that composites each field from the highest-priority source that returns it, a deliberately framework-free frontend, and on-device ML for news sentiment and image tagging.

## Features

- **Charts** — candlestick and line modes across 1D, 5D, 1M, 3M, 6M, YTD, 1Y, 5Y, 10Y, and MAX ranges.
- **Technical indicators** — SMA-20/50/200, RSI, MACD, Bollinger Bands, and Stochastic Oscillator.
- **Comparison and benchmarks** — normalized percent-change view across multiple symbols, with benchmark overlays (SPY, QQQ, DIA, IWM, EWU, EWG, EWJ, EFA, EEM, ACWI).
- **Portfolio view** — combine holdings with equal, share-count, or dollar-value weighting.
- **News** — aggregated from Finnhub and Marketaux, deduplicated by Jaccard similarity, with relevance scoring.
- **Significant moves** — flags ±3–10% daily moves (threshold configurable) with hover cards explaining context.
- **Dashboard** — draggable, resizable tiles (GridStack.js), dark mode, and four built-in themes.
- **Watchlists** — stored in `localStorage` only. No account, no server-side profile.

## Tech stack

| Layer | Choice |
|-------|--------|
| Backend | .NET 8.0 (LTS), ASP.NET Core minimal APIs, C# 12 |
| Data access | Entity Framework Core 8 on SQL Server |
| Frontend | Plain HTML/CSS/JS — no SPA framework |
| Styling | Tailwind CSS 3.4, compiled locally |
| Charting | Plotly.js, GridStack.js |
| ML | ONNX Runtime (YOLOv8n image detection), VADER + FinBERT sentiment |
| Tests | xUnit, Moq, FluentAssertions (C#); Jest (JS) |
| Hosting | Azure App Service (Linux), Azure SQL, Key Vault, Application Insights |

The frontend deliberately avoids a SPA framework. The UI is chart- and data-heavy rather than navigation-heavy, so vanilla JS plus Plotly keeps the bundle small and the dependency surface low.

## Project structure

```
stock-analyzer/
├── src/
│   ├── StockAnalyzer.Api/      # ASP.NET Core host + wwwroot frontend
│   │   ├── wwwroot/            # index.html, js/, css/, data/, docs.html
│   │   ├── MLModels/           # YOLOv8n ONNX model
│   │   └── package.json        # Tailwind build
│   └── StockAnalyzer.Core/     # Services, models, EF Core data layer
├── tests/
│   └── StockAnalyzer.Core.Tests/
├── eodhd-loader/               # Standalone WPF tool for bulk price backfills
├── infrastructure/azure/       # Bicep IaC
├── docs/                       # Project, technical, and functional specs
├── endpoints.json              # Single source of truth for service endpoints + secret keys
└── StockAnalyzer.sln
```

## Testing

Around 140 automated tests cover the backend and frontend: xUnit for the C# services (with Moq and FluentAssertions), and Jest for the JavaScript. The C# tests mock the external HTTP clients and run against an in-memory database, so the suite needs neither network access nor a live SQL Server.

## Data sources

Price data uses a multi-provider strategy with per-field compositing — each metric comes from the highest-priority provider that returns a non-null value:

1. **TwelveData** — primary (8 calls/min, 800/day on the free tier)
2. **Financial Modeling Prep** — secondary (250 calls/day)
3. **Yahoo Finance** — fallback (via the OoplesFinance NuGet package)
4. **EODHD** — bulk historical backfills, loaded by the `eodhd-loader` tool and cached in the database

News comes from Finnhub and Marketaux; company descriptions fall back to Wikipedia (cached); identifier mapping uses OpenFIGI. All external calls are rate-limited and cached to stay within free-tier quotas.

## Deployment

Production runs on Azure, provisioned from Bicep in [`infrastructure/azure/`](infrastructure/azure/):

- **App Service** (Linux, P0v3) running a Docker container
- **Azure SQL Database** with pre-loaded historical prices
- **Key Vault** for secrets, **Application Insights** for telemetry
- **Cloudflare** in front for CDN and the custom domain

Deployment is a manual GitHub Actions dispatch (`azure-deploy.yml`) that requires explicit confirmation — there's no auto-deploy on merge. CI (`dotnet-ci.yml`) runs the build, both test suites, and security scans on every push.

See [`docs/RUNBOOK.md`](docs/RUNBOOK.md) for deploy and rollback procedures, and [`docs/DEPLOYMENT_AZURE.md`](docs/DEPLOYMENT_AZURE.md) for the Azure setup.

## Privacy

No personally identifiable information is collected. Watchlists and portfolios live entirely in browser `localStorage`. See [`docs/PRIVACY_POLICY.md`](docs/PRIVACY_POLICY.md).

## Documentation

The [`docs/`](docs/) directory holds the detailed specs:

- [`PROJECT_OVERVIEW.md`](docs/PROJECT_OVERVIEW.md) — architecture and milestones
- [`TECHNICAL_SPEC.md`](docs/TECHNICAL_SPEC.md) — APIs, services, data models
- [`FUNCTIONAL_SPEC.md`](docs/FUNCTIONAL_SPEC.md) — user-facing requirements
- [`SECURITY_OVERVIEW.md`](docs/SECURITY_OVERVIEW.md) — headers, validation, SAST tooling
