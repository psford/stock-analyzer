# Stock Analyzer Project Overview

**Version:** 2.6
**Date:** January 19, 2026
**Status:** Production (https://psfordtaurus.com)
**Repository:** Private (available on request)

---

## Executive Summary

Stock Analyzer is a full-stack web application for equity research and portfolio analysis, built over a 6-day intensive development sprint. The project demonstrates modern software engineering practices including CI/CD pipelines, cloud deployment, comprehensive testing, and ML-powered features—all developed with AI-assisted pair programming using Claude Code.

The application provides interactive stock charts, technical indicators, news correlation, and portfolio management through a responsive web interface. It runs on Azure App Service with Azure SQL Database, featuring zero-downtime deployments and secrets management through Azure Key Vault.

---

## Project Statistics

### Codebase Metrics

| Category | Count |
|----------|-------|
| **Total Lines of Code** | ~15,000 |
| C# Backend Code | 8,597 lines |
| JavaScript Frontend Code | 4,781 lines |
| HTML Templates | 1,725 lines |
| Documentation | 4,702 lines |

### Source Files

| Type | Count |
|------|-------|
| C# Source Files | 41 |
| Service Classes | 11 |
| Model Classes | 8 |
| Test Files | 8 |
| JavaScript Modules | 5 |
| HTML Pages | 3 |
| Documentation Files | 8 |

### Test Coverage

| Category | Tests |
|----------|-------|
| Unit Tests (xUnit) | 113+ |
| Frontend Tests (Jest) | 25 |
| Integration Tests | 3 |
| **Total Automated Tests** | **140+** |

### Git History

| Metric | Value |
|--------|-------|
| Total Commits | 205 |
| Project Duration | 6 days |
| First Commit | January 13, 2026 |
| Average Commits/Day | ~34 |

### API Endpoints

| Category | Count |
|----------|-------|
| REST API Endpoints | 23 |
| Async Service Methods | 33 |
| Health Check Endpoints | 3 |

### CI/CD Pipeline

| Workflow | Purpose |
|----------|---------|
| dotnet-ci.yml | Build, test, security scan on push/PR |
| azure-deploy.yml | Production deployment to App Service |
| codeql.yml | Static analysis security scanning |
| docs-deploy.yml | GitHub Pages documentation sync |
| frontend-tests.yml | Jest test runner |
| dependabot.yml | Automated dependency updates |

---

## Architecture Highlights

### Backend Stack

- **Framework:** ASP.NET Core 8.0 Minimal APIs
- **Data Access:** Entity Framework Core 8.0 with Azure SQL
- **External APIs:**
  - Yahoo Finance (via OoplesFinance.YahooFinanceAPI)
  - Finnhub (company news, profiles)
  - Marketaux (alternative news source)
- **ML Integration:** Microsoft.ML.OnnxRuntime with YOLOv8n model
- **Image Processing:** SixLabors.ImageSharp

### Frontend Stack

- **UI Framework:** Tailwind CSS (CDN)
- **Charting:** Plotly.js with interactive candlestick/line charts
- **Documentation:** Marked.js (Markdown), Mermaid.js (diagrams), Fuse.js (search)
- **Testing:** Jest with jsdom

### Infrastructure

- **Hosting:** Azure App Service B1 (Linux)
- **Database:** Azure SQL Database (Basic tier)
- **Secrets:** Azure Key Vault
- **CDN/SSL:** Cloudflare (free tier)
- **CI/CD:** GitHub Actions
- **Container Registry:** Azure Container Registry

---

## Key Features Implemented

### Stock Analysis

1. **Interactive Charts**
   - Candlestick and line chart modes
   - Time periods: 1D, 5D, 1M, 3M, 6M, YTD, 1Y, 5Y, 10Y, MAX
   - Technical indicators: SMA-20/50/200, RSI, MACD, Bollinger Bands
   - Stock comparison with normalized percentage view

2. **Significant Move Detection**
   - Configurable threshold (3-10%)
   - Triangle markers on chart for notable price movements
   - Wikipedia-style hover cards with related news

3. **News Integration**
   - Multi-source aggregation (Finnhub + Marketaux)
   - ML-based headline relevance scoring
   - Jaccard similarity for deduplication
   - Sentiment analysis integration

### Portfolio Management

1. **Watchlists**
   - Multiple named watchlists
   - LocalStorage persistence (privacy-first)
   - Export/import JSON functionality

2. **Combined Portfolio View**
   - Holdings tracking (shares/dollars)
   - Weighting modes: equal, shares, dollars
   - Portfolio performance aggregation
   - Benchmark comparison (SPY, QQQ)

### ML-Powered Features

1. **Image Processing**
   - YOLOv8n ONNX model for animal detection
   - Intelligent cropping centered on detected faces
   - Quality control: 50% confidence threshold, 20% minimum detection size
   - Background cache with 100 images, auto-refill at 30

2. **Headline Relevance Scoring**
   - Weighted factor scoring:
     - Ticker mention: 35%
     - Company name: 25%
     - Recency: 20%
     - Sentiment data: 10%
     - Source quality: 10%

---

## Security Implementation

### Headers & Policies

- Content Security Policy (CSP)
- HTTP Strict Transport Security (HSTS)
- X-Frame-Options, X-Content-Type-Options
- Subresource Integrity (SRI) for CDN resources

### Input Validation

- Ticker symbol regex validation
- Request size limits
- CORS restricted to known origins

### Scanning Tools

- SecurityCodeScan (.NET SAST)
- Roslynator (code analysis)
- OWASP Dependency Check
- CodeQL (GitHub Advanced Security)
- Dependabot (automated updates)

### Secrets Management

- Azure Key Vault for production secrets
- No secrets in source control
- Environment-specific configuration

---

## Development Practices

### Version Control

- **Branching Strategy:** PR-to-production workflow
- **develop:** Working branch for iteration (direct commits)
- **master:** Production-only (PR required, CI must pass)

### Code Quality

- Strong typing throughout (C# records, TypeScript-like JSDoc)
- Comprehensive error handling
- Async/await patterns for all I/O
- Immutable data models where possible

### Testing Strategy

- Unit tests for all services
- Mocked HTTP clients for external APIs
- FluentAssertions for readable test assertions
- Moq for dependency injection testing

### Documentation

- Technical specification (2,000+ lines)
- Functional specification
- Deployment guides (Azure, Oracle Cloud)
- Security runbook
- Architecture diagrams (Mermaid.js)

---

## Timeline & Milestones

### Day 1-2 (Jan 14-15)
- Initial project scaffolding
- Core API endpoints (stock info, history, search)
- Basic chart rendering with Plotly.js
- Pre-commit hooks and initial security setup

### Day 3 (Jan 16)
- Dark mode implementation
- ML image processing pipeline
- Image caching system
- Technical indicators (RSI, MACD)
- Company profile integration (ISIN/CUSIP/SEDOL)

### Day 4 (Jan 17)
- Watchlist feature with full CRUD
- Combined portfolio view
- Documentation page with search
- Azure deployment (ACI + SQL)
- CI/CD pipelines

### Day 5 (Jan 18)
- Custom domain + SSL (psfordtaurus.com)
- GitHub Pages for docs
- Privacy-first localStorage watchlists
- Frontend Jest tests
- Mobile responsiveness

### Day 6 (Jan 19)
- App Service migration (zero-downtime)
- Azure Key Vault integration
- Multi-source news aggregation
- ML headline relevance scoring
- Image quality control improvements
- Comprehensive unit test suite (52 new tests)

---

## Lessons Learned

### What Worked Well

1. **AI-Assisted Development:** Claude Code enabled rapid iteration and maintained consistency across the codebase. The pair programming approach caught issues early and suggested best practices.

2. **Minimal API Pattern:** ASP.NET Core Minimal APIs provided a clean, lightweight structure that was easy to extend and test.

3. **Progressive Enhancement:** Starting with core functionality and layering on features (ML, multi-source news) kept the architecture flexible.

4. **Documentation-First:** Keeping specs updated alongside code prevented drift and made onboarding straightforward.

### Challenges Overcome

1. **Azure App Service Cold Starts:** Background image caching helped but required tuning to avoid thread pool exhaustion.

2. **News API Limitations:** Free tier rate limits necessitated multi-source aggregation and intelligent caching.

3. **ML Model Integration:** ONNX Runtime configuration required careful attention to input/output tensor shapes.

4. **Mobile Responsiveness:** Retrofitting a desktop-first UI required significant CSS refactoring.

---

## Future Roadmap

### Planned

- Bollinger Bands indicator
- Stochastic Oscillator
- Earnings calendar integration
- Export to Excel
- Real-time streaming (WebSocket)

### Under Consideration

- User authentication (Azure AD B2C)
- Options chain data
- Backtesting capability
- VNet + Private Endpoints

---

## Acknowledgments

This project was built using AI-assisted development with Claude Code (Anthropic). The development process demonstrated effective human-AI collaboration for rapid prototyping and production deployment.

### Technologies Used

- .NET 8.0, C# 12
- ASP.NET Core Minimal APIs
- Entity Framework Core 8.0
- Tailwind CSS, Plotly.js
- Azure App Service, SQL Database, Key Vault
- GitHub Actions, CodeQL
- YOLOv8n, ONNX Runtime
- xUnit, Jest, Moq, FluentAssertions

---

*Document generated: January 19, 2026*
*Stock Analyzer v2.6*
