# Technical Specification: Stock Analyzer Dashboard (.NET)

**Version:** 2.53
**Last Updated:** 2026-02-26
**Author:** Claude (AI Assistant)
**Status:** Production (Azure)

---

## 1. Overview

### 1.1 Purpose

The Stock Analyzer Dashboard is a web-based application that provides interactive stock market analysis, visualization, and news integration. It enables users to research equity securities through charts, financial metrics, and news correlation for significant price movements.

This document covers the **C#/.NET 8 implementation** with a custom HTML/CSS/JavaScript frontend using Tailwind CSS and Plotly.js.

### 1.2 Scope

This specification covers:
- System architecture and component interactions
- Data sources and API integrations
- Deployment and runtime requirements
- Configuration and environment setup
- Troubleshooting procedures

### 1.3 Glossary

| Term | Definition |
|------|------------|
| OHLCV | Open, High, Low, Close, Volume - standard price data format |
| SMA | Simple Moving Average - trend indicator calculated over N periods |
| EMA | Exponential Moving Average - weighted moving average with more recent emphasis |
| RSI | Relative Strength Index - momentum oscillator measuring overbought/oversold conditions (0-100) |
| MACD | Moving Average Convergence Divergence - trend-following momentum indicator |
| Ticker | Unique stock symbol (e.g., AAPL for Apple Inc.) |
| Significant Move | Daily price change of ±3% or greater (configurable) |
| Minimal APIs | ASP.NET Core lightweight API approach without controllers |
| Finnhub | Third-party financial news API service |
| Dog CEO API | Third-party random dog image API |
| cataas | Cat as a Service - random cat image API |
| ONNX | Open Neural Network Exchange - portable ML model format |
| YOLOv8 | You Only Look Once v8 - object detection model |
| COCO | Common Objects in Context - ML dataset with 80 object classes |
| ISIN | International Securities Identification Number (12-char global identifier) |
| CUSIP | Committee on Uniform Securities Identification Procedures (9-char US/Canada) |
| SEDOL | Stock Exchange Daily Official List (7-char UK/Ireland identifier) |
| OpenFIGI | Bloomberg's free identifier mapping API |

---

## 2. System Architecture

### 2.1 High-Level Architecture

```
┌─────────────────────────────────────────────────────────────────────┐
│                          User Browser                                │
│                        (localhost:5000)                              │
└─────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼
┌─────────────────────────────────────────────────────────────────────┐
│                     ASP.NET Core Web API                             │
│                   (StockAnalyzer.Api)                                │
│  ┌────────────────┐  ┌────────────────┐  ┌────────────────────────┐ │
│  │ wwwroot/       │  │ Minimal APIs   │  │ Static Files           │ │
│  │ - index.html   │  │ - /api/stock/* │  │ - Tailwind CSS CDN     │ │
│  │ - js/*.js      │  │ - /api/search  │  │ - Plotly.js CDN        │ │
│  │ - data/symbols │  │ - /api/trending│  │ - symbols.txt (30K)    │ │
│  └────────────────┘  └────────────────┘  └────────────────────────┘ │
└─────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼
┌─────────────────────────────────────────────────────────────────────┐
│                    StockAnalyzer.Core Library                        │
│  ┌────────────────────────────────────────────────────────────────┐ │
│  │ Models:                    Services:                           │ │
│  │ - StockInfo               - AggregatedStockDataService (Multi) │ │
│  │ - OhlcvData               - TwelveDataService (Primary)        │ │
│  │ - HistoricalDataResult    - FmpService (Secondary)             │ │
│  │ - NewsItem/NewsResult     - YahooFinanceService (Fallback)     │ │
│  │ - SignificantMove         - NewsService (Finnhub)              │ │
│  │ - SearchResult            - MarketauxService (Marketaux)       │ │
│  │                           - AggregatedNewsService (Multi-src)  │ │
│  │                           - HeadlineRelevanceService (ML)      │ │
│  │                           - AnalysisService (MAs, Volatility)  │ │
│  │                           - ImageProcessingService (ML/ONNX)   │ │
│  │                           - ImageCacheService (Background)     │ │
│  │                           - SymbolRefreshService (Background)  │ │
│  │                           - SqlSymbolRepository (Local Search) │ │
│  └────────────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────────┘
                                    │
                    ┌───────────────┴───────────────┐
                    ▼                               ▼
┌────────────────────────────┐    ┌────────────────────────────────────┐
│   OoplesFinance.Yahoo      │    │         Finnhub REST API           │
│   FinanceAPI (NuGet)       │    │                                    │
│                             │    │                                    │
│  - GetSummaryDetailsAsync  │    │  - Company news                    │
│  - GetAssetProfileAsync    │    │  - Company profile (ISIN, CUSIP)   │
│  - GetHistoricalDataAsync  │    │  - News images                     │
│  - GetTopTrendingStocks    │    │  - Article URLs                    │
│                             │    │                                    │
└────────────────────────────┘    └────────────────────────────────────┘
         │                                        │
         ▼                                        ▼
┌────────────────────────────┐    ┌────────────────────────────────────┐
│  Yahoo Finance Search API  │    │       OpenFIGI REST API            │
│  (Direct HTTP Client)      │    │       (Bloomberg)                   │
│                             │    │                                    │
│  - Ticker search by name   │    │  - SEDOL lookup from ISIN          │
│  - Company name lookup     │    │  - Identifier mapping              │
└────────────────────────────┘    └────────────────────────────────────┘
         │
         ▼
┌────────────────────────────┐
│  Yahoo Finance Search API  │
│  (Direct HTTP Client)      │
│                             │
│  - Ticker search by name   │
│  - Company name lookup     │
└────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────┐
│                     Image Processing (Server-Side)                   │
│  ┌────────────────────────────────────────────────────────────────┐ │
│  │  ImageProcessingService             ImageCacheService           │ │
│  │  ┌──────────────────────┐          ┌──────────────────────┐   │ │
│  │  │ YOLOv8n ONNX Model   │          │ BackgroundService    │   │ │
│  │  │ - Detect cat/dog     │    ───>  │ - Maintain 50+ cache │   │ │
│  │  │ - Crop to center     │          │ - Auto-refill < 10   │   │ │
│  │  │ - Resize 320×150     │          │ - Thread-safe queue  │   │ │
│  │  └──────────────────────┘          └──────────────────────┘   │ │
│  └────────────────────────────────────────────────────────────────┘ │
│                              │                                       │
│              ┌───────────────┴───────────────┐                      │
│              ▼                               ▼                      │
│  ┌────────────────────────────┐    ┌────────────────────────────┐  │
│  │   Dog CEO API              │    │   cataas.com               │  │
│  │   (dog.ceo)                │    │   (Cat as a Service)       │  │
│  │  - /api/breeds/image/     │    │  - /cat?width=640&height=  │  │
│  │    random                  │    │    640&{cacheBuster}       │  │
│  └────────────────────────────┘    └────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────────┘
```

### 2.2 Component Description

| Component | Location | Responsibility |
|-----------|----------|----------------|
| Web API | `StockAnalyzer.Api/Program.cs` | REST endpoints, static file serving |
| Core Library | `StockAnalyzer.Core/` | Business logic, data models, services |
| Frontend | `StockAnalyzer.Api/wwwroot/` | HTML/CSS/JS user interface |
| Configuration | `appsettings.json` | API keys, environment settings |

### 2.3 Technology Stack

| Layer | Technology | Version/Notes |
|-------|------------|---------------|
| Runtime | .NET | 8.0 LTS |
| Web Framework | ASP.NET Core Minimal APIs | Built-in |
| Stock Data (Primary) | Twelve Data REST API | 8 calls/min, 800/day free |
| Stock Data (Secondary) | Financial Modeling Prep API | 250 calls/day free |
| Stock Data (Fallback) | OoplesFinance.YahooFinanceAPI | NuGet 1.7.1 |
| News Data | Finnhub REST API | Custom HttpClient |
| News Data (Alt) | Marketaux REST API | 100 calls/day free |
| ML Runtime | Microsoft.ML.OnnxRuntime | NuGet 1.17.0 |
| Image Processing | SixLabors.ImageSharp | NuGet 3.1.7 |
| Object Detection | YOLOv8n ONNX | ~12MB model |
| Charting | Plotly.js | 2.27.0 (CDN) |
| Tile Layout | GridStack.js | 12.4.2 (local) |
| CSS Framework | Tailwind CSS | Built locally |
| Serialization | System.Text.Json | Built-in |

### 2.4 Stock Data Provider Architecture

The application uses a multi-provider architecture with cascading fallback for stock data:

```
AggregatedStockDataService (orchestrator)
    ├── TwelveDataService    (Priority 1 - 8/min, 800/day)
    ├── FmpService           (Priority 2 - 250/day, ~87 symbols)
    └── YahooFinanceService  (Priority 3 - fallback, full coverage)
```

**Strategy:** Providers are tried in priority order. First successful response wins.

**Caching:** In-memory cache with TTLs:
- Quotes: 5 minutes
- Historical data: 1 hour
- Search results: 24 hours

**Cache invalidation:** Per-symbol `CancellationTokenSource` tokens ensure ALL cache entries (period-based and custom date range) are evicted when `InvalidateCache(symbol)` is called. Prevents stale API fallback results from blocking subsequent DB queries after background backfill completes.

**Rate Limiting:** Each provider tracks its own rate limits. When limits are approached, requests automatically fall through to the next provider.

**Configuration:**
```json
{
  "StockDataProviders": {
    "TwelveData": { "ApiKey": "" },
    "FMP": { "ApiKey": "" }
  }
}
```

Environment variables: `TWELVEDATA_API_KEY`, `FMP_API_KEY`

---

## 3. API Endpoints

### 3.1 Endpoint Reference

| Endpoint | Method | Description | Parameters |
|----------|--------|-------------|------------|
| `/api/stock/{ticker}` | GET | Stock information | `ticker`: Stock symbol |
| `/api/stock/{ticker}/history` | GET | Historical OHLCV data | `ticker`, `period` (optional, default: 1y; values: 1d/5d/mtd/1mo/3mo/6mo/ytd/1y/2y/5y/10y/15y/20y/30y/max), `from`/`to` (optional, ISO dates for custom range) |
| `/api/stock/{ticker}/news` | GET | Company news with sentiment + relevance scoring | `ticker`, `days` (optional, default: 30), `limit` (optional, default: 30) |
| `/api/stock/{ticker}/significant` | GET | Significant price moves (no news) | `ticker`, `threshold` (optional, default: 3.0), `period` (optional), `from`/`to` (optional, ISO dates - takes precedence over period) |
| `/api/stock/{ticker}/news/move` | GET | News for specific move with metadata | `ticker`, `date`, `change`, `limit` (optional, default: 5) |
| `/api/stock/{ticker}/analysis` | GET | Performance metrics + MAs | `ticker`, `period` (optional) |
| `/api/stock/{ticker}/chart-data` | GET | Combined history + analysis (single request) | `ticker`, `period` (optional, default: 1y; same values as /history), `from`/`to` (optional, ISO dates) |
| `/api/search` | GET | Ticker search | `q`: Search query (min 2 chars) |
| `/api/trending` | GET | Trending stocks | `count` (optional, default: 10) |
| `/api/images/cat` | GET | ML-processed cat image | None |
| `/api/images/dog` | GET | ML-processed dog image | None |
| `/api/images/status` | GET | Image cache status | None |
| `/api/watchlists` | GET | List all watchlists | None |
| `/api/watchlists` | POST | Create watchlist | `name` (body) |
| `/api/watchlists/{id}` | GET | Get watchlist by ID | `id`: Watchlist ID |
| `/api/watchlists/{id}` | PUT | Rename watchlist | `id`, `name` (body) |
| `/api/watchlists/{id}` | DELETE | Delete watchlist | `id`: Watchlist ID |
| `/api/watchlists/{id}/tickers` | POST | Add ticker to watchlist | `id`, `ticker` (body) |
| `/api/watchlists/{id}/tickers/{ticker}` | DELETE | Remove ticker | `id`, `ticker` |
| `/api/watchlists/{id}/quotes` | GET | Get quotes for watchlist | `id`: Watchlist ID |
| `/api/watchlists/{id}/holdings` | PUT | Update holdings | `id`, `weightingMode`, `holdings` (body) |
| `/api/watchlists/{id}/combined` | GET | Get combined portfolio | `id`, `period`, `benchmark` (optional) |
| `/api/news/market` | GET | Get general market news | `category` (optional, default: general) |
| `/api/health` | GET | Health check | None |

### 3.2 Response Examples

**GET /api/stock/AAPL**
```json
{
  "symbol": "AAPL",
  "shortName": "Apple Inc",
  "longName": "Apple Inc",
  "sector": "Technology",
  "industry": "Technology",
  "website": "https://www.apple.com/",
  "country": "US",
  "currency": "USD",
  "exchange": "NASDAQ NMS - GLOBAL MARKET",
  "micCode": "XNAS",
  "exchangeName": "NASDAQ",
  "isin": null,
  "cusip": null,
  "sedol": null,
  "description": "Apple Inc. designs, manufactures, and markets smartphones, personal computers, tablets, wearables, and accessories worldwide...",
  "fullTimeEmployees": 164000,
  "currentPrice": 198.50,
  "previousClose": 197.25,
  "dayHigh": 199.00,
  "dayLow": 196.50,
  "marketCap": 3050000000000,
  "peRatio": 31.25,
  "dividendYield": 0.0044,
  "fiftyTwoWeekHigh": 199.62,
  "fiftyTwoWeekLow": 164.08
}
```

**Note:** ISIN/CUSIP/SEDOL availability depends on data source (Finnhub free tier may not include all identifiers).

**GET /api/search?q=apple**
```json
{
  "query": "apple",
  "results": [
    {
      "symbol": "AAPL",
      "shortName": "Apple Inc.",
      "longName": "Apple Inc.",
      "exchange": "NMS",
      "type": "EQUITY",
      "displayName": "AAPL - Apple Inc. (NMS)"
    }
  ]
}
```

**GET /api/stock/AAPL/history?period=1mo**
```json
{
  "symbol": "AAPL",
  "period": "1mo",
  "startDate": "2025-12-16",
  "endDate": "2026-01-16",
  "data": [
    {
      "date": "2025-12-16",
      "open": 195.50,
      "high": 197.00,
      "low": 195.00,
      "close": 196.75,
      "volume": 45000000
    }
  ]
}
```

---

## 4. Data Models

### 4.1 StockInfo

```csharp
public record StockInfo
{
    public required string Symbol { get; init; }
    public string? ShortName { get; init; }
    public string? LongName { get; init; }
    public string? Sector { get; init; }
    public string? Industry { get; init; }
    public string? Website { get; init; }
    public string? Country { get; init; }
    public string? Currency { get; init; }
    public string? Exchange { get; init; }       // Set by external API services
    public string? MicCode { get; init; }        // ISO 10383 Market Identifier Code from SecurityMaster
    public string? ExchangeName { get; init; }   // Joined from MicExchange reference table

    // Security identifiers
    public string? Isin { get; init; }
    public string? Cusip { get; init; }
    public string? Sedol { get; init; }

    // Company profile
    public string? Description { get; init; }
    public int? FullTimeEmployees { get; init; }

    public decimal? CurrentPrice { get; init; }
    public decimal? PreviousClose { get; init; }
    public decimal? Open { get; init; }
    public decimal? DayHigh { get; init; }
    public decimal? DayLow { get; init; }
    public long? Volume { get; init; }
    public long? AverageVolume { get; init; }

    public decimal? MarketCap { get; init; }
    public decimal? PeRatio { get; init; }
    public decimal? DividendYield { get; init; }
    public decimal? FiftyTwoWeekHigh { get; init; }
    public decimal? FiftyTwoWeekLow { get; init; }
}
```

### 4.2 SearchResult

```csharp
public record SearchResult
{
    public required string Symbol { get; init; }
    public required string ShortName { get; init; }
    public string? LongName { get; init; }
    public string? Exchange { get; init; }       // Set by external API services
    public string? MicCode { get; init; }        // ISO 10383 Market Identifier Code from SecurityMaster
    public string? ExchangeName { get; init; }   // Joined from MicExchange reference table
    public string? Type { get; init; }
    public string DisplayName => $"{Symbol} - {ShortName}" +
        (ExchangeName != null ? $" ({ExchangeName})" :
         Exchange != null ? $" ({Exchange})" : "");
}
```

### 4.3 OhlcvData

```csharp
public record OhlcvData
{
    public DateTime Date { get; init; }
    public decimal Open { get; init; }
    public decimal High { get; init; }
    public decimal Low { get; init; }
    public decimal Close { get; init; }
    public long Volume { get; init; }
    public decimal? AdjustedClose { get; init; }
}
```

### 4.4 SignificantMove

```csharp
public record SignificantMove
{
    public DateTime Date { get; init; }
    public decimal PercentChange { get; init; }
    public decimal ClosePrice { get; init; }
    public long Volume { get; init; }
    public NewsItem? RelatedNews { get; init; }
}
```

### 4.5 RsiData

```csharp
public record RsiData
{
    public required DateTime Date { get; init; }
    public decimal? Rsi { get; init; }  // 0-100, null when insufficient data
}
```

### 4.6 MacdData

```csharp
public record MacdData
{
    public required DateTime Date { get; init; }
    public decimal? MacdLine { get; init; }     // Fast EMA - Slow EMA
    public decimal? SignalLine { get; init; }   // 9-period EMA of MACD line
    public decimal? Histogram { get; init; }    // MACD line - Signal line
}
```

### 4.7 BollingerData

```csharp
public record BollingerData
{
    public required DateTime Date { get; init; }
    public decimal? UpperBand { get; init; }    // Middle + (StdDev * multiplier)
    public decimal? MiddleBand { get; init; }   // 20-period SMA
    public decimal? LowerBand { get; init; }    // Middle - (StdDev * multiplier)
}
```

### 4.8 StochasticData

```csharp
public record StochasticData
{
    public required DateTime Date { get; init; }
    public decimal? K { get; init; }    // %K line (0-100), null when insufficient data
    public decimal? D { get; init; }    // %D signal line (SMA of %K), null when insufficient data
}
```

### 4.10 CompanyProfile

```csharp
public record CompanyProfile
{
    public required string Symbol { get; init; }
    public string? Name { get; init; }
    public string? Country { get; init; }
    public string? Currency { get; init; }
    public string? Exchange { get; init; }       // Set by Finnhub/NewsService
    public string? MicCode { get; init; }        // ISO 10383 Market Identifier Code from SecurityMaster
    public string? ExchangeName { get; init; }   // Joined from MicExchange reference table
    public string? Industry { get; init; }
    public string? WebUrl { get; init; }
    public string? Logo { get; init; }
    public string? IpoDate { get; init; }
    public decimal? MarketCapitalization { get; init; }
    public decimal? ShareOutstanding { get; init; }

    // Security identifiers
    public string? Isin { get; init; }
    public string? Cusip { get; init; }
    public string? Sedol { get; init; }
}
```

### 4.11 TickerHolding

```csharp
public record TickerHolding
{
    public required string Ticker { get; init; }
    public decimal? Shares { get; init; }      // Number of shares (null if using dollar mode)
    public decimal? DollarValue { get; init; } // Dollar amount (null if using shares mode)
}
```

### 4.12 CombinedPortfolioResult

```csharp
public record CombinedPortfolioResult
{
    public required string WatchlistId { get; init; }
    public required string WatchlistName { get; init; }
    public required string Period { get; init; }
    public required string WeightingMode { get; init; }
    public required List<PortfolioDataPoint> Data { get; init; }
    public required decimal TotalReturn { get; init; }
    public required decimal DayChange { get; init; }
    public required decimal DayChangePercent { get; init; }
    public required Dictionary<string, decimal> TickerWeights { get; init; }
    public List<PortfolioDataPoint>? BenchmarkData { get; init; }
    public string? BenchmarkSymbol { get; init; }
    public List<PortfolioSignificantMove>? SignificantMoves { get; init; }
}

public record PortfolioDataPoint
{
    public required DateTime Date { get; init; }
    public required decimal PortfolioValue { get; init; }
    public required decimal PercentChange { get; init; }
}

public record PortfolioSignificantMove
{
    public required DateTime Date { get; init; }
    public required decimal PercentChange { get; init; }
    public bool IsPositive => PercentChange > 0;
}
```

---

## 5. Services

### 5.1 StockDataService

**File:** `StockAnalyzer.Core/Services/StockDataService.cs`

| Method | Description |
|--------|-------------|
| `GetStockInfoAsync(symbol)` | Fetch stock info + asset profile via OoplesFinance |
| `GetHistoricalDataAsync(symbol, period)` | Fetch OHLCV history |
| `SearchAsync(query)` | Search tickers via Yahoo Finance API |
| `GetTrendingStocksAsync(count)` | Get trending stocks |

**Stock Info Implementation:**
Fetches both summary details and asset profile from Yahoo Finance:
- `GetSummaryDetailsAsync(symbol)` - Price data, ratios, metrics
- `GetAssetProfileAsync(symbol)` - Company description, sector, industry, employees

**Search Implementation:**
Uses direct HTTP call to Yahoo Finance Search API since OoplesFinance doesn't provide search:
```
https://query2.finance.yahoo.com/v1/finance/search?q={query}&quotesCount=8&newsCount=0
```

### 5.2 NewsService

**File:** `StockAnalyzer.Core/Services/NewsService.cs`

| Method | Description |
|--------|-------------|
| `GetCompanyNewsAsync(symbol, fromDate)` | Fetch news from Finnhub |
| `GetNewsForDateAsync(symbol, date)` | Fetch news for specific date (±1 day range) |
| `GetNewsForDateWithSentimentAsync(symbol, date, priceChange, maxArticles)` | Fetch sentiment-filtered news matching price direction |
| `GetCompanyProfileAsync(symbol)` | Fetch company profile with identifiers from Finnhub |
| `GetSedolFromIsinAsync(isin)` | Look up SEDOL via OpenFIGI API |
| `GetMarketNewsAsync(category)` | Fetch general market news from Finnhub |

**Finnhub News Endpoint:**
```
GET https://finnhub.io/api/v1/company-news?symbol={symbol}&from={date}&to={date}&token={api_key}
```

**Finnhub Profile Endpoint:**
```
GET https://finnhub.io/api/v1/stock/profile2?symbol={symbol}&token={api_key}
```
Returns: name, country, currency, exchange, industry, weburl, logo, isin, cusip

**OpenFIGI Endpoint:**
```
POST https://api.openfigi.com/v3/mapping
Body: [{"idType": "ID_ISIN", "idValue": "{isin}"}]
```
Used to look up SEDOL for UK/Irish securities from ISIN.

**Finnhub Market News Endpoint:**
```
GET https://finnhub.io/api/v1/news?category={category}&token={api_key}
```
Returns general market news. Categories: `general`, `forex`, `crypto`, `merger`.

### 5.2.1 SentimentAnalyzer (3-Tier Ensemble)

**File:** `StockAnalyzer.Core/Services/SentimentAnalyzer.cs`

Static utility class for ensemble sentiment analysis of news headlines. Uses a 3-tier approach combining keyword matching, VADER, and optionally FinBERT for high accuracy.

| Method | Description |
|--------|-------------|
| `Analyze(headline)` | Returns (Sentiment, decimal score) for a headline |
| `MatchesPriceDirection(headline, priceChange)` | Returns true if headline sentiment matches price direction |
| `CalculateMatchScore(headline, priceChange)` | Returns 0-100 relevance score for sentiment-price alignment |

**Sentiment Classification:**

```csharp
public enum Sentiment { Positive, Negative, Neutral }
```

**3-Tier Sentiment Architecture:**

| Tier | Service | Weight | Description |
|------|---------|--------|-------------|
| 1 | Keyword matching | 60% | Financial domain-specific vocabulary with word-boundary matching |
| 2 | VADER | 40% | General sentiment with modifier/negation handling |
| 3 | FinBERT | (Optional) | ML-based financial text analysis via ONNX |

**Keyword Lists (~70+ each with word-boundary matching):**

| Category | Examples |
|----------|----------|
| **Positive** | soars, surges, rallies, jumps, beats, upgrade, bullish, strong, record, boost, gains |
| **Negative** | plunges, crashes, tumbles, downgrade, bearish, warning, miss, drops, falls, weak, **dips**, **slump**, **weaken** |

**Word Boundary Matching (v2.16+):**
- Uses regex `\b{keyword}\b` to prevent substring matches
- "regains" no longer matches "gains" keyword
- "uprising" no longer matches "rising" keyword
- Multi-word phrases use simple contains matching

**Scoring Algorithm (Ensemble):**

```
keyword_score = (positive_count - negative_count) / max(total, 1)
vader_score = VADER.Analyze(headline).Compound  // -1 to +1

combined_score = (keyword_score * 0.6) + (vader_score * 0.4)

Classification:
  - combined_score > 0.05 → Positive
  - combined_score < -0.05 → Negative
  - else → Neutral

match_score (0-100):
  - Neutral headline → 50 (base)
  - Matching direction → 50 + (|combined_score| * 50)
  - Mismatching direction → 50 - (|combined_score| * 50)
```

### 5.2.2 VaderSentimentService

**File:** `StockAnalyzer.Core/Services/VaderSentimentService.cs`

VADER (Valence Aware Dictionary and sEntiment Reasoner) wrapper for general sentiment analysis.

| Method | Description |
|--------|-------------|
| `Analyze(text)` | Returns VaderResult with Positive, Negative, Neutral, Compound scores |
| `GetSentimentLabel(text)` | Returns "positive", "negative", or "neutral" string |

**VADER Features:**
- Handles modifiers ("not good" → negative)
- Handles intensifiers ("very good" → more positive)
- Handles punctuation ("great!!!" → stronger)
- Handles emoji and emoticons
- Compound score: -1 (most negative) to +1 (most positive)

**NuGet Package:** `VaderSharp2` v3.3.2.1

### 5.2.3 FinBertSentimentService (Optional ML Tier)

**File:** `StockAnalyzer.Core/Services/FinBertSentimentService.cs`

FinBERT sentiment analysis using ONNX Runtime for high-accuracy financial text classification.

| Method | Description |
|--------|-------------|
| `Analyze(text)` | Returns FinBertResult with label and probability distribution |

**FinBertResult Record:**
```csharp
public record FinBertResult(
    string Label,        // "positive", "negative", "neutral"
    float Confidence,    // 0-1 confidence for predicted label
    float PositiveProb,  // Probability of positive
    float NegativeProb,  // Probability of negative
    float NeutralProb    // Probability of neutral
);
```

**Model Files (Optional):**
- `wwwroot/MLModels/finbert-onnx/model.onnx` (~418 MB)
- Uses built-in BERT vocabulary via BERTTokenizers NuGet

**Dependencies:**
- `Microsoft.ML.OnnxRuntime` v1.23.2
- `BERTTokenizers` v1.2.0

### 5.2.4 SentimentCacheService

**File:** `StockAnalyzer.Core/Services/SentimentCacheService.cs`

Background service that pre-computes and caches FinBERT sentiment analysis results.

| Method | Description |
|--------|-------------|
| `QueueForAnalysisAsync(headline)` | Queue headline for background analysis |
| `GetCachedResultAsync(headline)` | Get cached result (or null if pending) |
| `AnalyzeNow(headline)` | Synchronous analysis (blocking) |
| `GetStatisticsAsync()` | Get cache statistics |

**Database Table:** `CachedSentiments`
- HeadlineHash (SHA256, unique index)
- Headline, Sentiment, Confidence
- PositiveProb, NegativeProb, NeutralProb
- AnalyzerVersion, CreatedAt, IsPending

**Usage in NewsService:**

```csharp
public async Task<MoveNewsResult> GetNewsForDateWithSentimentAsync(
    string symbol, DateTime date, decimal priceChangePercent, int maxArticles = 5)
{
    // 1. Fetch company news (date-2 to date+3)
    // 2. Score each headline with SentimentAnalyzer
    // 3. Filter to articles with matchScore > 25
    // 4. Fallback: recent dates → market news; old dates → best company news
    // Returns MoveNewsResult with articles, source type, and directionMatch flag
}
```

**Fallback Cascade:**

| Priority | Condition | Source |
|----------|-----------|--------|
| 1 | Sentiment-matched company news exists | Company news (filtered, `source: "company"`) |
| 2 | No match, date ≤3 days old | General market news (`source: "market"`) |
| 3 | No match, date >3 days old | Best company news by match score (`source: "company"`) |

**Note:** We skip directly to market news when no sentiment-matched company news exists. Showing mismatched or unrelated company news is worse than showing general market context that explains broader conditions.

### 5.3 MarketauxService

**File:** `StockAnalyzer.Core/Services/MarketauxService.cs`

Alternative news source to complement Finnhub, providing redundancy and additional coverage.

| Method | Description |
|--------|-------------|
| `GetNewsAsync(symbol, publishedAfter, limit)` | Fetch stock-specific news from Marketaux |
| `GetMarketNewsAsync(limit)` | Fetch general market news |

**Marketaux News Endpoint:**
```
GET https://api.marketaux.com/v1/news/all?symbols={symbol}&filter_entities=true&published_after={date}&language=en&api_token={token}
```

**Rate Limits (Free Tier):**
- 100 requests/day
- Max 50 articles per request

**Response includes:**
- Article title, description, URL, image
- Entity-level sentiment scores (-1 to +1)
- Entity matching with symbol relevance

### 5.4 HeadlineRelevanceService

**File:** `StockAnalyzer.Core/Services/HeadlineRelevanceService.cs`

Scores news headlines for relevance to a given stock symbol using multiple factors.

| Method | Description |
|--------|-------------|
| `ScoreRelevance(article, symbol, companyName)` | Calculate 0-1 relevance score |
| `AggregateNews(articles, symbol, companyName, maxResults)` | Score, deduplicate, and rank articles |

**Scoring Weights:**
| Factor | Weight | Description |
|--------|--------|-------------|
| Ticker Mention | 35% | Ticker in headline: 1.0, summary: 0.7, RelatedSymbols only: 0.3 |
| Company Name | 25% | Company name appears in text |
| Recency | 20% | Exponential decay (24hr half-life) |
| Sentiment Data | 10% | Having sentiment indicates better coverage |
| Source Quality | 10% | Premium sources (Reuters, Bloomberg, CNBC, etc.) |

**Note:** RelatedSymbols-only score was reduced from 1.0 to 0.3 in v2.37 because Finnhub tags articles broadly (e.g., general market articles tagged with specific tickers). This prevents ~60% of noise articles from outranking genuinely relevant headlines.

**Deduplication:**
Uses Jaccard similarity (>70% threshold) on normalized headlines to remove duplicate stories across sources.

### 5.5 AggregatedNewsService

**File:** `StockAnalyzer.Core/Services/AggregatedNewsService.cs`

Combines news from multiple sources (Finnhub, Marketaux) and applies relevance scoring.

| Method | Description |
|--------|-------------|
| `GetAggregatedNewsAsync(symbol, days, maxResults)` | Fetch and aggregate stock news from all sources |
| `GetAggregatedMarketNewsAsync(maxResults)` | Fetch and aggregate market news from all sources |

**Response Model (`AggregatedNewsResult`):**
- `Symbol` - Ticker symbol
- `CompanyName` - Resolved company name
- `Articles` - Scored and deduplicated news items
- `TotalFetched` - Total articles before deduplication
- `SourceBreakdown` - Count per source API
- `AverageRelevanceScore` - Mean relevance score

### 5.6 AnalysisService

**File:** `StockAnalyzer.Core/Services/AnalysisService.cs`

| Method | Description |
|--------|-------------|
| `CalculateMovingAverages(data)` | Calculate SMA-20, SMA-50, SMA-200 |
| `CalculatePerformance(data)` | Calculate return, volatility, high/low |
| `DetectSignificantMovesAsync(...)` | Find moves exceeding threshold |
| `CalculateRsi(data, period)` | Calculate RSI using Wilder's smoothing |
| `CalculateMacd(data, fast, slow, signal)` | Calculate MACD line, signal line, histogram |
| `CalculateBollingerBands(data, period, stdDev)` | Calculate Bollinger Bands (upper, middle, lower) |
| `CalculateStochastic(data, kPeriod, dPeriod)` | Calculate Stochastic Oscillator (%K and %D lines) |
| `CalculateEma(values, period)` | Private helper for EMA calculation |

**RSI Calculation (Wilder's Smoothing Method):**
```
1. Calculate price changes (gains and losses)
2. Initial average: SMA of first N gains/losses
3. Subsequent: avgGain = (prevAvgGain × (period-1) + currentGain) / period
4. RS = avgGain / avgLoss
5. RSI = 100 - (100 / (1 + RS))
```

**MACD Calculation:**
```
1. Fast EMA = 12-period EMA of close prices
2. Slow EMA = 26-period EMA of close prices
3. MACD Line = Fast EMA - Slow EMA
4. Signal Line = 9-period EMA of MACD Line
5. Histogram = MACD Line - Signal Line
```

**Bollinger Bands Calculation:**
```
1. Middle Band = 20-period SMA of close prices
2. Standard Deviation = √(Σ(close - SMA)² / period)
3. Upper Band = Middle Band + (2 × Standard Deviation)
4. Lower Band = Middle Band - (2 × Standard Deviation)
```

**Stochastic Oscillator Calculation:**
```
1. For each day (starting at kPeriod):
   - Highest High = max(High) over kPeriod days
   - Lowest Low = min(Low) over kPeriod days
   - %K = 100 × (Close - Lowest Low) / (Highest High - Lowest Low)
2. %D = SMA of %K over dPeriod days
3. Minimum data required: kPeriod + dPeriod - 1 = 16 points for first valid %D
```

**EMA Formula:**
```
Multiplier = 2 / (period + 1)
EMA = (Current Price - Previous EMA) × Multiplier + Previous EMA
```

### 5.7 ImageProcessingService

**File:** `StockAnalyzer.Core/Services/ImageProcessingService.cs`

| Method | Description |
|--------|-------------|
| `GetProcessedCatImageAsync()` | Fetch, detect, crop, and return cat image |
| `GetProcessedDogImageAsync()` | Fetch, detect, crop, and return dog image |
| `ProcessImage(imageData, classId)` | Run YOLO detection and crop (returns null if no valid detection) |
| `DetectAnimal(image, classId)` | Find animal bounding box via ONNX inference |
| `CropToTarget(image, detection)` | Crop 320×320 (square) centered on detection |

**ML Model:**
- **Model:** YOLOv8n (nano) exported to ONNX
- **Input:** 640×640 RGB image, normalized to 0-1
- **Output:** (1, 84, 8400) tensor - 4 bbox coords + 80 COCO class probabilities
- **Classes:** Cat=15, Dog=16 (COCO class indices)
- **Confidence Threshold:** 0.50 (high threshold ensures clear animal faces)
- **Minimum Detection Size:** 20% of image area (ensures animal is prominent)

**Quality Control:**
Images are rejected (returns null) if:
- No detection found above confidence threshold
- Detection bounding box is less than 20% of image area
- This ensures only images with clearly visible animal faces are used

### 5.8 ImageCacheService

**File:** `StockAnalyzer.Core/Services/ImageCacheService.cs`

Implements `BackgroundService` for continuous cache maintenance with database persistence.

| Method | Description |
|--------|-------------|
| `GetCatImageAsync()` | Get random processed cat image from database |
| `GetDogImageAsync()` | Get random processed dog image from database |
| `GetCacheStatusAsync()` | Return (cats, dogs, maxSize) tuple |
| `ExecuteAsync(token)` | Background loop monitoring cache levels |
| `GetCatImage()` | Sync wrapper for backward compatibility |
| `GetDogImage()` | Sync wrapper for backward compatibility |

**Cache Configuration:**
- **Cache Size:** 1000 images per type (configurable via `ImageProcessing:CacheSize`)
- **Refill Threshold:** 100 images (triggers background refill)
- **Storage:** SQL Server via `ICachedImageRepository` (persists across restarts)
- **Refill Delay:** 500ms between cache checks
- **Startup Delay:** 10 seconds to allow app to become responsive

### 5.9 ICachedImageRepository

**Interface:** `StockAnalyzer.Core/Services/ICachedImageRepository.cs`
**Implementation:** `StockAnalyzer.Core/Data/SqlCachedImageRepository.cs`

Repository pattern for persistent image cache storage.

| Method | Description |
|--------|-------------|
| `GetRandomImageAsync(imageType)` | Get random image using SQL `ORDER BY NEWID()` |
| `AddImageAsync(imageType, imageData)` | Store new processed image |
| `GetCountAsync(imageType)` | Get count of cached images by type |
| `GetAllCountsAsync()` | Get counts for all image types |
| `TrimOldestAsync(imageType, maxCount)` | Delete oldest images when over limit |
| `ClearAllAsync()` | Delete all images (for testing/reset) |

**Database Schema:**
```sql
CREATE TABLE CachedImages (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    ImageType NVARCHAR(10) NOT NULL,      -- "cat" or "dog"
    ImageData VARBINARY(MAX) NOT NULL,    -- JPEG bytes (~20-30KB each)
    CreatedAt DATETIME2 DEFAULT GETUTCDATE()
);
CREATE INDEX IX_CachedImages_ImageType ON CachedImages(ImageType);
```

**Storage Estimate:** 2000 images × 25KB avg = ~50MB (acceptable for Azure SQL)

### 5.6 WatchlistService

**File:** `StockAnalyzer.Core/Services/WatchlistService.cs`

| Method | Description |
|--------|-------------|
| `GetAllWatchlistsAsync()` | List all user watchlists |
| `GetWatchlistAsync(id)` | Get single watchlist by ID |
| `CreateWatchlistAsync(request)` | Create new watchlist |
| `UpdateWatchlistAsync(id, request)` | Rename watchlist |
| `DeleteWatchlistAsync(id)` | Delete watchlist |
| `AddTickerAsync(id, request)` | Add ticker to watchlist |
| `RemoveTickerAsync(id, ticker)` | Remove ticker from watchlist |
| `GetQuotesAsync(id)` | Get current quotes for all tickers |
| `UpdateHoldingsAsync(id, request)` | Update holdings and weighting mode |
| `GetCombinedPortfolioAsync(id, period, benchmark)` | Calculate combined portfolio performance |

**Combined Portfolio Calculation:**

The `GetCombinedPortfolioAsync` method aggregates multiple stock positions into a single portfolio performance line.

**Weighting Modes:**
| Mode | Calculation |
|------|-------------|
| `equal` | Each ticker gets 1/N weight, returns normalized percentage change |
| `shares` | Portfolio value = Σ(shares × close price per date) |
| `dollars` | Convert initial dollars to shares at period start, then track value |

**Portfolio Value Formula (Shares Mode):**
```
PortfolioValue[date] = Σ(holdings[i].shares × closePrice[i][date])
```

**Significant Moves Detection:**
The `CalculateSignificantMoves` method identifies days with ≥5% portfolio change:
```csharp
private static List<PortfolioSignificantMove> CalculateSignificantMoves(
    List<PortfolioDataPoint> data, decimal threshold)
{
    var moves = new List<PortfolioSignificantMove>();
    for (int i = 1; i < data.Count; i++)
    {
        var dailyChange = ((data[i].PortfolioValue - data[i-1].PortfolioValue)
                          / data[i-1].PortfolioValue) * 100;
        if (Math.Abs(dailyChange) >= threshold)
            moves.Add(new PortfolioSignificantMove { Date = data[i].Date,
                                                      PercentChange = dailyChange });
    }
    return moves;
}
```

---

## 6. Frontend Architecture

### 6.1 File Structure

```
wwwroot/
├── index.html          # Main page with Tailwind CSS layout
├── docs.html           # Documentation viewer with Architecture visualization tab
├── docs/               # Documentation files
│   ├── CLAUDE.md       # Project guidelines (synced during build)
│   ├── FUNCTIONAL_SPEC.md
│   ├── TECHNICAL_SPEC.md
│   └── diagrams/       # Mermaid diagram files (.mmd)
│       ├── project-structure.mmd     # AUTO - solution dependencies
│       ├── service-architecture.mmd  # MANUAL - backend services
│       ├── data-flow.mmd             # MANUAL - sequence diagram
│       ├── domain-models.mmd         # MANUAL - class diagram
│       ├── image-pipeline.mmd        # MANUAL - ML processing flow
│       ├── frontend-architecture.mmd # MANUAL - JS modules
│       └── api-endpoints.mmd         # MANUAL - REST endpoints
├── js/
│   ├── api.js          # API client wrapper + portfolio aggregation
│   ├── app.js          # Main application logic
│   ├── charts.js       # Plotly chart configuration
│   ├── dragMeasure.js  # Click-and-drag performance measurement overlay
│   ├── storage.js      # LocalStorage watchlist persistence
│   └── watchlist.js    # Watchlist sidebar and combined view
├── tests/              # Frontend JavaScript unit tests
│   └── portfolio.test.js  # Portfolio aggregation tests
└── package.json        # Jest test configuration
```

### 6.1.1 Documentation Page (docs.html)

The documentation page provides six tabs:
- **App Explanation** - Overview of the application
- **Project Guidelines** - CLAUDE.md with project rules and best practices
- **Functional Spec** - User-facing feature documentation
- **Technical Spec** - Developer documentation
- **Architecture** - Interactive Mermaid.js diagrams loaded from .mmd files
- **Security** - Security overview document

**Documentation Source: GitHub Pages**

Documentation is served from GitHub Pages at `https://psford.github.io/claudeProjects/`. The app's `docs.html` fetches markdown files client-side via CORS, allowing documentation updates without container rebuilds.

| Document | GitHub Pages URL |
|----------|------------------|
| App Explanation | `https://psford.github.io/claudeProjects/APP_EXPLANATION.md` |
| Project Guidelines | `https://psford.github.io/claudeProjects/claude_disp.md` |
| Functional Spec | `https://psford.github.io/claudeProjects/FUNCTIONAL_SPEC.md` |
| Technical Spec | `https://psford.github.io/claudeProjects/TECHNICAL_SPEC.md` |
| Security Overview | `https://psford.github.io/claudeProjects/SECURITY_OVERVIEW.md` |
| Diagrams | `https://psford.github.io/claudeProjects/diagrams/*.mmd` |

**To update production docs:** Push changes to the `/docs` folder on main branch. GitHub Pages deploys automatically.

### 6.1.2 Architecture Diagrams (Hybrid Approach)

Diagrams are stored as separate `.mmd` files in the GitHub Pages `/docs/diagrams/` folder:

| File | Type | Description |
|------|------|-------------|
| `project-structure.mmd` | AUTO | Solution dependency graph (regenerated during build if mermaid-graph tool available) |
| `service-architecture.mmd` | MANUAL | Backend services and external API connections |
| `data-flow.mmd` | MANUAL | Sequence diagram for stock lookup flow |
| `domain-models.mmd` | MANUAL | Class diagram of core models |
| `image-pipeline.mmd` | MANUAL | ML-based image processing flow |
| `frontend-architecture.mmd` | MANUAL | JavaScript modules and interactions |
| `api-endpoints.mmd` | MANUAL | REST API endpoint reference |

**Updating diagrams:**
1. Edit the corresponding `.mmd` file in `/docs/diagrams/` (GitHub Pages source)
2. Mermaid syntax documentation: https://mermaid.js.org/
3. Push to main branch - GitHub Pages deploys automatically

**Auto-generation (optional):**
To regenerate `project-structure.mmd` from the solution:
```bash
dotnet tool install -g mermaid-graph
dotnet mermaid-graph --sln . --output docs/diagrams/project-structure.mmd --direction TD
```

**MIME type configuration:**
`.mmd` files are served with `text/plain` content type via custom `FileExtensionContentTypeProvider` in Program.cs

### 6.1.3 Documentation Search (Fuse.js)

The documentation page includes fuzzy search across all documents using Fuse.js.

**Configuration:**
```javascript
this.fuse = new Fuse(this.searchIndex, {
    keys: [
        { name: 'title', weight: 2 },    // Headings weighted higher
        { name: 'content', weight: 1 }
    ],
    threshold: 0.4,          // 0 = exact, 1 = match anything
    ignoreLocation: true,    // Match anywhere in string
    includeMatches: true,    // For highlighting
    minMatchCharLength: 2
});
```

**Search Index Structure:**
- Documents parsed into sections by markdown headings (h1, h2, h3)
- Each section indexed with: `docKey`, `docTitle`, `title`, `content`, `headingId`
- Architecture diagram titles and descriptions also indexed
- Content truncated to 500 chars, markdown syntax stripped

**UI Behavior:**
- Debounced input (200ms) to reduce search frequency
- Results dropdown with up to 10 matches
- Highlighted matching terms in title and snippet
- Click result to navigate to document and section
- Press Escape or click outside to close

### 6.1.4 Scroll Spy (TOC Highlighting)

The TOC sidebar highlights the currently visible section as the user scrolls.

**Implementation:**
```javascript
// Scroll-based approach with requestAnimationFrame throttling
const updateActiveHeading = () => {
    const threshold = scrollerRect.top + 100; // 100px from top

    // Find last heading that scrolled past threshold
    for (const heading of headings) {
        if (heading.getBoundingClientRect().top <= threshold) {
            activeId = heading.id;
        } else {
            break;
        }
    }

    // Only update DOM if active changed (performance)
    if (activeId !== currentActiveId) {
        // Update TOC highlighting
    }
};
```

**CSS Styling:**
- Active item: blue left border, light blue background, blue text, bold
- Hover: subtle background highlight
- Smooth transitions for all states

### 6.2 JavaScript Modules

**api.js** - REST API client:
```javascript
const API = {
    baseUrl: '/api',
    getStockInfo(ticker) { ... },
    getHistory(ticker, period) { ... },
    getAnalysis(ticker, period) { ... },
    getSignificantMoves(ticker, threshold, period, from, to) { ... },
    getNews(ticker, days) { ... },
    search(query) { ... }
};
```

**app.js** - Application controller:
- Event binding (search, period change, chart type)
- Autocomplete with debouncing (300ms)
- Data rendering (stock info, metrics, news)
- State management (currentTicker, historyData)
- Image cache management (prefetch, refill, consume)
- Hover card display with cached images

**charts.js** - Plotly configuration:
- Candlestick chart traces
- Line chart traces
- Moving average overlays
- Portfolio chart for combined view
- Responsive layout
- Theme-aware colors (light/dark mode)
- Dynamic chart title: updates to reflect visible date range on scroll/zoom via `plotly_relayout` listener (`_attachDynamicTitle` method). Title DOM is updated directly (no Plotly.relayout loop).

**storage.js** - LocalStorage watchlist persistence:
- Privacy-first client-side storage (no PII sent to server)
- CRUD operations on localStorage
- Export watchlists to JSON file
- Import watchlists from JSON file
- Storage usage tracking

**symbolSearch.js** - Client-side instant search with weighted relevance (v2.14+):
```javascript
const SymbolSearch = {
    symbols: [],        // Array of {symbol, description}
    symbolMap: {},      // O(1) exact match lookup
    popularTickers: {}, // Popularity boost map (~100 well-known tickers)
    isLoaded: false,

    async load() { ... },           // Fetch /data/symbols.txt at page load
    search(query, limit) { ... },   // Weighted relevance scoring
    scoreMatch(entry, query) { ... }, // Calculate relevance score
    exists(symbol) { ... },         // Check if symbol exists
    get(symbol) { ... }             // Get symbol info by exact match
};
```
- Loads ~30K symbols (~315KB gzipped) at page load
- Sub-millisecond search latency (no network calls)
- Offline-capable once loaded
- 5-second debounced server fallback for unknown symbols

**Relevance Scoring Algorithm (v2.14+):**

| Match Type | Base Score | Example |
|------------|------------|---------|
| Exact ticker match | 1000 | Query "F" → "F" (Ford) |
| Ticker starts with query | 200 | Query "FORD" → "FORD..." |
| Description word starts with query | 100 | Query "ford" → "FORD Motor" |
| Description contains query | 25 | Query "ford" → "BedFORD" |

**Popularity Boost (added to base score):**

| Tier | Boost | Examples |
|------|-------|----------|
| Mega-cap / Household | +50 | AAPL, MSFT, F, TSLA |
| Major indices / ETFs | +40 | SPY, QQQ, ^DJI |
| Large-cap well-known | +30 | JPM, WMT, XOM, BA |
| Common tickers | +10 | USB, NEE, TSM |

Results sorted by score descending, then alphabetically for ties.

**watchlist.js** - Watchlist sidebar and combined view:
- Watchlist CRUD operations
- Ticker management (add/remove)
- Combined view toggle and state
- Holdings editor modal
- Portfolio chart rendering
- ±5% significant move markers
- Hover cards with market news
- Escape key handler for modal dismissal

**dragMeasure.js** - Click-and-drag performance measurement overlay:
- Self-contained module with state machine: `IDLE → MEASURING → PINNED → IDLE` (left-click) and `IDLE → ZOOMING → IDLE` (right-click)
- HTML overlay positioned over Plotly SVG for zero-flicker performance (no `Plotly.relayout()` during drag)
- Left-click drag: Shows floating bubble with % return, $ change, and date range, updating in real time
- Right-click drag: Zoom to selected date range with shaded preview
- Scroll wheel: Cursor-centered zoom with rAF-based throttling (accumulate-then-apply pattern for high-speed scroll wheels)
- Double-click: Reset zoom to full data range
- Escape: Dismiss pinned bubble or cancel active drag
- Supports both price charts (`dataType: 'price'`) and portfolio percent-change charts (`dataType: 'percent'`)
- Comparison mode shows both stocks' returns with colored labels
- `onRangeExtend` callback enables fetching additional historical data when user scrolls past data bounds (right edge clamped to last data point — no future dates). After extension completes, auto-retries if visible range still extends past data AND data actually grew (prevents infinite loops).
- Binary search (O(log n)) for nearest data point lookup
- Coordinate conversion via Plotly's `xaxis.p2c()` / `xaxis.d2p()` with linear interpolation fallback
- Attached in `app.js` (stock charts) and `watchlist.js` (portfolio charts)

**tileDashboard.js** - GridStack tile layout manager with physics engine:
- IIFE module `TileDashboard` — self-contained, no modifications to existing JS
- MutationObserver on `#results-section` triggers lazy GridStack init when results become visible
- Layout persistence via localStorage (`stockanalyzer_tile_layout` key, version 1)
- Close/reopen tiles via panel dropdown with content caching
- ResizeObserver on chart tile triggers `Plotly.Plots.resize()`
- Physics engine: spring transitions, lift effect, magnetic pull, FLIP animations, snap settle
- Web Audio API snap sound (1200Hz + 300Hz dual-oscillator)

### 6.3 JSON Theme System

The application uses a JSON-based theme system with 94+ CSS custom properties, visual effects, and optional background images and audio parameters.

**Theme Architecture:**

| Component | Location | Purpose |
|-----------|----------|---------|
| Theme Files | `wwwroot/themes/*.json` | Theme definitions with variables, effects, fonts |
| Azure Themes | `stockanalyzerblob.z13.web.core.windows.net/themes/` | Production theme hosting |
| ThemeLoader | `wwwroot/js/themeLoader.js` | Loads, applies, and switches themes |
| ThemePreview | `wwwroot/js/themePreview.js` | Mini-app preview for theme editor |
| ThemeEditor | `wwwroot/js/themeEditor.js` | AI-powered theme generation UI |
| ThemeAudio | `wwwroot/js/themeAudio.js` | Procedural audio from theme params |
| CanvasEffects | `wwwroot/js/canvasEffects.js` | Canvas-based visual effects (Matrix rain, snow, particles) |
| Theme Generator | `helpers/theme_generator.py` | FastAPI service for AI theme generation |

**Theme JSON Structure:**
```json
{
  "id": "theme-id",
  "name": "Theme Name",
  "version": "1.0.0",
  "extends": "dark",
  "meta": { "category": "dark", "icon": "moon", "iconColor": "#fff" },
  "background": {
    "image": "/images/backgrounds/bg.jpg",
    "overlay": "linear-gradient(...)",
    "blur": 0
  },
  "variables": {
    "bg-primary": "#0a0a0f",
    "text-primary": "#e0e0ff",
    "accent": "#ff71ce"
  },
  "effects": {
    "scanlines": { "enabled": true, "opacity": 0.08 },
    "vignette": { "enabled": true, "strength": 0.4 },
    "bloom": { "enabled": true, "contrast": 1.05 },
    "rain": { "enabled": false },
    "crtFlicker": { "enabled": false }
  },
  "fonts": { "primary": "...", "mono": "..." },
  "audio": {
    "key": "A", "mode": "minor",
    "chordProgression": ["i", "iv", "V", "i"],
    "texture": "pad", "tempo": 60
  }
}
```

**Theme Inheritance:**
Themes can extend base themes via `"extends": "dark"`. ThemeLoader deep-merges child variables over base, with circular inheritance detection.

**Loading Priority:**
1. Localhost: Local themes first (`/themes/`) for dev workflow
2. Production: Azure Blob Storage first, local fallback

**Visual Effects (CSS-based):**
- `scanlines`: CRT horizontal line overlay
- `vignette`: Darkened edges (radial gradient)
- `bloom`: Contrast/brightness boost for glow effect
- `rain`: Animated rain drops (CSS animation)
- `crtFlicker`: Subtle screen flicker animation

**Visual Effects (Canvas-based):**
Canvas effects are managed by `canvasEffects.js` with proper lifecycle (start/stop/cleanup):
- `rain`: Falling raindrops with angle and streak length (color, count, speed, angle, length, width)
- `matrixRain`: Falling Matrix-style characters with glow (color, speed, density, fontSize, glowIntensity)
- `snow`: Falling snowflakes (color, count, speed, wind)
- `particles`: Floating particles with optional connection lines (color, count, speed, connections)

Canvas effects render at z-index: 9995 (fixed position, above content, below CSS overlay effects at 9997-9999). ThemeLoader delegates canvas effects to CanvasEffects when `effects.<name>.enabled` is true.

**Theme Import Modal:**
Users can test custom themes without deploying via the import modal (`#theme-import-modal`):
- Access: Theme dropdown → "Import Custom Theme..."
- Paste JSON with id, variables, effects
- Apply immediately to see all effects (including canvas-based rain, snow, etc.)
- Download applied theme as JSON file
- Keyboard: Ctrl+Enter to apply, Escape to close
- Modal stays open after apply so Download button is accessible

**Theme Audio Parameters:**
Music-theory driven procedural audio synthesis:
- `key`: Root note (C, D, E, etc.)
- `mode`: Scale (major, minor, dorian, harmonic_minor, etc.)
- `chordProgression`: Roman numeral array (i, iv, V)
- `texture`: drone, pad, arpeggiated, pulsing
- `tempo`: BPM (0 = free time/ambient)

**Theme Generator Service:**
Python FastAPI sidecar (`helpers/theme_generator.py`):
- Mock mode (default): Keyword-matched pre-built themes, no API cost
- Live mode (`THEME_GENERATOR_LIVE=true`): Claude API for custom themes
- Endpoints: `/generate`, `/refine`, `/health`
- Port: 8001

**Prompt Sanitization (Security):**
User-entered prompts are sanitized before processing:
- Max length: 2000 characters (prevents DoS, controls API costs)
- Theme name max: 100 characters
- Control character removal (CWE-117 log injection prevention)
- Null byte stripping
- Suspicious prompt injection patterns logged (not blocked)
- Frontend: Character counters, `maxlength` attributes
- Backend: `sanitize_prompt()` function as security boundary

**Scope Enforcement (Anti-Abuse):**
The theme generator is locked to theme generation only:
- **Pre-flight validation** (`is_theme_related()`): Rejects prompts that don't contain theme keywords (colors, styles, aesthetics) or match off-topic patterns (greetings, homework requests, code generation, general questions)
- **System prompt hardening**: Claude instructed to ONLY output theme JSON, interpret ANY input as theme description (e.g., "tell me a joke" becomes Comedy theme with playful colors)
- **Response validation** (`validate_theme_response()`): Verifies response contains valid CSS color variables, rejects chatbot-style text responses
- Prevents token burn from users attempting to use as general-purpose chatbot

**Jailbreak Detection (Persistent Abuse Prevention):**
Tracks violations per IP and escalates to temporary blocks:
- **Rate limiting**: 2-second minimum between requests
- **Violation tracking**: 30-minute rolling window, violations recorded for off-topic requests and jailbreak patterns
- **Soft block**: After 3 violations → 5-minute cooldown
- **Hard block**: After 5 violations → 60-minute ban (escalates with repeat offenses)
- **Jailbreak patterns detected**: Base64/encoding tricks, multi-language evasion, instruction overrides, role-play escapes, theme word stuffing
- In-memory tracking resets on service restart (stateless between deploys)

**Available Themes:**
| ID | Name | Category |
|----|------|----------|
| light | Light | light |
| dark | Dark | dark |
| neon-noir | Neon Noir | dark |
| grimdark-space-opera | Grimdark Space Opera | dark |

### 6.4 Autocomplete Flow

```
User types → 300ms debounce → API.search(query) → Show dropdown
                                    ↓
User clicks result → Populate input → Hide dropdown
                                    ↓
User clicks Analyze → analyzeStock() → Load all data
```

### 6.5 Image Caching System

The application pre-caches ML-processed animal images for instant display in hover popups.
Images are fetched from the backend API, which handles ML detection and cropping server-side.

**Cache Configuration:**
```javascript
imageCache: {
    cats: [],           // Array of blob URLs from backend
    dogs: [],           // Array of blob URLs from backend
    isRefilling: { cats: false, dogs: false }  // Prevent concurrent refills
},
IMAGE_CACHE_SIZE: 50,       // Number of images to fetch per refill
IMAGE_CACHE_THRESHOLD: 10   // Trigger refill when cache drops below this
```

**Cache Flow:**
```
Page Load → prefetchImages()
                ↓
    ┌───────────┴───────────┐
    ↓                       ↓
fetchImagesFromBackend    fetchImagesFromBackend
('dogs', 50)              ('cats', 50)
    ↓                       ↓
GET /api/images/dog       GET /api/images/cat
(×50 requests)            (×50 requests)
    ↓                       ↓
Convert blob to URL       Convert blob to URL
    ↓                       ↓
Add to imageCache.dogs    Add to imageCache.cats
```

**Image Consumption:**
```
Hover on marker → getImageFromCache(type)
                        ↓
                  Remove blob URL from cache (no repeats)
                        ↓
                  Check cache.length < 10?
                        ↓ Yes
                  Trigger background refill from backend
```

**Backend Image Processing:**

| Endpoint | Response | Processing |
|----------|----------|------------|
| `GET /api/images/cat` | JPEG 320×150 | YOLOv8n detection → center crop |
| `GET /api/images/dog` | JPEG 320×150 | YOLOv8n detection → center crop |
| `GET /api/images/status` | JSON | Cache counts and timestamp |

### 6.6 Combined Watchlist View

The combined view aggregates multiple holdings into a single portfolio performance chart.

**State Management:**
```javascript
combinedView: {
    isOpen: false,              // View visibility
    watchlistId: null,          // Current watchlist
    period: '1y',               // Selected time period
    benchmark: null,            // SPY, QQQ, or null
    data: null,                 // CombinedPortfolioResult
    showMarkers: true,          // ±5% move markers toggle
    currentAnimal: 'cats',      // Hover card animal preference
    marketNews: [],             // General market news cache
    hoverTimeout: null,         // Delayed hover card show
    hideTimeout: null,          // Delayed hover card hide
    isHoverCardHovered: false   // Prevent hide while on card
}
```

**UI Components:**

| Component | Purpose |
|-----------|---------|
| View Toggle | Switch between Simple View (list) and Combined View (chart) |
| Period Selector | 1mo, 3mo, 6mo, 1y, 2y time periods |
| Benchmark Buttons | Compare portfolio against SPY or QQQ |
| Holdings Editor | Modal to set weighting mode and holdings values |
| Marker Toggle | Show/hide ±5% significant move markers |
| Cat/Dog Toggle | Switch animal images in hover cards |

**Significant Move Markers:**
```javascript
// Green triangles for positive ≥5% moves
traces.push({
    type: 'scatter',
    mode: 'markers+text',
    x: positiveMoves.map(m => m.date),
    y: positiveMoves.map(m => dataByDate[m.date]),
    marker: { symbol: 'triangle-up', size: 12, color: '#10B981' },
    name: 'Gains ≥5%'
});

// Red triangles for negative ≤-5% moves
traces.push({
    type: 'scatter',
    mode: 'markers+text',
    x: negativeMoves.map(m => m.date),
    y: negativeMoves.map(m => dataByDate[m.date]),
    marker: { symbol: 'triangle-down', size: 12, color: '#EF4444' },
    name: 'Losses ≥5%'
});
```

**Hover Cards (Wikipedia-style):**
- Delayed show (500ms) on marker hover via Plotly `plotly_hover` event
- Shows market news instead of stock-specific news
- Includes cat/dog image from cache (same system as main page)
- Delayed hide (300ms) with cancel on card mouseenter
- Z-index layering: combined view (z-40), hover card (z-50)

**Holdings Editor Modal:**
| Field | Description |
|-------|-------------|
| Weighting Mode | Radio buttons: Equal, Shares, Dollars |
| Holdings List | Shows ticker, current price, and input field for shares/dollars |
| Weight Display | Calculated weight percentage for each ticker |
| Save Button | Calls `PUT /api/watchlists/{id}/holdings` |

**Combined View Flow:**
```
Toggle Combined View → GET /api/watchlists/{id}/combined
                            ↓
                      Render portfolio chart with Plotly
                            ↓
                      GET /api/news/market (for hover cards)
                            ↓
                      Setup Plotly hover events
                            ↓
                      User hovers on ±5% marker
                            ↓
                      Show hover card with market news + animal image
```

### 6.7 Date Range Selection with Flatpickr

The date picker uses a device-aware strategy: modern desktop browsers get the flatpickr widget for rich UX, while mobile devices fall back to the native `<input type="date">` picker for optimal touch experience.

**Device Detection:**
```javascript
const hasFineTouchPointer = window.matchMedia('(pointer: fine)').matches;
// true: desktop (mouse/trackpad) → use flatpickr
// false: mobile/tablet (coarse touch) → use native <input type="date">
```

**Flatpickr Integration (Desktop Only):**
- Library: flatpickr 4.6.13 (~16KB + 4KB CSS, CDN with SRI hash)
- Initialized only when user selects "Custom" date mode
- Destroyed when switching back to preset mode to save memory
- Includes time picker for intraday analysis (time portion not currently used in API)

**CSS Theming:**
- Dark mode support via CSS custom properties (`--fp-*` variables)
- Override mechanism: `.dark .flatpickr-calendar { --fp-bg-color: ... }`
- No JavaScript theme swapping required; theme follows app-wide `dark` class

**Date Range State Management:**
```javascript
app.dateRange = {
    endDatePreset: 'PBD',                // Previous Business Day / LME / LQE / LYE / Custom
    startDatePreset: '1Y',               // 1D–30Y / MTD / YTD / Max / Custom
    resolvedEndDate: '2026-02-01',       // Actual calculated date (readonly input display)
    resolvedStartDate: '2025-02-01',     // Actual calculated date (readonly input display)
    customEndDate: null,                 // User-selected custom end date (flatpickr)
    customStartDate: null                // User-selected custom start date (flatpickr)
};
```

**Flatpickr Lifecycle:**
1. Page load: flatpickr lib loaded but NOT initialized
2. User selects "Custom" end/start preset: flatpickr instance created on first focus
3. User picks date: `resolvedEndDate`/`resolvedStartDate` updated, chart re-fetched
4. User switches back to preset: flatpickr destroyed, memory freed

### 6.8 Tile Dashboard Architecture

The results section uses GridStack.js v12 to render 7 draggable/resizable tiles with a physics-based animation engine. All existing JS (app.js, charts.js, etc.) remains completely unmodified — the tile system is a cosmetic overlay.

**Library:** GridStack.js 12.4.2 (downloaded locally to `wwwroot/lib/gridstack/`)

**Files:**
| File | Purpose |
|------|---------|
| `lib/gridstack/gridstack-all.min.js` | GridStack v12 library (83KB) |
| `lib/gridstack/gridstack.min.css` | GridStack base styles (4KB) |
| `js/tileDashboard.js` | Tile management + physics engine (~620 lines) |
| `src/input.css` | Tile CSS (~280 lines added to Tailwind source) |

**Tile Layout (12-column grid):**

| Tile ID | Content | gs-w | gs-h | min-w | min-h |
|---------|---------|------|------|-------|-------|
| `tile-chart` | `#stock-chart` (Plotly) | 8 | 5 | 4 | 3 |
| `tile-watchlist` | Watchlist management (create, export, import, watchlist-container) | 4 | 5 | 3 | 3 |
| `tile-info` | `#stock-info` + watchlist dropdown | 8 | 5 | 3 | 3 |
| `tile-metrics` | `#key-metrics` | 4 | 4 | 2 | 2 |
| `tile-performance` | `#performance-metrics` | 6 | 3 | 3 | 2 |
| `tile-moves` | `#significant-moves` | 6 | 3 | 3 | 2 |
| `tile-news` | `#news-list` | 12 | 3 | 4 | 2 |

**GridStack Config:**
```javascript
GridStack.init({
    column: 12, cellHeight: 70, margin: 12,
    animate: true, float: false, handle: '.tile-header',
    columnOpts: { breakpointForWindow: true, breakpoints: [{ w: 768, c: 1 }] }
}, '#tile-grid');
```

**Lazy Initialization (zero app.js changes):**
```
DOMContentLoaded → TileDashboard.boot()
    ↓
MutationObserver on #results-section (watching 'class' attribute)
    ↓
App.showResults() removes 'hidden' class → observer fires
    ↓
requestAnimationFrame → initGridStack()
    ↓
Guard: if (initialized) return; — prevents re-init on subsequent searches
```

**Chart Integration:**
- CSS `height: 100% !important` on `#stock-chart` overrides app.js inline pixel height
- ResizeObserver on `#tile-chart-body` calls `Plotly.Plots.resize()` on tile resize
- DragMeasure overlay unaffected — attaches to chart body, not tile header

**Layout Persistence:**
```javascript
// localStorage key: 'stockanalyzer_tile_layout'
// LAYOUT_VERSION: 7 (bumped on layout changes; clears saved layouts)
// Format: { version: 7, tiles: [{ id, x, y, w, h, visible }] }
// Saves on: GridStack 'change' event (move/resize)
// Restores on: initGridStack() via grid.batchUpdate()
```

**Physics Engine (carried over from prototype):**

| Feature | Implementation |
|---------|---------------|
| Spring transitions | CSS `cubic-bezier(0.25, 1.1, 0.5, 1)` on top/width/height (0.35s) |
| Lift effect | Dragged tile: `scale(1.025)`, elevated shadow, `opacity: 0.92` |
| Magnetic pull | Quadratic easing within 50px threshold, 0.35 strength factor |
| Snap settle | `@keyframes snapSettle` — scale overshoot (1.012) + blue glow (400ms) |
| Neighbor FLIP | MutationObserver on `gs-x` attribute changes → WAAPI `element.animate()` |
| Placeholder | `@keyframes placeholderReveal` — fade in + scale from 0.96 (200ms) |
| Grid dot glow | `.grid-stack.drag-active` — background dots pulse during drag |
| Snap audio | Web Audio API: 1200Hz + 300Hz oscillators, 80ms duration |
| Haptics | `navigator.vibrate(8)` on snap (mobile) |
| Coupled resize | Adjacent tiles shrink/grow inversely during horizontal resize instead of being pushed. Uses `float(true)` during resize + `maxW` constraint to respect neighbor `minW`. `_findRowNeighbors()` detects horizontally adjacent tiles sharing at least one row. |

**Tile Header Controls:**
- Lock button: Toggles `noMove`/`noResize`/`locked` via GridStack API + `tile-locked` class (dashed border, diagonal hatch)
- Close button: Removes widget via `grid.removeWidget()`, content cached for reopen
- Panel dropdown: 7 checkboxes (one per tile) + Reset Layout button

**Watchlist Toggle (Header Star Button):**
- `#watchlist-toggle-btn` in the page header (star SVG, visible at all screen sizes)
- Click toggles `tile-watchlist` via `closeTile()`/`reopenTile()`
- `.watchlist-toggle-active` class applied when tile is visible (yellow star highlight)
- On reopen: `setTimeout(() => Watchlist.loadWatchlists(), 200)` re-binds events

**Horizontal Expansion on Tile Close:**
- When a tile is closed, its horizontal neighbor on the same row expands to fill the gap
- `expandRowNeighbor(closingId, closingNode)` finds left/right adjacent tile (sharing row overlap) and increases its width by the closed tile's width
- State tracked in `tileExpansions[tileId] = { neighborId, origW, origX }`
- On reopen: expanded neighbor shrinks back to original width before the tile is re-added at its original position
- General-purpose: works for any adjacent tile pair (chart/watchlist, info/metrics, etc.)

**Watchlist Dropdown Fix:**
- `.grid-stack-item[gs-id="tile-info"] .tile-card` and `.tile-body` have `overflow: visible` to prevent clipping

---

## 7. Configuration

### 7.1 Environment Variables

| Variable | Required | Description |
|----------|----------|-------------|
| `FINNHUB_API_KEY` | Yes | Finnhub API authentication key |

**Configuration Priority:**
1. `appsettings.json` → `Finnhub:ApiKey`
2. Environment variable `FINNHUB_API_KEY`

### 7.2 appsettings.json

```json
{
  "Finnhub": {
    "ApiKey": "your_api_key_here"
  },
  "ImageProcessing": {
    "ModelPath": "MLModels/yolov8n.onnx",
    "CacheSize": 50,
    "RefillThreshold": 10,
    "TargetWidth": 320,
    "TargetHeight": 150
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information"
    }
  }
}
```

### 7.3 Program.cs Configuration

```csharp
// CORS for frontend
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Service registration
builder.Services.AddSingleton<StockDataService>();
builder.Services.AddSingleton<NewsService>();
builder.Services.AddSingleton<AnalysisService>();
builder.Services.AddSingleton<ImageProcessingService>();
builder.Services.AddSingleton<ImageCacheService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ImageCacheService>());
```

---

## 8. Testing

### 8.1 Test Project Structure

```
tests/
└── StockAnalyzer.Core.Tests/
    ├── StockAnalyzer.Core.Tests.csproj
    ├── Services/
    │   ├── AggregatedNewsServiceTests.cs    # 13 tests - Multi-source aggregation
    │   ├── AnalysisServiceTests.cs          # 14 tests - Technical indicators
    │   ├── HeadlineRelevanceServiceTests.cs # 18 tests - ML scoring logic
    │   ├── MarketauxServiceTests.cs         # 18 tests - Marketaux API
    │   ├── NewsServiceTests.cs              # 11 tests - Finnhub API
    │   ├── StockDataServiceTests.cs         # 15 tests (3 skipped integration)
    │   └── WatchlistServiceTests.cs         # Portfolio tests
    ├── Models/
    │   └── ModelCalculationTests.cs         # 27 tests
    └── TestHelpers/
        └── TestDataFactory.cs               # Test data generators
```

### 8.2 Test Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| xUnit | 2.6.6 | Test framework |
| xUnit.runner.visualstudio | 2.5.6 | Test runner |
| Microsoft.NET.Test.Sdk | 17.9.0 | Test SDK |
| Moq | 4.20.70 | Mocking framework |
| FluentAssertions | 6.12.0 | Assertion library |
| coverlet.collector | 6.0.0 | Code coverage |

### 8.3 Test Coverage

| Category | Tests | Description |
|----------|-------|-------------|
| AggregatedNewsService | 13 | Multi-source news aggregation, deduplication, scoring |
| AnalysisService | 19 | Moving averages, significant moves, performance, RSI, MACD, Stochastic calculations |
| HeadlineRelevanceService | 18 | Relevance scoring, ticker detection, deduplication |
| MarketauxService | 18 | HTTP mocking, sentiment mapping, API token handling |
| NewsService | 11 | HTTP mocking, date range handling, JSON parsing |
| StockDataService | 12 | Query validation, period mapping, dividend yield fix |
| Model Calculations | 27 | Calculated properties on record types |
| **Total** | **113+** | Plus 3 skipped integration tests |

### 8.4 Running Tests

```bash
# Run all .NET tests
cd stock_analyzer_dotnet
dotnet test

# Run with verbose output
dotnet test --logger "console;verbosity=detailed"

# Run specific test class
dotnet test --filter "FullyQualifiedName~AnalysisServiceTests"

# Run with coverage
dotnet test --collect:"XPlat Code Coverage"
```

### 8.5 Frontend JavaScript Tests

**Location:** `wwwroot/tests/portfolio.test.js`

**Framework:** Jest with jsdom environment

**Installation:**
```bash
cd src/StockAnalyzer.Api/wwwroot
npm install
```

**Running Tests:**
```bash
npm test                    # Run all tests
npm test -- --watch         # Watch mode
npm test -- --coverage      # With coverage report
```

**Test Coverage:**

| Function | Tests | Description |
|----------|-------|-------------|
| `calculateWeights` | 7 | Equal, shares, dollars weighting modes |
| `aggregatePortfolioData` | 7 | Portfolio aggregation, weighted returns, data format handling |
| `normalizeToPercentChange` | 4 | Percent change normalization, data/prices array handling |
| `findSignificantMoves` | 6 | ±5% move detection, threshold boundary testing |
| Integration | 1 | Full workflow: weights → aggregate → significant moves |
| **Total** | **25** | |

**Key Test Scenarios:**
- Equal, shares, and dollars weighting modes
- API response format handling (`data` vs `prices` arrays)
- Missing data across tickers
- Threshold boundary conditions
- Empty data handling

### 8.6 Mocking Strategy

**NewsService:** Constructor accepts optional `HttpClient` for dependency injection:
```csharp
public NewsService(string apiKey, HttpClient? httpClient = null)
```

**AnalysisService:** Constructor accepts optional `NewsService`:
```csharp
public AnalysisService(NewsService? newsService = null)
```

**StockDataService:** Uses concrete `YahooClient` from OoplesFinance. Full mocking would require introducing an interface wrapper. Current tests focus on:
- Query validation logic
- Internal helper method behavior (documented via tests)
- Integration tests marked as skipped

---

## 9. Deployment

### 9.1 Prerequisites

- .NET 8.0 SDK
- Finnhub API key (free tier: https://finnhub.io/)

### 9.2 Installation Steps

```bash
# 1. Navigate to project
cd stock_analyzer_dotnet

# 2. Configure API key (choose one method)
# Option A: Set environment variable
set FINNHUB_API_KEY=your_key_here

# Option B: Edit appsettings.json
# Add key under Finnhub:ApiKey

# 3. Build the solution
dotnet build

# 4. Run the application
dotnet run --project src/StockAnalyzer.Api

# 5. Open browser
# http://localhost:5000
```

### 9.3 File Structure

```
stock_analyzer_dotnet/
├── StockAnalyzer.sln
├── .gitignore
├── Dockerfile
├── docker-compose.yml
├── DEPLOYMENT_ORACLE.md
├── docs/
│   ├── FUNCTIONAL_SPEC.md
│   ├── TECHNICAL_SPEC.md
│   └── DEPLOYMENT_AZURE.md             # Azure App Service deployment guide
├── infrastructure/
│   └── azure/
│       ├── main.bicep                  # Azure IaC template
│       ├── parameters.json             # Deployment parameters
│       └── deploy.ps1                  # Deployment script
├── src/
│   ├── StockAnalyzer.Api/
│   │   ├── Program.cs
│   │   ├── appsettings.json
│   │   ├── StockAnalyzer.Api.csproj
│   │   ├── MLModels/
│   │   │   └── yolov8n.onnx           # YOLOv8 nano model (~12MB)
│   │   └── wwwroot/
│   │       ├── index.html
│   │       └── js/
│   │           ├── api.js
│   │           ├── app.js
│   │           └── charts.js
│   └── StockAnalyzer.Core/
│       ├── StockAnalyzer.Core.csproj
│       ├── Data/
│       │   ├── StockAnalyzerDbContext.cs    # EF Core DbContext
│       │   ├── SqlWatchlistRepository.cs    # Azure SQL implementation
│       │   ├── SqlCachedImageRepository.cs  # Image cache persistence
│       │   ├── DesignTimeDbContextFactory.cs
│       │   ├── Entities/
│       │   │   ├── WatchlistEntity.cs       # EF Core entities
│       │   │   └── CachedImageEntity.cs     # Cached image entity
│       │   └── Migrations/                   # EF Core migrations
│       ├── Models/
│       │   ├── StockInfo.cs
│       │   ├── CompanyProfile.cs
│       │   ├── HistoricalData.cs
│       │   ├── NewsItem.cs
│       │   ├── SearchResult.cs
│       │   ├── SignificantMove.cs
│       │   ├── Watchlist.cs
│       │   └── TechnicalIndicators.cs    # RsiData, MacdData, StochasticData records
│       └── Services/
│           ├── StockDataService.cs
│           ├── NewsService.cs
│           ├── AnalysisService.cs
│           ├── WatchlistService.cs
│           ├── IWatchlistRepository.cs
│           ├── JsonWatchlistRepository.cs   # Local file storage
│           ├── ImageProcessingService.cs   # ML detection + cropping
│           ├── ImageCacheService.cs        # Background cache management
│           └── ICachedImageRepository.cs   # Image cache interface
└── tests/
    └── StockAnalyzer.Core.Tests/
        ├── StockAnalyzer.Core.Tests.csproj
        ├── Services/
        ├── Models/
        └── TestHelpers/
```

### 9.4 CI/CD Pipelines

The project has two CI/CD systems configured:

#### GitHub Actions (Cloud)

**File:** `.github/workflows/dotnet-ci.yml`

**Triggers:**
- Push to `master` branch (changes in `stock_analyzer_dotnet/**`)
- Pull requests to `master`
- Manual trigger via `workflow_dispatch`

**Jobs:**
1. `frontend-tests` (Ubuntu) - JavaScript unit tests with Jest
2. `build-and-test` (Ubuntu) - Primary .NET build, runs tests, uploads artifacts
3. `build-windows` - Verification build on Windows

**Stages:**
```
Frontend Tests:
Checkout → Setup Node.js 20.x → npm install → Jest tests → Upload coverage

.NET Build:
Checkout → Setup .NET 8.0 → Restore → Build (Release) → Test → Upload Artifacts
```

#### Jenkins (Local Docker)

**File:** `Jenkinsfile` (project root)

**Access:** http://localhost:8080/job/StockAnalyzer/

**Prerequisites:**
- Docker Desktop running
- Jenkins container with Docker socket access
- Jenkins credentials stored in `.env`:
  - `JENKINS_USER` - Jenkins admin username
  - `JENKINS_API_TOKEN` - API token for CLI/automation

**Pipeline Stages:**
```groovy
Checkout → Restore → Build → Test (.NET) → Test (JavaScript) → Publish
```

**Docker Agent:** `mcr.microsoft.com/dotnet/sdk:8.0` (with Node.js installed for JS tests)

**Helper Scripts:**

| Script | Purpose |
|--------|---------|
| `helpers/slack_bot.py` | Slack bot manager (start/stop/status/restart listener + acknowledger) |
| `helpers/slack_listener.py` | Slack message listener with image attachment support (poll/socket modes, downloads images to `slack_attachments/`) |
| `helpers/slack_acknowledger.py` | Watches for read messages and adds checkmark reaction |
| `helpers/jenkins-local.ps1` | Main Jenkins management script |
| `helpers/jenkins-console.ps1` | Fetch build console output |
| `helpers/jenkins-reload.ps1` | Reload Jenkins configuration |
| `helpers/hooks/jenkins_pre_push.py` | Pre-push hook for CI validation |

**Usage:**
```powershell
# Start/stop/restart Jenkins
.\helpers\jenkins-local.ps1 start
.\helpers\jenkins-local.ps1 stop
.\helpers\jenkins-local.ps1 restart

# Check status
.\helpers\jenkins-local.ps1 status

# Trigger a build manually
.\helpers\jenkins-local.ps1 build

# View build logs
.\helpers\jenkins-local.ps1 logs
```

**Pre-Push Hook Integration:**

The project includes a pre-push hook (`helpers/hooks/jenkins_pre_push.py`) that:
1. Triggers a Jenkins build before every `git push`
2. Waits for the build to complete (5-minute timeout)
3. Blocks the push if the build fails
4. Gracefully skips if Jenkins is not running

Install the hook:
```powershell
py -m pre_commit install --hook-type pre-push
```

**Required Plugins:**
- `docker-workflow`
- `docker-plugin`
- `git`
- `workflow-aggregator`

**Credentials:**
- `github-pat` - GitHub Personal Access Token for private repo access

See `docs/CI_CD_SETUP.md` for complete setup and troubleshooting guide.

#### CI/CD Security Tools

**File:** `.github/workflows/codeql.yml`

**CodeQL Analysis (GitHub Actions):**
- Automated SAST for C# and Python code
- Runs on push, PR, and weekly schedule
- Results uploaded to GitHub Security tab
- Uses `security-extended` query pack for comprehensive coverage

**Triggers:**
- Push to `master` branch
- Pull requests to `master`
- Weekly scheduled scan (Monday 6 AM UTC)

**Security Toolchain:**
| Tool | Type | Integration |
|------|------|-------------|
| CodeQL | SAST (multi-language) | GitHub Actions |
| SecurityCodeScan.VS2019 | .NET SAST | Build-time analyzer |
| Bandit | Python SAST | Pre-commit hook |
| detect-secrets | Secrets detection | Pre-commit hook |
| Claude Code hooks | SDLC guard rails | PreToolUse/PostToolUse hooks |
| Dependabot | Dependency scanning | GitHub (enabled) |
| Jenkins CI | Full test suite | Pre-push hook (local)

See `docs/CI_CD_SECURITY_PLAN.md` for the full security migration roadmap.

#### GitHub Actions - Azure Deployment

**File:** `.github/workflows/azure-deploy.yml`

**Purpose:** Automated deployment to Azure App Service

**Triggers:**
- Push to `master`/`main` branch (changes in `stock_analyzer_dotnet/**`)
- Manual trigger via `workflow_dispatch`

**Jobs:**
1. `build-and-test` - Build and run unit tests
2. `build-container` - Build Docker image, push to ghcr.io
3. `deploy` - Deploy to Azure App Service, run migrations, health check

**Required Secrets:**
- `AZURE_CREDENTIALS` - Service principal JSON for Azure authentication

**Deployment Flow:**
```
Build → Test → Build Container → Push to GHCR → Deploy to App Service → Run Migrations → Health Check
```

### 9.5 Azure Deployment

See `docs/DEPLOYMENT_AZURE.md` for the complete Azure deployment guide.

#### Live Deployment

**Production URL:** https://psfordtaurus.com (custom domain with Cloudflare SSL)

**Alternative URLs:**
- https://www.psfordtaurus.com (www subdomain)
- https://docs.psfordtaurus.com (documentation site, hosted on GitHub Pages)
- https://app-stockanalyzer-prod.azurewebsites.net (direct App Service access)

**DNS/SSL Configuration:**
- Domain: `psfordtaurus.com` (registered at Hover)
- DNS: Cloudflare CNAME → app-stockanalyzer-prod.azurewebsites.net
- SSL: Cloudflare Full (strict) - HTTPS end-to-end
- Cloudflare Zone ID: `cb047b6224a4ebb8e7a94d855bcde93b`

**Health Endpoints:**
- `/health/live` - Liveness probe (basic app check)
- `/health/ready` - Readiness probe (external dependencies)
- `/health` - Full health status JSON

#### Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                     Azure Resource Group                     │
│                  (rg-stockanalyzer-prod)                    │
│                       West US 2                              │
├─────────────────────────────────────────────────────────────┤
│  ┌──────────────────────┐    ┌─────────────────────────┐   │
│  │  App Service Plan    │    │  Azure SQL Database     │   │
│  │  (asp-stockanalyzer) │    │  (Basic 5 DTU)          │   │
│  │  B1 Linux            │    │  sql-stockanalyzer-     │   │
│  └──────────┬───────────┘    │  er34ug                 │   │
│             │                └─────────────────────────┘   │
│  ┌──────────▼───────────┐              ▲                   │
│  │  App Service         │──────────────┘                   │
│  │  (app-stockanalyzer- │    Connection String             │
│  │   prod)              │                                  │
│  │  Docker from ACR     │                                  │
│  └──────────────────────┘                                  │
│  ┌──────────────────────┐    ┌─────────────────────────┐   │
│  │  Container Registry  │    │  Key Vault              │   │
│  │  (ACR Basic)         │    │  (kv-stockanalyzer)     │   │
│  │  stockanalyzer:prod  │    │  SQL password, API keys │   │
│  └──────────────────────┘    └─────────────────────────┘   │
└─────────────────────────────────────────────────────────────┘
          │
          │ Cloudflare CNAME
          ▼
   psfordtaurus.com → app-stockanalyzer-prod.azurewebsites.net
```

**Estimated Monthly Cost:** ~$18-20/month
- App Service B1: ~$13/mo
- Azure SQL Basic: ~$5/mo
- Container Registry Basic: ~$0.17/day
- Key Vault: ~$0.03/10k operations

#### Database Support

The application supports two storage modes for watchlist data:

**1. Azure SQL / SQL Server (Production)**
- Uses Entity Framework Core with `SqlWatchlistRepository`
- Automatic migrations on startup in Production
- Connection string via `ConnectionStrings:DefaultConnection`

**2. JSON File (Development/Fallback)**
- Uses `JsonWatchlistRepository`
- File stored at `data/watchlists.json`
- Active when no connection string is configured

**Storage Mode Detection:**
```csharp
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (!string.IsNullOrEmpty(connectionString))
{
    // SQL mode - register DbContext and SqlWatchlistRepository
}
else
{
    // JSON mode - register JsonWatchlistRepository
}
```

#### Symbol Database (Fast Ticker Search)

The application maintains a local cache of ~30K stock symbols in Azure SQL for sub-10ms search performance. This eliminates cascading API calls during ticker search.

**Schema:**
```sql
CREATE TABLE Symbols (
    Symbol NVARCHAR(20) PRIMARY KEY,     -- Ticker symbol (e.g., "AAPL")
    DisplaySymbol NVARCHAR(50),          -- Display version from Finnhub
    Description NVARCHAR(500),           -- Company name
    Type NVARCHAR(50),                   -- Common Stock, ETF, ADR, etc.
    Exchange NVARCHAR(50),               -- Exchange (US for US markets)
    Mic NVARCHAR(20),                    -- Market Identifier Code
    Currency NVARCHAR(10),               -- Trading currency
    Figi NVARCHAR(50),                   -- FIGI identifier
    Country NVARCHAR(10) DEFAULT 'US',   -- Country code
    IsActive BIT DEFAULT 1,              -- Whether actively traded
    LastUpdated DATETIME2,               -- Last refresh timestamp
    CreatedAt DATETIME2                  -- Record creation timestamp
);

-- Standard B-tree indexes
CREATE INDEX IX_Symbols_Description ON Symbols(Description);
CREATE INDEX IX_Symbols_Type ON Symbols(Type);
CREATE INDEX IX_Symbols_Country_Active ON Symbols(Country, IsActive);

-- Full-Text Search for fast description search (v2.11+)
CREATE FULLTEXT CATALOG StockAnalyzerCatalog AS DEFAULT;
CREATE FULLTEXT INDEX ON Symbols(Description)
    KEY INDEX PK_Symbols
    ON StockAnalyzerCatalog
    WITH CHANGE_TRACKING AUTO;
```

**Full-Text Search (v2.11+):**

The search uses SQL Server Full-Text Search with the `CONTAINS` predicate for efficient description matching on 30K+ symbols:

```sql
SELECT Symbol, Description, Exchange, Type
FROM Symbols
WHERE Symbol LIKE @prefix
   OR CONTAINS(Description, @ftsQuery)
ORDER BY Rank, Symbol
```

- **Why FTS:** Standard `LIKE '%term%'` forces full table scans. FTS uses inverted indexes for O(log n) lookups.
- **Prefix matching:** FTS query format is `"APPLE*"` for prefix matching.
- **Fallback:** If FTS is unavailable (SQL Server Express without FTS, InMemory for tests), automatically falls back to LINQ-based search.
- **Performance:** Sub-10ms latency on 30K symbols (vs 1-4 seconds with LIKE scans).

**Search Ranking Algorithm:**

| Priority | Match Type | Example |
|----------|-----------|---------|
| 1 | Exact symbol match | Query "AAPL" matches AAPL |
| 2 | Symbol prefix | Query "AAP" matches AAPL, AAPD |
| 3 | Description contains (via FTS) | Query "Apple" matches Apple Inc |

**Background Refresh (SymbolRefreshService):**
- Runs as `BackgroundService`
- Daily refresh at 2 AM UTC (configurable via `SymbolDatabase:RefreshHourUtc`)
- Auto-seeds database on startup if Symbols table is empty
- Fetches from Finnhub: `GET /stock/symbol?exchange=US`
- Marks delisted symbols as `IsActive = false`

**Admin Endpoints:**
- `POST /api/admin/symbols/refresh` - Manually trigger symbol refresh
- `GET /api/admin/symbols/status` - Returns active count, last refresh time, API key status

**Fallback Behavior:**
- If local DB has no results or is unavailable, falls back to API providers (TwelveData → FMP → Yahoo)
- Quotes and historical data always use API providers (symbol DB is search-only)

#### Security Master & Historical Prices (data schema)

The application maintains a separate `data` schema for domain data (securities and price history), distinct from the `dbo` schema used for operational tables (watchlists, symbols, cached images).

**Schema Separation Rationale:**
- **Clear ownership** - Operational vs. analytical data separation
- **Security** - Can grant read-only access to `data` schema for reporting
- **Backup strategy** - `data` schema (larger) can be backed up on different schedule
- **Future scaling** - Could move `data` schema to separate database later

**MicExchange Table (ISO 10383 Reference):**

Reference table for ISO 10383 Market Identifier Codes (MICs). Enables normalized exchange identification across securities.

```sql
CREATE TABLE data.MicExchange (
    MicCode CHAR(4) PRIMARY KEY,                    -- ISO 10383 MIC code (e.g., "XNYS")
    ExchangeName NVARCHAR(200) NOT NULL,            -- Display name (e.g., "New York Stock Exchange")
    Country CHAR(2) NOT NULL,                       -- ISO 3166 country code (e.g., "US")
    IsActive BIT DEFAULT 1                          -- Whether this exchange is actively trading
);
```

**MIC Seed Data:**
- Contains ~2,274 rows from ISO 10383 standard as of 2026-02-25
- Key US exchanges: XNYS (NYSE), XNAS (NASDAQ), ARCX (NYSE Arca), BATS (BATS Exchange), OTCM (OTC Markets), PINX (Pink Sheets)
- All exchanges marked IsActive based on ISO status field (ACTIVE/UPDATED = 1, others = 0)

**MicExchange Entity Configuration (EF Core):**

- Configured in `StockAnalyzerDbContext.OnModelCreating()` with Fluent API
- PK: MicCode (char(4), fixed-length)
- Properties: ExchangeName (nvarchar(200), required), Country (char(2), fixed-length, required), IsActive (bit, default=1)
- No navigation relationships beyond the reverse relationship from SecurityMaster

**EF Core Migration: AddMicExchangeTable**

- Migration: `20260227013220_AddMicExchangeTable.cs`
- Drops Exchange column from SecurityMaster table
- Adds MicCode column (char(4), nullable, FK to MicExchange)
- Creates MicExchange table with 2817 rows from ISO 10383 standard seed data
- Creates FK constraint from SecurityMaster.MicCode → MicExchange.MicCode with OnDelete.SetNull
- Includes key US exchanges: XNYS (NYSE), XNAS (NASDAQ), ARCX (NYSE Arca), BATS, OTCM, PINX
- Down() migration properly re-adds Exchange column for rollback

**SecurityMaster Table:**
```sql
CREATE TABLE data.SecurityMaster (
    SecurityAlias INT IDENTITY(1,1) PRIMARY KEY,  -- Auto-increment for efficient joins
    PrimaryAssetId NVARCHAR(50),                   -- Future: CUSIP, ISIN, etc.
    IssueName NVARCHAR(200) NOT NULL,              -- Full name (e.g., "Apple Inc.")
    TickerSymbol NVARCHAR(20) NOT NULL,            -- Ticker (e.g., "AAPL")
    MicCode CHAR(4),                                -- ISO 10383 Market Identifier Code (e.g., "XNYS")
    SecurityType NVARCHAR(50),                      -- Common Stock, ETF, ADR, etc.
    Country NVARCHAR(10),                           -- Country code (e.g., "USA")
    Currency NVARCHAR(10),                          -- Currency (e.g., "USD")
    Isin NVARCHAR(20),                              -- International Securities ID Number
    IsActive BIT DEFAULT 1,                         -- Whether actively traded
    IsTracked BIT DEFAULT 0,                        -- Whether in tracked universe for gap-filling
    IsEodhdUnavailable BIT DEFAULT 0,               -- Whether EODHD has no data for this security
    IsEodhdComplete BIT DEFAULT 0,                  -- Whether all available EODHD data has been loaded
    ImportanceScore INT DEFAULT 5,                  -- Calculated importance (1-10, 10=most important)
    CreatedAt DATETIME2 DEFAULT GETUTCDATE(),
    UpdatedAt DATETIME2 DEFAULT GETUTCDATE(),
    FOREIGN KEY (MicCode) REFERENCES data.MicExchange(MicCode) ON DELETE SET NULL
);

CREATE UNIQUE INDEX IX_SecurityMaster_TickerSymbol ON data.SecurityMaster(TickerSymbol);
CREATE INDEX IX_SecurityMaster_IsActive ON data.SecurityMaster(IsActive);
```

**Prices Table:**
```sql
CREATE TABLE data.Prices (
    Id BIGINT IDENTITY(1,1) PRIMARY KEY,          -- BIGINT for >2B row scale
    SecurityAlias INT NOT NULL,                    -- FK to SecurityMaster
    EffectiveDate DATE NOT NULL,                   -- Trading date (no time component)
    Open DECIMAL(18,4) NOT NULL,
    High DECIMAL(18,4) NOT NULL,
    Low DECIMAL(18,4) NOT NULL,
    Close DECIMAL(18,4) NOT NULL,
    Volatility DECIMAL(10,6),                      -- Calculated volatility
    Volume BIGINT,                                  -- Trading volume
    AdjustedClose DECIMAL(18,4),                   -- Split/dividend adjusted
    CreatedAt DATETIME2 DEFAULT GETUTCDATE(),
    FOREIGN KEY (SecurityAlias) REFERENCES data.SecurityMaster(SecurityAlias) ON DELETE CASCADE
);

CREATE UNIQUE INDEX IX_Prices_SecurityAlias_EffectiveDate ON data.Prices(SecurityAlias, EffectiveDate);
CREATE INDEX IX_Prices_EffectiveDate ON data.Prices(EffectiveDate);
```

**Scale Target:** ~1.26 million rows (500 stocks × 252 trading days × 10 years)

**CompanyBio Table:**

Caches company descriptions fetched from Wikipedia or financial data providers. One row per security, joined 1:1 on SecurityAlias. Populated on first stock lookup, served from Azure SQL on subsequent requests.

```sql
CREATE TABLE data.CompanyBio (
    SecurityAlias INT NOT NULL PRIMARY KEY,         -- FK to SecurityMaster (1:1)
    Description NVARCHAR(MAX) NOT NULL,             -- The company bio text
    Source NVARCHAR(50) NOT NULL,                   -- "wikipedia", "provider", etc.
    FetchedAt DATETIME2 DEFAULT GETUTCDATE(),       -- When originally fetched
    UpdatedAt DATETIME2 DEFAULT GETUTCDATE(),       -- Last update
    FOREIGN KEY (SecurityAlias) REFERENCES data.SecurityMaster(SecurityAlias) ON DELETE CASCADE
);
```

**PriceStaging Table (staging schema):**

Buffers incoming bulk data before merge to production. No foreign key constraints for maximum insert speed.

```sql
CREATE TABLE staging.PriceStaging (
    Id BIGINT IDENTITY(1,1) PRIMARY KEY,
    BatchId UNIQUEIDENTIFIER NOT NULL,              -- Groups records from same import
    Ticker NVARCHAR(20) NOT NULL,                   -- Raw ticker (looked up during merge)
    EffectiveDate DATE NOT NULL,
    Open DECIMAL(18,4) NOT NULL,
    High DECIMAL(18,4) NOT NULL,
    Low DECIMAL(18,4) NOT NULL,
    Close DECIMAL(18,4) NOT NULL,
    AdjustedClose DECIMAL(18,4) NULL,
    Volume BIGINT NULL,
    Status NVARCHAR(20) DEFAULT 'pending',          -- pending/processed/skipped/error
    ErrorMessage NVARCHAR(500) NULL,
    CreatedAt DATETIME2 DEFAULT GETUTCDATE(),
    ProcessedAt DATETIME2 NULL
);

CREATE INDEX IX_PriceStaging_Status_CreatedAt ON staging.PriceStaging(Status, CreatedAt);
CREATE INDEX IX_PriceStaging_BatchId ON staging.PriceStaging(BatchId);
CREATE INDEX IX_PriceStaging_Ticker_EffectiveDate ON staging.PriceStaging(Ticker, EffectiveDate);
```

**Staging Workflow:**
1. Bulk insert raw data to staging (fast, no FK checks)
2. Merge to production via `MergeToPricesAsync()`:
   - Lookup SecurityAlias for each ticker
   - Optionally create missing SecurityMaster entries
   - Skip duplicates (ticker+date already in production)
   - Update staging status (processed/skipped/error)
3. Cleanup old processed records via `CleanupProcessedAsync()`

**Repository Interfaces:**

| Interface | Implementation | Purpose |
|-----------|----------------|---------|
| `ISecurityMasterRepository` | `SqlSecurityMasterRepository` | Security CRUD, upsert, search |
| `IPriceRepository` | `SqlPriceRepository` | Price range queries, bulk insert |
| `IPriceStagingRepository` | `SqlPriceStagingRepository` | Staging bulk insert, merge to production |

**Key Methods:**
- `GetByTickerAsync(ticker)` - Lookup by ticker symbol
- `UpsertManyAsync(securities)` - Batch upsert for data loading (500-row batches, batch-fetch existing per chunk to avoid N+1)
- `GetActiveTickerAliasMapAsync()` - Projected query returning `Dictionary<string, int>` (ticker→alias, 2 columns only)
- `GetPricesAsync(alias, startDate, endDate)` - Date range query
- `BulkInsertAsync(prices)` - High-performance insert (1000-row batches, per-batch dedup to avoid massive IN clauses)
- `GetLatestPricesAsync(aliases)` - Batch latest price lookup
- `GetDistinctDatesAsync(startDate, endDate)` - Get all dates with price data (while-loop MIN() skip-scan on `IX_Prices_EffectiveDate`)
- `GetDateRangeAsync(alias)` - Min/max dates for a security (two TOP 1 index seeks)
- `GetCountForSecurityAsync(alias)` - Count with `AsNoTracking()` to avoid change tracker overhead
- `AnalyzeHolidaysAsync()` - Non-BD analysis using `GetDistinctDatesAsync` + single calendar query + binary search (5 queries total, was ~2,700)
- `ForwardFillHolidaysAsync(limit)` - Batch MERGE via raw SQL in chunks of 50 non-BD dates (~70 operations, was ~12,000)

**Files:**
- `Data/Entities/SecurityMasterEntity.cs` - Security master entity
- `Data/Entities/PriceEntity.cs` - Price entity
- `Data/Entities/IndexDefinitionEntity.cs` - Index definition entity (iShares)
- `Data/Entities/IndexConstituentEntity.cs` - Index constituent snapshot entity (iShares)
- `Data/Entities/SecurityIdentifierEntity.cs` - Security identifier mapping (CUSIP, ISIN, SEDOL)
- `Data/Entities/SecurityIdentifierHistEntity.cs` - Security identifier history (SCD Type 2)
- `Services/ISecurityMasterRepository.cs` - Interface + DTOs
- `Services/IPriceRepository.cs` - Interface + DTOs
- `Data/SqlSecurityMasterRepository.cs` - SQL implementation
- `Data/SqlPriceRepository.cs` - SQL implementation
- `scripts/001_CreateDataSchema.sql` - Schema creation script
- `scripts/002_AddSecurityMasterAndPrices.sql` - Migration script
- `Migrations/20260223034707_MapIndexAttributionTables.cs` - Baseline migration (empty Up/Down, tables created by Python pipeline)
- `Migrations/20260223034707_MapIndexAttributionTables.Designer.cs` - Migration snapshot metadata
- `Migrations/20260223232008_CreateIndexTablesIfNotExist.cs` - Idempotent migration: creates IndexDefinition, IndexConstituent, SecurityIdentifier, SecurityIdentifierHist tables with `IF NOT EXISTS` guards (safe for both local and production)
- `Migrations/20260224051341_CreateCoverageTablesIfNotExist.cs` - Idempotent migration: creates SecurityPriceCoverage and SecurityPriceCoverageByYear pre-aggregation tables with `IF NOT EXISTS` guards (safe for both local and production)
- `Data/Entities/SecurityPriceCoverageEntity.cs` - Per-security coverage metadata entity
- `Data/Entities/SecurityPriceCoverageByYearEntity.cs` - Per-security-per-year coverage metadata entity
- `Data/StockAnalyzerDbContext.cs` - DbContext with Fluent API configuration for all entities

**DbContext Configuration:**
- IndexDefinitionEntity: Composite index on `(IndexCode)` unique
- IndexConstituentEntity: `Id` is `long` (maps to `bigint` PK); composite unique index on `(IndexId, SecurityAlias, EffectiveDate, SourceId)` prevents duplicates
- SecurityIdentifierEntity: Composite PK on `(SecurityAlias, IdentifierType)` enables 1:N identifiers per security
- SecurityIdentifierHistEntity: `Id` is `long` (maps to `bigint` PK); tracks historical identifier changes using SCD Type 2 with effective date ranges
- SecurityPriceCoverageEntity: PK on `SecurityAlias` (FK to SecurityMaster); tracks overall price coverage including computed `GapDays` column (ISNULL(ExpectedCount, 0) - PriceCount), eliminates expensive full-table scans on Prices table
- SecurityPriceCoverageByYearEntity: Composite PK on `(SecurityAlias, Year)`; tracks per-year coverage metadata for CoverageSummary pre-aggregation (replaces costly GROUP BY YEAR aggregations on 43M+ row Prices table)
- Schema validation integration test (`SchemaValidationTests.cs`) compares all EF Core entity CLR types against actual SQL Server column types via `INFORMATION_SCHEMA.COLUMNS` to catch int/bigint drift

**Security Price Coverage Tables (Pre-Aggregation):**

Both tables use idempotent migration (IF NOT EXISTS guards) for safe application to local and production databases.

| Table | Purpose | Key Columns | Relationships |
|-------|---------|-------------|----------------|
| `data.SecurityPriceCoverage` | Per-security summary of price data coverage | SecurityAlias (PK), PriceCount, FirstDate, LastDate, ExpectedCount (from BusinessCalendar), GapDays (computed persisted) | 1:1 FK to SecurityMaster, OnDelete Cascade |
| `data.SecurityPriceCoverageByYear` | Per-security-per-year coverage breakdown | SecurityAlias + Year (composite PK), PriceCount, LastUpdatedAt | N:1 FK to SecurityMaster, OnDelete Cascade |

**Coverage Update Strategy:**
- Tables updated via delta arithmetic during price loads (PriceRefreshService)
- GapDays computed column: ISNULL(ExpectedCount, 0) - PriceCount (positive = gaps, zero = fully covered)
- Both tables provide instant analytics without scanning 43M+ row Prices table
- Critical for Azure SQL Basic tier (5 DTU limit) where full-table scans cause timeouts

**CoverageDelta Model and Computation:**

The `CoverageDelta` record (`SqlPriceRepository.cs`) represents the per-security price delta inserted in a single batch:

```csharp
internal record CoverageDelta(
    int SecurityAlias,
    int InsertedCount,          // Number of prices inserted for this security
    DateTime MinDate,           // Earliest date (date only, no time)
    DateTime MaxDate,           // Latest date (date only, no time)
    Dictionary<int, int> YearCounts);  // Prices per calendar year
```

The static method `ComputeCoverageDeltas(List<PriceCreateDto> newPrices)` performs in-memory delta arithmetic on newly inserted prices:

- **Grouping:** Partitions input prices by `SecurityAlias`
- **Count:** Tallies total inserted prices per security
- **Date Range:** Computes MIN/MAX dates from `EffectiveDate.Date` (strips time component)
- **Year Partition:** Aggregates price counts by calendar year via `EffectiveDate.Year`
- **Zero DTU Cost:** Pure C# computation — no database access

**Usage:**

During `BulkInsertAsync` batch processing, after each 1000-row batch successfully completes `SaveChangesAsync()`:

```csharp
var deltas = ComputeCoverageDeltas(newPrices);
await UpdateCoverageAsync(deltas);
```

**UpdateCoverageAsync Implementation:**

The private async method `UpdateCoverageAsync(List<CoverageDelta> deltas)` in `SqlPriceRepository.cs` executes atomic MERGE statements against both coverage tables:

**SecurityPriceCoverage MERGE:**
- **WHEN MATCHED:** Increments `PriceCount` by `InsertedCount`; widens `FirstDate`/`LastDate` to include new boundaries (MIN/MAX); recomputes `ExpectedCount` via correlated subquery against `data.BusinessCalendar` (SourceId = 1, IsBusinessDay = 1, date range = widened boundaries)
- **WHEN NOT MATCHED:** Inserts new row with SecurityAlias, InsertedCount → PriceCount, MinDate → FirstDate, MaxDate → LastDate, computed ExpectedCount, LastUpdatedAt = GETUTCDATE()
- **HOLDLOCK:** Applied to prevent race conditions on concurrent batch processing

**SecurityPriceCoverageByYear MERGE (per year in YearCounts):**
- **WHEN MATCHED:** Increments `PriceCount` by count for that year
- **WHEN NOT MATCHED:** Inserts new row with SecurityAlias, Year, count → PriceCount, LastUpdatedAt = GETUTCDATE()
- **HOLDLOCK:** Applied to prevent race conditions

**Parameterization & Error Handling:**
- All SQL values passed as parameters (@SecurityAlias, @InsertedCount, @MinDate, @MaxDate, @Year, @Count) — no string concatenation
- Per-delta try/catch: logs `LogWarning` on failure, continues to next delta
- Coverage updates are eventually consistent per design — a failure does NOT block price insertion

**Test Coverage:**

Unit tests in `SqlPriceRepositoryCoverageTests.cs` verify delta computation:
- Empty input → empty list
- Single price → single delta with correct boundaries and year count
- Multiple prices per security → correct aggregated InsertedCount and date range
- Multiple securities → separate deltas with independent counts and date ranges
- Dates spanning calendar years → correct YearCounts partition across years
- Min/MaxDate stripped to `.Date` (no time component)

Integration tests in `CoverageIntegrationTests.cs` verify MERGE behavior against SQL Express:
- **AC1.5:** First-ever price insert creates new SecurityPriceCoverage row with correct PriceCount, FirstDate = min date, LastDate = max date
- **AC1.2:** Inserting prices before existing FirstDate widens FirstDate; inserting after existing LastDate widens LastDate; opposite boundary unchanged
- **AC1.3:** ExpectedCount matches actual business day count from BusinessCalendar (SourceId = 1) between FirstDate and LastDate
- **AC1.1 (incremental):** Multiple security insertions in one batch update each security's coverage independently
- **AC1.6 (multi-year):** Prices spanning multiple calendar years increment each year's coverage in SecurityPriceCoverageByYear

#### EODHD Integration (Historical Price Data)

**Service:** `EodhdService.cs`

Primary data source for historical EOD prices. Uses [EODHD API](https://eodhd.com/financial-apis/) which provides:
- Bulk API: Download entire exchange in one request (100 API calls)
- Historical API: Per-ticker date range queries
- Data quality: Institutional-grade OHLCV with adjusted close

**Configuration:**
```json
{
  "Eodhd": {
    "ApiKey": "your-api-key"
  }
}
```

**Key Methods:**
| Method | Description |
|--------|-------------|
| `GetHistoricalDataAsync(ticker, start, end)` | Get date range for single ticker |
| `GetBulkEodDataAsync(date, exchange)` | Get all tickers for a date (entire exchange) |
| `GetBulkEodDataForTickersAsync(tickers, date)` | Get specific tickers for a date |

**PriceRefreshService (Background Service):**

Maintains the historical price database with automatic daily updates.

| Behavior | Description |
|----------|-------------|
| Startup | Checks `MAX(EffectiveDate)` and backfills missing days |
| Daily Schedule | 2:30 AM UTC - fetches previous trading day |
| Weekend Skip | No refresh on Saturday/Sunday |
| Rate Limiting | 2-second delay between bulk requests |

**Admin Endpoints:**
| Endpoint | Description |
|----------|-------------|
| `GET /api/admin/prices/status` | Database status: counts, latest date, EODHD status |
| `GET /api/admin/prices/coverage-dates` | Get distinct dates with price data (query: `startDate`, `endDate`) |
| `GET /api/admin/prices/gaps` | Find tracked securities with missing price data (query: `market`, `limit`) |
| `GET /api/admin/prices/gaps/{securityAlias}` | Get specific missing dates for a security (query: `limit`) |
| `POST /api/admin/prices/sync-securities` | Sync SecurityMaster from Symbols table |
| `POST /api/admin/prices/refresh-date` | Fetch prices for specific date (body: `{Date: "yyyy-MM-dd"}`) |
| `POST /api/admin/prices/load-tickers` | Load historical prices for specific tickers (body: `{Tickers[], StartDate, EndDate}`) |
| `POST /api/admin/prices/backfill` | Parallel backfill for multiple tickers (body: `{Tickers[], StartDate, EndDate}`, 10 concurrent) |
| `POST /api/admin/prices/backfill-coverage` | Backfill SecurityPriceCoverage and SecurityPriceCoverageByYear from existing Prices data (one-time bootstrap, 600s timeout, MERGE idempotent) |
| `POST /api/admin/prices/bulk-load` | Start bulk historical load (body: `{StartDate, EndDate}`) |
| `POST /api/admin/securities/calculate-importance` | Calculate importance scores for all active securities |
| `POST /api/admin/securities/promote-untracked` | Promote untracked securities to tracked (query: `count`, default 500, max 500) |
| `POST /api/admin/prices/mark-eodhd-complete/{alias}` | Mark security as having all EODHD data loaded (unfillable gaps) |
| `POST /api/admin/prices/bulk-mark-eodhd-complete` | Bulk mark securities with sufficient price data as EODHD complete (query: `minPriceCount` default 50, `dryRun`). Uses CROSS APPLY for per-security indexed counts instead of GROUP BY JOIN. |
| `POST /api/admin/securities/reset-unavailable` | Reset IsEodhdUnavailable flag (query: `days`, `all`) |
| `GET /api/admin/dashboard/stats` | Consolidated dashboard stats: universe, prices, tiers, decade/year coverage |
| `GET /api/admin/dashboard/heatmap` | Bivariate heatmap data: Year x ImportanceScore with tracked/untracked split |
| `POST /api/admin/dashboard/refresh-summary` | Refresh the CoverageSummary pre-aggregation table |

**Dashboard Stats (`/api/admin/dashboard/stats`):**
- Consolidated endpoint returning all dashboard metrics
- Universe counts from SecurityMaster (small table, instant)
- **totalRecords**: Real-time row count from `sys.dm_db_partition_stats` (SQL Server metadata, zero DTU, always current — replaces stale CoverageSummary sum)
- distinctSecurities derived from `data.CoverageSummary` (pre-aggregated, instant)
- Latest price date via lightweight `TOP 1 ORDER BY DESC` on Prices (index seek, near-instant)
- `summaryLastRefreshed`: `MAX(LastUpdatedAt)` from CoverageSummary — enables freshness indicators in client UI
- ImportanceScore tier distribution derived from CoverageSummary
- Decade/year coverage also from CoverageSummary
- **No full-table scans on Prices** — critical fix for Azure SQL Basic (5 DTU) with 7M+ rows
- **Caching:** `IMemoryCache` with 10-minute TTL (key: `dashboard:stats`); cache invalidated by `POST /api/admin/prices/load-tickers` on successful insert
- Used by EODHD Loader's Data Load Monitor UI (3-tier metric card layout)
- Returns: universe counts (total/tracked/untracked/unavailable), price record stats (total/distinct/oldest/latest), ImportanceScore tier distribution with completion status, coverage by decade, coverage by year
- **Performance:** Responds in <1s even under concurrent crawler load (previously timed out at 30s+)
- **Response structure:** `{ success, timestamp, summaryLastRefreshed, universe, prices, importanceTiers[], coverageByDecade[], coverageByYear[] }`

**Heatmap Data (`/api/admin/dashboard/heatmap`):**
- Reads from pre-aggregated `data.CoverageSummary` table (instant response, even on Azure SQL Basic tier)
- **Caching:** `IMemoryCache` with 30-minute TTL (key: `dashboard:heatmap`)
- If summary table is empty, returns `stale: true` with guidance to call `refresh-summary`
- Used by EODHD Loader's bivariate coverage heatmap (SkiaSharp custom control)
- Response: `{ success, cells[{ year, score, trackedRecords, untrackedRecords, trackedSecurities, untrackedSecurities }], metadata{ minYear, maxYear, totalCells, maxTrackedRecords, maxUntrackedRecords } }`
- ~230 cells (47 years × ~5 populated scores)

**Refresh Summary (`/api/admin/dashboard/refresh-summary`):**
- Aggregates pre-computed coverage metadata from `data.SecurityPriceCoverageByYear` × `data.SecurityMaster` and writes results to `data.CoverageSummary`
- **30-second timeout** — fast operation that queries ~60K pre-aggregated coverage rows instead of 43M+ Prices rows
- SQL: `GROUP BY cy.[Year], sm.ImportanceScore` with tracked/untracked splits via SUM of PriceCount, SUM of 1 for securities count, correlated BusinessCalendar subquery for TradingDays (expected business days per year from SourceId=1, decoupled from data completeness)
- Invalidates both `dashboard:heatmap` and `dashboard:stats` cache keys on completion
- Returns: `{ success, message, cellCount }`
- **When to call:** After deployment, after running calculate-importance, after crawl sessions

**CoverageSummary Table (`data.CoverageSummary`):**
- Pre-aggregated Year × ImportanceScore grid (~500 rows max: ~50 years × 10 scores)
- Columns: Id (PK), Year, ImportanceScore, TrackedRecords, UntrackedRecords, TrackedSecurities, UntrackedSecurities, TradingDays, LastUpdatedAt
- Unique index on `(Year, ImportanceScore)` — each cell has exactly one row
- **Purpose:** Avoids expensive full-table scans on the 7M+ row Prices table; critical for Azure SQL Basic tier (5 DTU)
- Populated by `POST /api/admin/dashboard/refresh-summary`; consumed by heatmap and stats endpoints
- Also consumed by: `/api/admin/data/prices/summary`, `/api/admin/data/prices/monitor`, `GetTotalCountAsync()`, `AnalyzeHolidaysAsync()`

**SecurityPriceCoverage Table (`data.SecurityPriceCoverage`):**
- Per-security price coverage metadata. One row per security.
- Tracks actual price count, date range, expected count, and gap days to replace expensive full-table scans on the Prices table.
- Columns: SecurityAlias (PK, FK to SecurityMaster), PriceCount, FirstDate (date), LastDate (date), ExpectedCount, GapDays (computed persisted: `ISNULL(ExpectedCount, 0) - PriceCount`), LastUpdatedAt
- Updated incrementally during price loads via delta arithmetic (no Prices table scan)
- **Purpose:** Replaces the 4-CTE gap query that scanned the entire Prices table; enables gap detection from a ~30K row table instead of 43M+ rows

**SecurityPriceCoverageByYear Table (`data.SecurityPriceCoverageByYear`):**
- Per-security-per-year price coverage metadata. One row per security per year.
- Composite PK: (SecurityAlias, Year), FK to SecurityMaster on SecurityAlias
- Columns: SecurityAlias, Year, PriceCount, LastUpdatedAt
- Updated incrementally during price loads via delta arithmetic (no Prices table scan)
- **Purpose:** Supports CoverageSummary aggregation from ~60K rows instead of scanning 43M+ Prices rows; replaces the expensive refresh-summary GROUP BY query

**Backfill Coverage (`/api/admin/prices/backfill-coverage`):**
- **Purpose:** One-time bootstrap operation to populate SecurityPriceCoverage and SecurityPriceCoverageByYear tables from existing Prices data
- **When to use:** After initial database setup or migration from legacy data; after manual Prices table edits
- **Operational note:** Run during off-hours or when EODHD crawler is idle. While MERGE statements are idempotent, concurrent runs with `BulkInsertAsync` (price delta updates) could transiently corrupt coverage counts. A subsequent backfill run will correct any inconsistencies.
- **Timeout:** 600 seconds (10 minutes) — intentionally generous for full Prices table scan on large databases; critical for Azure SQL Basic (5 DTU) which may exhaust DTU during intensive full-table operations
- **Idempotency:** Uses MERGE (not INSERT), safe to re-run multiple times
- **Semaphore:** Guarded by `SemaphoreSlim(1,1)` — prevents concurrent backfill runs; returns HTTP 409 Conflict if already running
- **SQL Operations:**
  1. **SecurityPriceCoverage MERGE:** Groups all Prices rows by SecurityAlias with `WITH (NOLOCK)`, computes COUNT(*), MIN(EffectiveDate), MAX(EffectiveDate). WHEN MATCHED: updates PriceCount, FirstDate, LastDate, and recomputes ExpectedCount from BusinessCalendar. WHEN NOT MATCHED: inserts new row.
  2. **SecurityPriceCoverageByYear MERGE:** Groups all Prices rows by SecurityAlias + YEAR(EffectiveDate) with `WITH (NOLOCK)`, computes COUNT(*) per year. WHEN MATCHED: updates PriceCount. WHEN NOT MATCHED: inserts new row.
- **Response:** HTTP 200 with `{ success: true, message, coverageRowsUpdated, coverageByYearRowsUpdated }`
- **Failure:** HTTP 400/500 with error details; semaphore released in finally block
- **Verification:** After backfill, coverage row counts should match number of distinct securities in Prices table; PriceCount should exactly match COUNT(*) per security (no deltas)

**DTU-Optimized Query Patterns (Azure SQL Basic, 5 DTU):**
- **No full-table scans on Prices** — the 7M+ row table will exhaust 5 DTU / 60 worker limits
- **Total record count:** Use `sys.dm_db_partition_stats` for real-time row count (zero DTU, instant, always current). Never use CoverageSummary for the headline totalRecords number — CoverageSummary goes stale between refreshes.
- **Aggregate breakdowns:** Use `CoverageSummary` pre-aggregated table for year/score breakdowns (SUM of TrackedRecords + UntrackedRecords)
- **Date range:** Use `TOP 1 ORDER BY EffectiveDate ASC/DESC` (index seek on `IX_Prices_EffectiveDate`)
- **Distinct securities:** Use `SELECT COUNT(*) FROM SecurityMaster WHERE EXISTS (SELECT 1 FROM Prices WHERE ...)` (index seek per small-table row)
- **Per-security counts:** Use `CROSS APPLY (SELECT COUNT(*) FROM Prices WHERE SecurityAlias = sm.SecurityAlias)` (index seek via `IX_Prices_SecurityAlias_EffectiveDate`)
- **Decade/year breakdowns:** Aggregate CoverageSummary in C#, not SQL
- **Recent dates:** `SELECT DISTINCT TOP 10 EffectiveDate ORDER BY DESC` (index seek, not GROUP BY)
- **Distinct dates (coverage):** While-loop with `MIN()` skip-scan on `IX_Prices_EffectiveDate` (~500 seeks for 2 years vs full index scan of 5M+ rows). Uses table variable + iterative MIN() because SQL Server recursive CTEs prohibit TOP, CROSS APPLY, and aggregate functions in the recursive member.
- **Holiday analysis:** Load calendar + existing dates in 5 queries, binary search for prior-BD mapping in C# (was ~2,700 N+1 queries)
- **Forward-fill:** Raw SQL `MERGE` in batches of 50 non-BD dates with temp table mapping (~70 batch ops, was ~12,000 N+1 queries)
- **Cache coalescing:** `ConcurrentDictionary<string, Task<T>>` with `GetOrAdd()` to prevent concurrent cache misses from triggering duplicate DB queries
- **HttpClient timeouts:** All external API services use explicit timeouts (15s for interactive, 10s for news, 30s for bulk) — never rely on the default 100s timeout
- **DB connection pool warmup:** `DbWarmupService` (IHostedService) runs `SELECT 1` on startup + `Min Pool Size=2` in connection strings to eliminate cold-start penalty
- **Combined endpoints:** `/api/stock/{ticker}/chart-data` merges history + analysis into a single request, halving DB queries and HTTP round-trips for the critical chart-loading path
- **Bulk insert dedup:** Per-1000-batch dedup queries (small IN clauses) instead of single upfront dedup (massive IN with all aliases × dates)
- **AsNoTracking:** Applied to all read-only queries (`GetCountForSecurityAsync`, forward-fill source reads, etc.)
- **Batch-fetch upsert:** `UpsertManyAsync` in SecurityMaster/Symbol repos: batch-fetch existing entities per 500 chunk via `WHERE IN` + `ToDictionary`, then lookup in C# (was 55K/30K individual queries → 110/60 batch queries)
- **Search bounds:** `SearchAsync` bounded server-side with `.Take(limit * 5)` before `ToListAsync()` to prevent unbounded fetch of 55K+ entities
- **Projected queries:** `GetActiveTickerAliasMapAsync()` returns `Dictionary<string, int>` via 2-column projection instead of materializing full 55K entities
- **Concurrency guards:** Static `SemaphoreSlim` on heavy endpoints (refresh-summary, bulk-load, calculate-importance) returns 409 Conflict if busy; auto-track limited to 3 concurrent
- **No double-scan exports:** Data export uses `pageSize + 1` fetch to detect `hasMore` (eliminates separate COUNT on Prices)
- **Chunked writes:** calculate-importance processes 1000-row pages with `SaveChanges + ChangeTracker.Clear()` per page; populate-us-calendar batches 2000 rows
- **NOLOCK on seed-tracked:** Read-only joins use `WITH (NOLOCK)` to prevent blocking
- **Backfill concurrency cap:** `BackfillTickersParallelAsync` default 10→3, max 50→10 to protect DTU budget
- **Auto-purge removed from crawler START** — bulk-mark was causing DTU exhaustion on every crawl session; now manual-only via PURGE button

**Split-Adjusted Price Data:**
- All OHLC data served by `GetHistoricalDataAsync()` is automatically adjusted for stock splits
- **Mechanism:** `AdjustedClose` (stored by EODHD) provides the split factor: `ratio = AdjustedClose / Close`
- The ratio is applied to Open, High, Low, Close before data is cached or returned to any consumer
- This ensures charts, return calculations, and all technical indicators (SMA, RSI, MACD, Bollinger, Stochastic) are correct for split-affected stocks
- When `AdjustedClose` is null or matches `Close` (ratio ≈ 1.0), no adjustment is applied
- **Provider AdjustedClose support:** EODHD (yes), FMP (yes), TwelveData (no), Yahoo Finance (no)
- **Location:** `AggregatedStockDataService.AdjustForSplits()` — single-point adjustment before caching

**Reset Unavailable (`/api/admin/securities/reset-unavailable`):**
- Resets `IsEodhdUnavailable = false` for securities incorrectly marked unavailable
- Default: Reset securities marked in last 7 days (by `UpdatedAt`); `?days=N` to customize
- `?all`: Reset ALL unavailable active securities
- Returns list of reset securities with ticker and exchange

**Gap Detection (`/api/admin/prices/gaps`):**
- Returns only tracked securities (`IsTracked = 1`) with price gaps
- Response includes `isTracked` flag and `importanceScore` per security, plus summary counts
- Used by EODHD Loader crawler for single-loop gap filling with batch promotion
- **Ordering:** Priority → MissingDays DESC → TickerSymbol
- **Query Architecture (Phase 3 — DTU optimized):**
  - Reads from `SecurityPriceCoverage` table (~30K rows, indexed) instead of scanning 43M+ row Prices table
  - Joins: `SecurityMaster LEFT JOIN SecurityPriceCoverage LEFT JOIN TrackedSecurities`
  - Includes securities with `GapDays > 0` (have prices but missing days) OR `SecurityAlias IS NULL` (zero prices loaded)
  - Excludes fully covered securities (`GapDays = 0`)
  - For securities with no coverage row: `FirstDate = NOW() - 2 years`, `LastDate = TODAY`, `ExpectedCount = business days over 2 years`, `GapDays = ExpectedCount`
  - Command timeout: 30 seconds (reduced from 300 seconds)
- **Summary stats:** Now pulls `SecuritiesWithData` (count from SecurityPriceCoverage) and `TotalPriceRecords` (sum of PriceCount from coverage table)

**Promote Untracked (`/api/admin/securities/promote-untracked`):**
- Selects top N untracked securities ordered by `ImportanceScore DESC`, then by `TickerSymbol`
- For each: inserts into `data.TrackedSecurities` (source: `auto-promote`) and sets `IsTracked = 1`
- Uses temp table (`#PromoteAliases`) for parameterized SQL (avoids CA2100 SQL injection warnings)
- Returns: `{ success, promoted, tickers[], message }`
- Count range: 1-500 (default: 500, clamped via `Math.Clamp`)
- Used by EODHD Loader crawler when tracked gap queue is exhausted — promotes a batch, then re-queries gaps

**Auto-Track on Stock View:**
- `GET /api/stock/{ticker}` fires a background task (`Task.Run`) to auto-track the viewed security
- If the ticker exists in SecurityMaster with `IsTracked = 0`: sets `IsTracked = 1`, inserts into `TrackedSecurities` (source: `user-search`, priority: 3)
- Non-blocking: runs as fire-and-forget so it doesn't slow down the stock data response
- Rationale: user searches are a proxy for interest in a stock; auto-tracking ensures the crawler will fill price data for stocks users care about

**Gap Detail (`/api/admin/prices/gaps/{securityAlias}`):**
- Returns specific missing dates for a security
- **Date capping:** `lastDate` is capped at `DateTime.Today` to prevent requesting future dates from EODHD

**Importance Score Calculation (`/api/admin/securities/calculate-importance`):**
- Calculates importance scores (1-10, 10=most important) for all active securities
- Used to prioritize gap-filling order and as Y-axis for the coverage heatmap
- Primary signal: **index membership** from IndexConstituent data (which indices, how many)
- Secondary signals: security type, exchange quality
- Pre-loads all index membership via efficient CTE+JOIN query with `NOLOCK` (DTU-safe)
- Falls back gracefully when IndexConstituent table is empty or missing (all securities get attribute-based scores only)
- Resilient to missing index tables: catches `SqlException` for "Invalid object name" and falls back to heuristic-only scoring
- Scoring algorithm (base score: 1):
  - **Index membership (primary, 0-6 pts):** Tier 1 x2+ → +6, Tier 1 x1 → +5, Tier 2 x2+ → +4, Tier 2 x1 → +3, 3+ other → +2, 1-2 other → +1
  - **Index tiers:** Tier 1 = SP500, R1000, R2000, R3000, MSCI_ACWI, MSCI_EAFE, MSCI_EM; Tier 2 = IJH, IJR, OEF, ITOT, IDEV, IEMG, IEFA, IXUS
  - **Breadth bonus (0-1 pts):** In 8+ distinct indices → +1
  - **Security Type (0-1 pts):** Common Stock → +1
  - **Exchange (0-1 pts):** NYSE or NASDAQ → +1
  - **Penalties:** OTC/Pink/Grey exchange -2, Preferred/Warrant/Right type -2, OTC type -2, Warrant/Right/Unit name -2, Liquidating/Bankrupt name -3
- Response includes `indexDataAvailable` flag and `securitiesWithIndexMembership` count for diagnostics
- Run on-demand after adding new securities or loading index constituents; scores persisted in SecurityMaster.ImportanceScore
- **Connection pattern:** Uses `context.Database.GetDbConnection()` (no `using`) + `context.Database.OpenConnectionAsync()` — EF Core manages the connection lifetime; wrapping in `using` disposes prematurely and causes 500 errors. Same pattern used by `bulk-mark-eodhd-complete`.

**Bulk Load Flow:**
1. Call `/api/admin/prices/sync-securities` to populate SecurityMaster
2. Call `/api/admin/prices/bulk-load` with date range
3. Service runs in background, logs progress every 100 tickers
4. Check `/api/admin/prices/status` for completion

**EODHD Loader (WPF Desktop Client):**

Location: `projects/eodhd-loader/src/EodhdLoader/`

WPF desktop application (.NET 8, `net8.0-windows10.0.19041`) for managing price data loading. Connects to the Stock Analyzer API.

| Tab | View | ViewModel | Purpose |
|-----|------|-----------|---------|
| Boris | `BorisView` | `BorisViewModel` | Single-ticker price loading |
| Crawler | `CrawlerView` | `CrawlerViewModel` | Autonomous gap-filling agent (Data Load Monitor) — 3-tier metric dashboard |
| Bulk Fill | `BulkFillView` | `BulkFillViewModel` | Batch historical data loading |
| Index Manager | `IndexManagerView` | `IndexManagerViewModel` | Index composition management — ETF dropdown shows "TICKER - Name" with acronym handling (MSCI, ESG, EAFE, etc.) |
| Dashboard | `DashboardView` | `DashboardViewModel` | Coverage stats and charts |

**Crawler — 3-Tier Metric Dashboard:**
- **Tier 1 (Hero Card):** DATA COVERAGE — full-width progress bar with `CompletionPercent`, gap count (`SecuritiesWithGaps`) with denominator (`TotalSecurities`), session delta indicator (green ▼ when gaps decreasing, red ▲ when increasing)
- **Tier 2 (Reference Cards):** TRACKED UNIVERSE (tracked/untracked/unavailable from SecurityMaster), PRICE RECORDS (total from CoverageSummary with freshness indicator), DATA SPAN (date range)
- **Tier 3 (Session Cards):** TICKERS processed (with rate/hr), RECORDS loaded (with rate/hr); show "last session" counts when idle instead of "0"
- **Key bug fix (v2.35):** Card 3 was bound to `TrackedDisplay` (`Universe.Tracked` = tracked universe size) but labeled "WITH GAPS". Actual gap count (`SecuritiesWithGaps` from gaps endpoint) was only in the status bar. This caused the "Tracked keeps going up" confusion.
- **Freshness:** API returns `summaryLastRefreshed` timestamp; client shows relative time ("refreshed 2h ago") on Price Records card
- **Auto-refresh:** Client triggers `POST /api/admin/dashboard/refresh-summary` on crawler stop (fire-and-forget, 2-5 min server-side)
- **Cache invalidation:** `POST /api/admin/prices/load-tickers` invalidates `dashboard:stats` cache on successful insert, so mid-session stats refresh is possible

**Crawler — Full-Range Loading Strategy:**
- Each security loads its entire available history in a single EODHD API call (`POST /api/admin/prices/load-tickers` with date range 1980-01-01 to today)
- Server-side `BulkInsertAsync` deduplicates against existing `(SecurityAlias, EffectiveDate)` pairs before insert
- ~15-20 seconds per security instead of processing individual dates (~25+ minutes per security with per-date approach)
- Zero-result securities (no EODHD data available) are marked as `IsEodhdUnavailable` and added to a skip set
- "Already complete" securities (0 new inserts, all EODHD data already loaded) are marked as `IsEodhdComplete` so the gap query skips them permanently, preventing wasted API calls on unfillable gaps

**Crawler — Auto-Promotion Flow:**
1. Query tracked securities with gaps (gap endpoint, limit 20)
2. For each: load full history via single API call
3. When tracked gap queue exhausted → auto-promote 500 untracked securities (highest ImportanceScore first)
4. Re-query gaps for newly promoted securities, continue crawling
5. When nothing left to promote → "All securities processed!" (truly done)
- Auto-promotion triggers at 3 code points: startup (no initial gaps), mid-crawl (queue empty), and skip-set exhaustion (all remaining in skip set)

**Crawler — Constituent Pre-Step Logging:**
- `CrawlerViewModel.CheckAndLoadConstituentsAsync` subscribes to `ISharesConstituentService.LogMessage` during constituent refresh so detailed status (download progress, "no equity holdings found", format detection, etc.) appears in the activity log instead of silent 0/0 summaries
- Event wired with try/finally to ensure unsubscription after the loop completes

**Heatmap V2 (SkiaSharp Custom Control):**

`HeatmapV2Control.cs` — bivariate Year × ImportanceScore coverage visualization.

| Feature | Implementation |
|---------|---------------|
| Rendering | Radial gradient blobs with `SKBlendMode.Screen` on off-screen surface |
| Blob radius | `Max(cellWidth, cellHeight) * 1.6` for dense overlap |
| Blur | Gaussian blur sigma `Max(cellWidth, cellHeight) * 0.5` |
| Edge treatment | Phantom blobs mirrored 2-deep at grid boundaries (edges + corners) |
| Color | Green `#00FF88` scaled by intensity (0.5 × tracked + 0.5 × untracked) |
| Active cell | Stone-drop ripple effect: 3 expanding rings from epicenter to grid corners |
| Ripple timing | `RipplePeriod = 6π` (~4.2s cycle), quadratic alpha fade, thinning stroke |
| Hover | Semi-transparent white overlay with tooltip (tracked/untracked counts) |
| Refresh | 30fps `DispatcherTimer` for animation; data refreshed from API after each security load |
| Bitmap scaling | `BitmapScalingMode.NearestNeighbor` prevents WPF bilinear filtering from blending adjacent cell colors |
| Cell padding | 2px gap between cells for visible boundaries between data-rich and empty cells |
| Hit-test DPI | Cached `_lastSurfaceWidth`/`_lastSurfaceHeight` from `OnPaintSurface` — HitTest uses cached dims instead of re-deriving via `PresentationSource` DPI to avoid mismatch |

**iShares Constituent Loader (Phase 1 - Core Service):**

`IISharesConstituentService` and `ISharesConstituentService` in EODHD Loader handle downloading and ingesting iShares ETF holdings data. Part of index attribution pipeline for tracking ETF composition changes.

**Implementation Details:**
- Downloads iShares holdings JSON from `https://www.ishares.com/us/products/{product_id}/{slug}/1467271812596.ajax` with user-agent and 60s timeout
- Auto-detects three iShares JSON formats: Format A (17 cols, IVV-style), Format B (19 cols, IJK-style), Format C (18-19 cols, active/thematic ETFs like IDEF, ICLN where col[5] is an object shifting subsequent column indices +1)
- Strips UTF-8 BOM prefix before JSON parsing (AC1.2)
- Filters non-equity holdings (Cash, Futures, Money Market) — keeps equities only (AC2.3)
- 3-level security matching: (1) ticker lookup, (2) CUSIP lookup, (3) ISIN lookup (AC3.2)
- Creates new securities in SecurityMaster if no match found (AC3.1)
- Upserts SecurityIdentifier with SCD Type 2 history — changed values snapshot old record with date range (AC3.3)
- Idempotent constituent insertion via composite unique constraint `(IndexId, SecurityAlias, EffectiveDate, SourceId)` (AC3.5)
- Error isolation per holding — one failure doesn't abort ETF (AC3.6); failed entities detached from change tracker to prevent poison cascade
- Rate limiting: 2s gap between ETF downloads (AC6.1, constant `RequestDelayMs`)
- iShares Source ID: 10

**Configuration:**
- ETF tickers and metadata loaded from bundled `Resources/ishares_etf_configs.json` at startup
- Keys: product_id, slug, index_code per ticker

**Progress Events:**
- `LogMessage` event for status/error logging
- `ProgressUpdated` event for UI progress bar (ETF counter, holdings processed)

#### Infrastructure as Code

**Location:** `infrastructure/azure/`

| File | Purpose |
|------|---------|
| `main.bicep` | Azure Bicep template (App Service + Key Vault) |
| `parameters.json` | Environment-specific parameters |
| `deploy.ps1` | PowerShell deployment script |

**Resources Provisioned:**
- App Service Plan (B1 Linux)
- App Service (Docker container)
- Azure Container Registry (Basic tier)
- Azure SQL Server (Basic tier) - **Note: Database NOT managed by Bicep**
- Azure Key Vault (secrets management)

**Database Management:**
The production database (`stockanalyzer-db`) is **NOT created or managed by Bicep**. It was created via BACPAC import containing 3.5M+ pre-loaded historical price records. The Bicep template only references the database name in connection strings. This prevents accidental data loss from infrastructure deployments.

**IMPORTANT:** Never add a SQL Database resource to main.bicep - it would create an empty database and overwrite the connection string, breaking the application.

#### CI/CD Pipeline

**Workflow:** `.github/workflows/azure-deploy.yml`

**Triggers:**
- Manual only via `workflow_dispatch` (production deploys require confirmation)

**Jobs:**
1. `preflight` - Validate confirmation, test Azure/ACR credentials
2. `build-and-test` - Build .NET solution, run tests
3. `build-container` - Build Docker image, push to ACR with `prod-{run_number}` tag
4. `deploy` - Update App Service container, restart, health check, warm caches, smoke test

**GitHub Secrets Required:**
| Secret | Purpose |
|--------|---------|
| `AZURE_CREDENTIALS` | Service principal JSON for Azure CLI |
| `ACR_PASSWORD` | Azure Container Registry admin password |

**Manual App Service Restart:**
```bash
az webapp restart -g rg-stockanalyzer-prod -n app-stockanalyzer-prod
```

**Rollback to Previous Image:**
```bash
az webapp config container set \
  --name app-stockanalyzer-prod \
  --resource-group rg-stockanalyzer-prod \
  --docker-custom-image-name acrstockanalyzerer34ug.azurecr.io/stockanalyzer:prod-{PREVIOUS_RUN_NUMBER}
az webapp restart -g rg-stockanalyzer-prod -n app-stockanalyzer-prod
```

### 9.6 Observability

#### Structured Logging (Serilog)

**Package:** `Serilog.AspNetCore` v10.0.0

**Configuration:**
- Console output with timestamp, level, message, and properties
- File output to `logs/stockanalyzer-{date}.log` with daily rolling
- 7-day log retention
- Request logging middleware for HTTP request/response tracking

**Log Levels:**
- `Information` for application events
- `Warning` for Microsoft framework (reduced noise)
- `Fatal` for unhandled exceptions

**Sample Output:**
```
[14:32:15 INF] Starting Stock Analyzer API {"Application":"StockAnalyzer"}
[14:32:16 INF] HTTP GET /api/stock/AAPL responded 200 in 145.2341ms
```

#### Health Checks

**Endpoints:**
| Endpoint | Purpose | Checks |
|----------|---------|--------|
| `/health` | Full status | All checks with detailed JSON response |
| `/health/live` | Liveness probe | Self-check only (is app running?) |
| `/health/ready` | Readiness probe | External dependencies (Finnhub, Yahoo) |

**Response Format:**
```json
{
  "status": "Healthy",
  "timestamp": "2026-01-17T14:32:15Z",
  "duration": 245.5,
  "checks": [
    { "name": "self", "status": "Healthy", "duration": 0.1 },
    { "name": "finnhub-api", "status": "Healthy", "duration": 120.3 },
    { "name": "yahoo-finance", "status": "Healthy", "duration": 125.1 }
  ]
}
```

#### Status Dashboard (`/status.html`)

A visual health monitoring dashboard accessible from the main app footer.

**Features:**
- Real-time health status display (Healthy/Degraded/Unhealthy)
- Individual service status cards (API, Finnhub, Yahoo Finance)
- API endpoint status table with response times
- Image cache status bars (cats/dogs)
- Auto-refresh every 30 seconds
- Dark mode support (matches main app)

**Layout:**
```
┌──────────────────────────────────────────────────────────────┐
│  System Status              Updated: 3:15 PM       [🌙/☀️]   │
├──────────────────────────────────────────────────────────────┤
│  ● All Systems Operational    Response Time: 57ms            │
├──────────────────────────────────────────────────────────────┤
│  [API Server ●]  [Finnhub API ●]  [Yahoo Finance ●]          │
├──────────────────────────────────────────────────────────────┤
│  API Endpoints                                                │
│  /api/stock/AAPL      Stock information      ● 200 (145ms)   │
│  /api/search?q=apple  Ticker search          ● 200 (89ms)    │
│  /api/trending        Trending stocks        ● 200 (234ms)   │
│  /health              Health check           ● 200 (57ms)    │
├──────────────────────────────────────────────────────────────┤
│  Image Cache                                                  │
│  Cat Images: [████████████░░░░░░] 32/50                       │
│  Dog Images: [██████████████████] 50/50                       │
└──────────────────────────────────────────────────────────────┘
```

### 9.6 Security Analyzers

#### Build-Time SAST Tools

| Tool | Package | Purpose |
|------|---------|---------|
| SecurityCodeScan | `SecurityCodeScan.VS2019` | OWASP Top 10 detection (SQL injection, XSS, etc.) |
| NetAnalyzers | `Microsoft.CodeAnalysis.NetAnalyzers` | Official .NET security + reliability rules |
| Roslynator | `Roslynator.Analyzers` | Extended code quality analysis |

**Configuration:** `.editorconfig` sets all CA5xxx security rules as errors.

#### CI/CD Security Tools

| Tool | Integration | Purpose |
|------|-------------|---------|
| CodeQL | GitHub Actions | Weekly SAST scans for C# and Python |
| OWASP Dependency Check | GitHub Actions | NuGet vulnerability scanning against NVD |
| Dependabot | GitHub | Automated PRs for vulnerable dependencies |

**Pipeline Flow:**
```
Build → Test → Security Scan → Artifact Upload
                    │
    ┌───────────────┼───────────────┐
    ▼               ▼               ▼
 CodeQL    OWASP Dep Check    Dependabot
 (SAST)         (SCA)          (Auto-PR)
```

---

## 10. Known Issues and Workarounds

### 10.1 Dividend Yield Inconsistency

**Issue:** Yahoo Finance returns dividend yield in inconsistent formats.

**Workaround:** Validation in `StockDataService`:
```csharp
private static decimal? ValidateDividendYield(decimal? yield)
{
    if (!yield.HasValue) return null;
    if (yield.Value > 0.10m)
        return yield.Value / 100;  // Correct inflated value
    return yield;
}
```

### 10.2 OoplesFinance API Wrapper Types

**Issue:** The library returns wrapper types with `Raw` properties instead of primitive values.

**Workaround:** Reflection-based extraction:
```csharp
private static decimal? TryGetDecimal(object? value)
{
    if (value == null) return null;
    var rawProp = value.GetType().GetProperty("Raw");
    if (rawProp != null)
    {
        var rawValue = rawProp.GetValue(value);
        if (rawValue is double d) return (decimal)d;
    }
    // Direct conversion fallback...
}
```

### 10.3 Search Not in OoplesFinance

**Issue:** OoplesFinance library doesn't provide ticker search functionality.

**Workaround:** Direct HTTP call to Yahoo Finance search API in `SearchAsync()`.

---

## 11. Troubleshooting

### 11.1 Application Won't Start

| Error | Cause | Solution |
|-------|-------|----------|
| `Port 5000 already in use` | Another process using port | Kill process or change port in launchSettings.json |
| `Unable to find package` | NuGet restore needed | Run `dotnet restore` |
| Build errors | SDK version mismatch | Ensure .NET 8.0 SDK installed |

### 11.2 No Data Displayed

| Cause | Solution |
|-------|----------|
| Invalid ticker symbol | Verify ticker exists on Yahoo Finance |
| Network connectivity | Check internet connection |
| API rate limit | Wait and retry |

### 11.3 News Not Loading

| Cause | Solution |
|-------|----------|
| Missing API key | Check appsettings.json or environment variable |
| API rate limit exceeded | Wait 1 minute (free tier: 60 req/min) |
| Invalid API key | Verify key at https://finnhub.io/dashboard |

### 11.4 Search Not Working

| Cause | Solution |
|-------|----------|
| Query too short | Type at least 2 characters |
| Yahoo Finance API down | Check Yahoo Finance directly |
| Network timeout | Check connectivity, try again |

---

## 12. Security Considerations

### 12.1 API Key Storage

- API keys stored in `appsettings.json` (not committed to git)
- `.gitignore` includes `appsettings.Development.json`
- Production should use environment variables or secret management

### 12.2 Data Privacy

- No user data is stored or transmitted
- All data requests are read-only
- No authentication/login system

### 12.3 Network Security

- Default: localhost only (safe for development)
- Production deployment uses HTTPS via Cloudflare
- HSTS header enabled in production (1-year max-age)
- CORS restricted to known origins: `psfordtaurus.com`, `localhost:5000`, `localhost:5001`

### 12.3.1 Input Validation

Ticker symbols are validated before processing to prevent injection attacks:
- Maximum 10 characters
- Allowed characters: alphanumeric, dots (`.`), dashes (`-`), carets (`^`)
- Examples: `AAPL`, `BRK.B`, `^GSPC`
- Invalid inputs return 400 Bad Request

### 12.3.2 Log Injection Prevention

All user input (ticker symbols, search queries) is sanitized before logging to prevent log forging attacks:

```csharp
// StockAnalyzer.Core/Helpers/LogSanitizer.cs
public static string Sanitize(string? value)
{
    // Replaces control characters (newlines, tabs, etc.) with underscores
    // Prevents attackers from injecting fake log entries
}

// Usage in services:
_logger.LogDebug("Cache hit for {Symbol}", LogSanitizer.Sanitize(symbol));
```

**Protection against:**
- Log forging (injecting fake log entries via newlines)
- Log tampering (confusing log analysis tools)
- CRLF injection in log files

**Services protected:**
- `AggregatedStockDataService` - symbol and query logging
- `TwelveDataService` - symbol and query logging
- `FmpService` - symbol and query logging
- `YahooFinanceService` - symbol and query logging

### 12.4 Content Security Policy

The application uses CSP headers to restrict resource loading. Since images are now
processed server-side, the client only connects to our own backend.

```
Content-Security-Policy:
  default-src 'self';
  script-src 'self' 'unsafe-inline' 'unsafe-eval' https://cdn.tailwindcss.com https://cdn.plot.ly;
  style-src 'self' 'unsafe-inline' https://cdn.tailwindcss.com;
  img-src 'self' data: blob:;
  font-src 'self' https:;
  connect-src 'self'
```

| Directive | Allowed Sources | Purpose |
|-----------|-----------------|---------|
| `script-src` | CDN for Tailwind, Plotly | Chart and styling libraries |
| `img-src` | `'self'`, `data:`, `blob:` | Images from backend + blob URLs |
| `connect-src` | `'self'` only | All API calls to own backend |

### 12.5 Subresource Integrity (SRI)

SRI hashes verify that CDN-loaded scripts haven't been tampered with.

**Plotly.js** - SRI enabled:
```html
<script src="https://cdn.plot.ly/plotly-2.27.0.min.js"
        integrity="sha384-Hl48Kq2HifOWdXEjMsKo6qxqvRLTYqIGbvlENBmkHAxZKIGCXv43H6W1jA671RzC"
        crossorigin="anonymous"></script>
```

**Tailwind CSS CDN** - SRI not applicable:
- `cdn.tailwindcss.com` is a JIT (Just-In-Time) compiler
- Generates CSS dynamically based on classes used in the page
- Content changes per request, so hash verification would fail
- For production, pre-build Tailwind locally: `npx tailwindcss -o styles.css`

**flatpickr** - SRI enabled:
```html
<script src="https://cdn.jsdelivr.net/npm/flatpickr@4.6.13/dist/flatpickr.min.js"
        integrity="sha384-..." crossorigin="anonymous"></script>
<link rel="stylesheet" href="https://cdn.jsdelivr.net/npm/flatpickr@4.6.13/dist/flatpickr.min.css"
      integrity="sha384-..." crossorigin="anonymous">
```

| Resource | SRI Status | Reason |
|----------|------------|--------|
| Plotly.js 2.27.0 | ✅ Enabled | Static versioned file |
| Tailwind CSS CDN | ❌ Not applicable | Dynamic JIT compiler |
| flatpickr 4.6.13 | ✅ Enabled | Static versioned file (~16KB + 4KB CSS) |

---

## 13. Performance Considerations

### 13.1 Caching

**Backend:** Currently no caching implemented. Each request fetches fresh data.
**Future Enhancement:** Add `IMemoryCache` for API responses.

**Server-Side Image Processing:**
- YOLOv8n ONNX model loaded once at startup (~12MB)
- Inference time: ~10-50ms per image on CPU
- Background cache: 50 cat + 50 dog images pre-processed
- Automatic refill when cache drops below 10 images
- Images stored as compressed JPEG byte arrays (~10-30KB each)

**Frontend Image Caching:**
- Fetches blob URLs from `/api/images/{type}` endpoints
- Images converted to blob URLs for instant display
- Automatic refill when cache drops below 10 images
- Each image used once to ensure variety

### 13.2 API Rate Limits

| Service | Limit | Impact |
|---------|-------|--------|
| Finnhub | 60/min | News fetching may fail under heavy use |
| Yahoo Finance | Undocumented | Occasional request failures possible |

### 13.3 Parallel Requests

Frontend uses a two-phase loading strategy for fast perceived performance:
```javascript
// PHASE 1: Critical path - single combined request for chart + analysis
const chartData = await API.getChartData(ticker, period, from, to);

// PHASE 2: Secondary data loaded in background (non-blocking)
// Significant moves always uses chart data's actual date range (not UI state)
// to guarantee markers align with the displayed chart
const significantMovesPromise = API.getSignificantMoves(
    ticker, threshold, null, chartData.startDate, chartData.endDate);
const stockInfoPromise = API.getStockInfo(ticker);
const newsPromise = API.getAggregatedNews(ticker, 30, 10);
```

---

## 14. Version History

| Version | Date | Changes |
|---------|------|---------|
| 2.53 | 2026-02-26 | **SecurityMaster MIC Code Enrichment (Phase 2):** Renamed `Exchange` → `MicCode` on SecurityMaster DTOs and repository methods. `SecurityMasterCreateDto` and `SecurityMasterUpdateDto` now use `MicCode` (ISO 10383 code populated by backfill endpoint). `SqlSecurityMasterRepository.CreateAsync/UpdateAsync/UpsertManyAsync` updated to handle `MicCode` instead of `Exchange` — **code review fix:** `CreateAsync` (line 81) and `UpsertManyAsync` insert path (line 201) now respect `dto.MicCode?.Trim()` instead of hardcoding `null`, fixing asymmetry with update paths. `GetAllActiveAsync` documents why `.Include(MicExchange)` is acceptable: ~2,800 reference rows with efficient char(4) PK/FK join adds minimal DTU overhead vs 55K row scan. Added `MicCode` and `ExchangeName` fields to response models (`StockInfo`, `SearchResult`, `CompanyProfile`) while keeping `Exchange` for external API compatibility (FmpService, TwelveDataService, YahooFinanceService, Finnhub). `SearchResult.DisplayName` now prefers `ExchangeName` (e.g., "New York Stock Exchange") over `Exchange` for readability. Updated `PriceRefreshService` methods (`SyncSecurityMasterFromEodhdAsync`, `SyncSecurityMasterFromSymbolCacheAsync`) to set `MicCode = null` (populated later by backfill-mic-codes endpoint in Phase 4). Verifies: secmaster-mic.AC6.1, secmaster-mic.AC6.2 (partial). |
| 2.52 | 2026-02-24 | **Pipeline DTU Fix (5 Phases):** Complete replacement of all Prices table full-scans with pre-aggregated coverage metadata. **Phase 1:** Two new EF Core entities (`SecurityPriceCoverageEntity`, `SecurityPriceCoverageByYearEntity`) with idempotent migration. **Phase 2:** `ComputeCoverageDeltas` pure function + `UpdateCoverageAsync` MERGE SQL wired into `BulkInsertAsync` for incremental coverage updates during price loads (no Prices scan). 10 unit tests + 6 integration tests. **Phase 3:** Gap endpoint 4-CTE query replaced with 3-table coverage join (timeout 300s→30s). **Phase 4:** Refresh-summary Prices scan replaced with CoverageByYear aggregation (timeout 300s→30s). TradingDays now from BusinessCalendar (expected days, not actual). **Phase 5:** New `POST /api/admin/prices/backfill-coverage` endpoint for one-time bootstrap via MERGE (600s timeout, semaphore-guarded, idempotent). All operations now within 5 DTU budget on Azure SQL Basic. |
| 2.51 | 2026-02-24 | **SecurityPriceCoverage DbContext Registration (Pipeline DTU Fix Phase 1):** Registered `SecurityPriceCoverageEntity` and `SecurityPriceCoverageByYearEntity` in `StockAnalyzerDbContext.cs` with Fluent API configuration. Tables: `data.SecurityPriceCoverage` (PK: SecurityAlias, 1:1 FK to SecurityMaster) and `data.SecurityPriceCoverageByYear` (PK: SecurityAlias+Year, N:1 FK to SecurityMaster). GapDays configured as persisted computed column `ISNULL(ExpectedCount, 0) - PriceCount`. Foundation for Phase 2–5 DTU optimization (pre-aggregated coverage metadata replaces expensive full-table Prices scans). |
| 2.48 | 2026-02-05 | **Theme Editor Security Hardening:** Comprehensive anti-abuse system for theme generator. **Sanitization:** `sanitize_prompt()` enforces 2000-char max, strips control characters (CWE-117), removes null bytes. Frontend: character counters, `maxlength` attributes. **Scope Enforcement:** `is_theme_related()` rejects off-topic prompts; system prompt hardened to ONLY output theme JSON; `validate_theme_response()` verifies valid CSS variables in response. **Jailbreak Detection:** IP-based violation tracking with escalating blocks — 2s rate limit, 3 violations → 5-min soft block, 5 violations → 60-min hard block (escalates on repeat). Detects evasion patterns: encoding tricks, multi-language, instruction overrides, theme word stuffing. `enforce_access_control()` integrates all checks. HTTP 429 responses for blocked clients. |
| 2.47 | 2026-02-05 | **JSON Theme System with AI Generation:** Complete theme system overhaul. New `ThemeLoader.js` with 94+ CSS custom properties, theme inheritance (`extends` property), deep merge with circular detection. Background image support with overlay and blur. Theme audio parameters (key, mode, chordProgression, texture, tempo) for procedural music synthesis. Visual effects: scanlines, vignette, bloom, rain, CRT flicker. New `ThemePreview.js` mini-app component for live preview. New `ThemeEditor.js` with AI-powered generation via Python FastAPI service. New `ThemeAudio.js` for theme-driven audio. Theme Generator (`helpers/theme_generator.py`): mock mode (keyword-matched themes, no API cost) and live mode (Claude API). New Grimdark Space Opera theme (Warhammer 40K inspired, blood red + imperial gold). Hotdog Stand mock theme. Theme manifest with icons. ThemeLoader localhost priority for dev workflow. Watchlist.js refactored to use semantic CSS classes (Tailwind removal). Section 6.3 rewritten to document JSON theme architecture. |
| 2.46 | 2026-02-03 | **Watchlist as GridStack Tile:** Converted fixed-position `<aside id="watchlist-sidebar">` into 7th GridStack tile (`tile-watchlist`, 4w×5h, min-w 3, min-h 3). Chart tile narrowed from 12w to 8w; watchlist fills right side of top row. LAYOUT_VERSION bumped from 6 to 7 (clears saved layouts). **Watchlist toggle:** Star button (`#watchlist-toggle-btn`) in page header toggles tile visibility with `.watchlist-toggle-active` yellow highlight state. On reopen, calls `Watchlist.loadWatchlists()` to re-bind events. **Horizontal expansion on tile close:** New `expandRowNeighbor()` function — when any tile is closed, its horizontal neighbor on the same row expands to fill the gap. State tracked in `tileExpansions` object (`{ neighborId, origW, origX }`). On reopen, neighbor shrinks back and tile restores to original position. General-purpose: works for any adjacent tile pair. **Dead code removed:** `initMobileSidebar()` (~37 lines), `bindMobileWatchlistEvents()` (~57 lines), mobile-watchlist-drawer handlers (~50 lines) from app.js. Sidebar DOM references removed from watchlist.js (2 lines). `<aside>`, `<div id="sidebar-overlay">`, and `mobile-watchlist-toggle` button removed from index.html. **CSS:** `#tile-watchlist-body` flex column layout with internal scroll; `.watchlist-toggle-active` yellow star styles (light + dark mode). All watchlist element IDs preserved — no changes to watchlist.js business logic. |
| 2.45 | 2026-02-03 | **Wikipedia Company Bio with DB Caching (`data.CompanyBio`):** New `CompanyBioEntity` with 1:1 FK to SecurityMaster via SecurityAlias. EF Core migration `AddCompanyBio` creates `data.CompanyBio` table (PK: SecurityAlias, nvarchar(max) Description, nvarchar(50) Source, FetchedAt/UpdatedAt with GETUTCDATE defaults). New `WikipediaService` fetches descriptions from Wikipedia REST API — two-step lookup: (1) direct page summary via `en.wikipedia.org/api/rest_v1/page/summary/{name}`, (2) search fallback via `en.wikipedia.org/w/api.php?action=query&list=search` for abbreviated names. Filters disambiguation pages (`type == "standard"`), 5s timeout, 24h IMemoryCache, no API key. **Rate limiting:** `SemaphoreSlim(1,1)` single-concurrency + 2-second minimum gap between every HTTP request to Wikipedia — treats Wikipedia as a shared public resource, never exceeds casual browsing pace. Combined with permanent DB caching, each company is fetched at most once. **Endpoint integration (`GET /api/stock/{ticker}`):** checks CompanyBio by SecurityAlias first (DB cache hit = no external calls); on cache miss, determines best description (provider if ≥150 chars, else Wikipedia fallback) and fire-and-forget stores in CompanyBio. Tickers not in SecurityMaster get Wikipedia lookup without caching. FR-006.8, FR-006.9. |
| 2.44 | 2026-02-02 | **Technical Indicator Tooltips:** Added native HTML `title` attributes to all 7 indicator/MA checkboxes (SMA 20, SMA 50, SMA 200, RSI, MACD, Bollinger Bands, Stochastic). Each tooltip explains the indicator's purpose, key thresholds (e.g., RSI 70/30, Stochastic 80/20), and interpretation. Uses existing native tooltip pattern (consistent with dark mode toggle, audio toggle, etc.). FR-011.24, FR-011.25. |
| 2.43 | 2026-02-02 | **Tile Dashboard (GridStack.js v12):** Results section wrapped in 6 draggable/resizable tiles using GridStack.js 12.4.2 (downloaded locally). Tiles: Chart (12w), Company Info (8w), Key Metrics (4w), Performance (6w), Significant Moves (6w), News (12w). 12-column grid with `cellHeight: 70`, `margin: 12`, mobile single-column at `<768px`. Physics engine: spring CSS transitions (`cubic-bezier(0.25, 1.1, 0.5, 1)`), lift effect (`scale(1.025)` + shadow), magnetic pull (50px threshold, 0.35 strength), snap settle animation (400ms keyframes with scale overshoot + blue glow), FLIP neighbor animations via MutationObserver + WAAPI, placeholder reveal animation (200ms). Web Audio API snap sound (1200Hz + 300Hz dual-oscillator, 80ms). Tile locking (noMove/noResize + dashed border + diagonal hatch). Close/reopen via panel dropdown with content caching. Layout persistence in localStorage (`stockanalyzer_tile_layout`, version 1). Lazy init via MutationObserver on `#results-section` class changes — zero modifications to app.js/charts.js/symbolSearch.js/api.js/dragMeasure.js/watchlist.js/storage.js. Chart height: CSS `!important` override + ResizeObserver → `Plotly.Plots.resize()`. Watchlist dropdown overflow fix on tile-info. Coupled horizontal resize: adjacent tiles shrink/grow inversely instead of being pushed away (uses `float(true)` during resize, `maxW` constraint, `_findRowNeighbors()` detection). Edge-drag resize on all edges (no corner grip icons). In-place Reset Layout (no page reload). New files: `lib/gridstack/gridstack-all.min.js`, `lib/gridstack/gridstack.min.css`, `js/tileDashboard.js` (~580 lines). New CSS: ~280 lines in `input.css`. New section 6.8 documents architecture. |
| 2.42 | 2026-02-01 | **Deploy Warmup & Bicep Drift Detection:** (1) Synced `main.bicep` to match live Azure config: F1/Free → B1/Basic SKU, `alwaysOn: true` (was false). (2) Added "Warm up application caches" step to deploy workflow — primes static files, symbol cache, DB pool, and health check before smoke tests. (3) Reduced container startup wait from 60s to 30s. (4) **Bicep drift detection** in deploy preflight: parses SKU and alwaysOn from `main.bicep`, compares against live Azure via `az` CLI, fails deployment if they diverge. Prevents stale Bicep from causing incorrect infrastructure assumptions. |
| 2.41 | 2026-02-01 | **Flatpickr Date Picker Integration:** (1) **Desktop/mobile device detection** — `window.matchMedia('(pointer: fine)')` detects mouse/trackpad → uses flatpickr widget; touch-only devices → native `<input type="date">` for optimal UX. (2) **Flatpickr initialization** — only when "Custom" date mode selected, destroyed when switching back to preset to save memory. (3) **CSS theming** — dark mode support via CSS custom properties (`--fp-*` variables) with `.dark .flatpickr-calendar` selector; no JS theme swapping. (4) **External dependency** — flatpickr 4.6.13 (~16KB + 4KB CSS) loaded from CDN with SRI hash. New subsection 6.7 documents device detection, lifecycle, state management, and CSS theming. SRI table updated (Section 12.5). (5) **Privacy disclosure** — Device Detection added to about.html privacy section (pointer-fine media query is observable but read-only). |
| 2.40 | 2026-02-01 | **Date Range UI Redesign:** Replaced `#period-select` dropdown + `#custom-date-range` (hidden From/To/Apply) with a two-row date range sub-panel. **New HTML:** `#end-date-preset` (PBD/LME/LQE/LYE/Custom) + `#end-date-resolved` (readonly date input) on row 1; `#start-date-preset` (1D–30Y/MTD/YTD/Max/Custom) + `#start-date-resolved` on row 2. **New state model:** replaced `currentPeriod`/`customDateFrom`/`customDateTo` with `endDatePreset`/`startDatePreset`/`resolvedEndDate`/`resolvedStartDate`. **Date resolution functions:** `resolveEndDate(preset)` computes PBD (skip weekends), LME, LQE, LYE. `resolveStartDate(preset, endDateStr)` computes relative to End Date with inclusive periods (subtract N, +1 day). `recalculateStartDate()` re-derives start when end changes. `triggerReanalysis()` re-fetches data on date change. **bindEvents:** 4 new listeners for preset dropdowns and resolved inputs, replacing period-select and apply-date-range handlers. `setDateInputEditable()` toggles readonly + CSS classes for Custom mode. **analyzeStock/setComparison:** now use `resolvedStartDate`/`resolvedEndDate` directly, period param always null. **clearAll:** resets to PBD + 1Y defaults. **charts.js:** `formatPeriodLabel()` simplified to always show date range when startDate/endDate exist. **Dead code removed:** `initPeriodSelect()`, `currentPeriod`, `customDateFrom`, `customDateTo`, mobile period select sync, `#period-select`, `#custom-date-range`, `#date-from`, `#date-to`, `#apply-date-range`. |
| 2.39 | 2026-02-01 | **Significant Moves Date Range Structural Fix:** Decoupled significant moves fetching from UI state. Both `analyzeStock()` and `refreshSignificantMoves()` now derive date range from `chartData.startDate`/`chartData.endDate` (the actual dates returned by the chart-data endpoint) instead of `this.currentPeriod`/`this.customDateFrom`/`this.customDateTo` (UI input state). This eliminates the class of bugs where significant move markers extend past the visible chart because the UI state disagrees with the actual data range. The `period` parameter is passed as `null` and `from`/`to` are always populated from chart response. Added `historyData` null guard to `refreshSignificantMoves()`. |
| 2.38 | 2026-02-01 | **Custom Date Ranges + Real-Time Crawler Stats:** (1) **Extended period options** — added 1D, 5D, MTD, 15Y, 20Y, 30Y, and Since Inception (max/all) to `GetDateRangeForPeriod`. (2) **Custom from/to date support** — `/chart-data` and `/history` endpoints now accept optional `from` and `to` query parameters for arbitrary date ranges. New `GetHistoricalDataAsync(symbol, from, to)` overload in AggregatedStockDataService with separate cache key pattern `history:{SYMBOL}:{from}:{to}`. API provider fallback synthesizes an appropriate period and filters results to requested range. (3) **Frontend date range UI** — period `<select>` expanded with all new options plus "Custom Range..." which reveals start/end date inputs with Apply button. (4) **Combined portfolio period buttons** — added YTD, 5Y, and All buttons. (5) **EODHD Loader real-time stats** — Price Records card now updates live during crawling (base count + session inserts, zero API overhead). Tracked/Untracked/Unavailable cards update locally when promoting or marking securities unavailable. |
| 2.37 | 2026-02-01 | **News Service Quality Overhaul:** 5 fixes to improve news relevance and completeness: (1) **Tightened relevance scoring** — HeadlineRelevanceService `CalculateTickerScore` demoted `RelatedSymbols`-only matches from 1.0 to 0.3 (Finnhub tags loosely related articles). Headline mentions now score 1.0, summary mentions 0.7. This filters out ~60% noise articles. (2) **Enriched `/news` endpoint** — now applies SentimentAnalyzer + HeadlineRelevanceService to all articles, looks up company profile for name-based relevance, filters to top 30 by relevance (was 249 raw). Added `limit` query parameter. (3) **Extended `/news/move` date window** — `GetNewsForDateAsync` now looks date-2 to date+3 (was date+1), capturing explanatory articles written after significant moves. (4) **Fixed market news fallback for historical dates** — old dates (>3 days) now return best-available company news instead of attempting market news lookup (which always fetched current news, useless for old dates). (5) **Added `/news/move` response metadata** — new `MoveNewsResult` model with `source` ("company"/"market"), `directionMatch` (bool). Cache type updated from `List<NewsItem>` to `MoveNewsResult`. |
| 2.36 | 2026-02-01 | **Chart Loading Performance Optimization:** 6 optimizations to reduce chart load time on 5 DTU Azure SQL: (1) **Combined `/api/stock/{ticker}/chart-data` endpoint** — returns history + analysis in a single request, eliminating duplicate `GetHistoricalDataAsync` calls and saving an HTTP round-trip. Frontend `analyzeStock()` switched from `Promise.all([getHistory, getAnalysis])` to single `getChartData()` call. Old endpoints preserved for backward compat (comparison mode, watchlist). (2) **Cache coalescing (stampede prevention)** — added `ConcurrentDictionary<string, Task<HistoricalDataResult?>> _inflight` to `AggregatedStockDataService`. Concurrent cache misses for the same key share one in-flight task via `GetOrAdd()`, preventing duplicate DB queries under load. (3) **HttpClient timeouts** — set explicit timeouts on all external API services (TwelveData/FMP: 15s, NewsService: 10s, Yahoo: 10s) replacing the default 100s timeout. Worst-case external cascade reduced from 300s to 45s. (4) **`Plotly.react` for re-renders** — added `_smartPlot()` helper to charts.js: uses `Plotly.newPlot` for first render, `Plotly.react` (diff-based incremental update) for subsequent renders. Eliminates full DOM teardown on indicator toggles and significant move marker additions. (5) **Eliminated chart double-render** — with `Plotly.react`, the second `renderChart()` call when significant moves arrive (app.js Phase 2) now does an incremental update instead of a full rebuild. (6) **DB connection pool warmup** — new `DbWarmupService` (IHostedService) runs `SELECT 1` on startup, plus `Min Pool Size=2` in connection strings (local + Bicep). Eliminates cold-start TCP+TLS+auth penalty on first user request. |
| 2.35 | 2026-01-31 | **Dashboard Statistics Redesign:** Fixed critical data binding bug — Card 3 ("WITH GAPS") was bound to `TrackedDisplay` (`Universe.Tracked` = tracked universe count) instead of `SecuritiesWithGaps` (actual gap count from gaps endpoint). This caused the "Tracked keeps increasing" confusion. **3-Tier Metric Layout:** Replaced 5 identical cards with: Tier 1 hero card (DATA COVERAGE progress bar + gap count + delta), Tier 2 reference cards (TRACKED UNIVERSE / PRICE RECORDS / DATA SPAN), Tier 3 session cards (TICKERS + RECORDS with rate/hr). **Session metrics:** Track rate/hr, session duration, show "last session" counts when idle instead of "0". **Gap delta indicator:** Shows "▼ N this session" with green/red coloring. **CoverageSummary freshness:** API returns `summaryLastRefreshed` (MAX LastUpdatedAt from CoverageSummary); client shows relative time. `load-tickers` endpoint invalidates `dashboard:stats` cache on insert. Client triggers `refresh-summary` on crawler stop. |
| 2.34 | 2026-01-31 | **GetDistinctDatesAsync SQL Fix:** Replaced recursive CTE with `CROSS APPLY TOP 1` (which SQL Server prohibits in recursive CTE members along with aggregates) with while-loop using `MIN()` index seeks on `IX_Prices_EffectiveDate`. Same ~500 seek performance, fully compatible with SQL Server/SQL Express. Fixed Unicode encoding (em-dash/arrow) in test_dtu_endpoints.py for Windows cp1252 console. All 8/8 DTU endpoint tests pass on localhost. |
| 2.33 | 2026-01-31 | **Program.cs + PriceRefreshService DTU Fixes:** 10 issues fixed: (1) **Data export** — removed separate COUNT scan, uses `pageSize+1` with CoverageSummary approximate total. (2) **refresh-summary** — `SemaphoreSlim(1,1)` guard, returns 409 Conflict if busy. (3) **BackfillTickersParallelAsync** — default concurrency 10→3, max 50→10. (4) **calculate-importance** — `SemaphoreSlim(1,1)` guard, processes in 1000-row pages with `ChangeTracker.Clear()`. (5) **bulk-load** — `SemaphoreSlim(1,1)` guard, returns 409 if busy. (6) **auto-track** — `SemaphoreSlim(3,3)` guard, skips if at capacity. (7) **seed-tracked** — added `WITH (NOLOCK)` on read-only joins. (8) **CoverageSummary** — added `AsNoTracking()` to stats and heatmap reads. (9) **populate-us-calendar** — batched into 2000-row chunks with per-batch SaveChanges. (10) **PriceRefreshService callers** — replaced `GetAllActiveAsync()` (55K full entities) with `GetActiveTickerAliasMapAsync()` (2-column projection). |
| 2.32 | 2026-01-31 | **Repository N+1 & Search Fixes:** (1) **SecurityMasterRepository.UpsertManyAsync** — batch-fetch existing per 500 chunk via `WHERE TickerSymbol IN (...)` + `ToDictionary` (55K individual queries → 110 batch queries). (2) **SymbolRepository.UpsertManyAsync** — same batch-fetch pattern (30K → 60 queries). (3) **SecurityMasterRepository.SearchAsync** — added `.Take(limit * 5)` before `ToListAsync()` to prevent unbounded 55K+ entity fetch. (4) **SymbolRepository.SearchFromDatabaseAsync** — same `.Take(limit * 5)` bound. (5) **New `GetActiveTickerAliasMapAsync()`** — projected query returning `Dictionary<string, int>` (ticker→alias) using only 2 columns instead of materializing full entities. |
| 2.31 | 2026-01-31 | **SqlPriceRepository DTU Overhaul:** 5 fixes in SqlPriceRepository.cs: (1) **AnalyzeHolidaysAsync** — replaced `SELECT DISTINCT` on 5M+ Prices with `GetDistinctDatesAsync()` skip-scan, replaced N+1 prior-BD loop (~2,700 queries) with single calendar load + binary search (5 total queries). (2) **ForwardFillHolidaysAsync** — complete rewrite from triple-nested N+1 (~12,000 DB ops) to batch raw SQL `MERGE` with temp table mapping in chunks of 50 non-BD dates (~70 batch ops). (3) **BulkInsertAsync** — moved dedup inside per-1000-batch loop to avoid massive IN clauses (hundreds of aliases × thousands of dates). (4) **GetDateRangeAsync** — replaced `GroupBy(_ => 1)` anti-pattern with two `TOP 1` index seeks. (5) **GetCountForSecurityAsync** — added `AsNoTracking()`. |
| 2.30 | 2026-01-31 | **Coverage-Dates DTU Fix:** Rewrote `GetDistinctDatesAsync()` from `SELECT DISTINCT EffectiveDate` (full index scan on 5M+ rows, times out on Azure SQL Basic) to recursive CTE with `CROSS APPLY TOP 1` skip-scan pattern (~500 index seeks for 2 years of trading days). Fixes Boris Bulk Fill tab "Could not get coverage dates" error against production. |
| 2.29 | 2026-01-28 | **Crawler Overhaul — Full-Range Loading + Auto-Promotion:** Replaced per-date loading (one EODHD API call per date, ~25 min/security) with full-range loading (single API call fetching 1980-today, ~15-20s/security, ~75-100x faster). Re-enabled auto-promotion at all 3 stop points in CrawlerViewModel — `PromoteAndRefreshAsync()` was never called, causing crawler to stop after tracked securities instead of promoting untracked batches. Promotion batch size increased from 100 to 500. **Heatmap V2 Improvements:** Stone-drop ripple effect (3 expanding rings from epicenter to grid corners, clipped to grid area, `RipplePeriod=6π`), edge phantom blobs (2-deep mirroring at boundaries to prevent gradient fade), tuned blob radius (1.4→1.6x) and blur sigma (0.6→0.5x). **UI Cleanup:** Removed dev tools button from MainWindow, renamed "DATA COMMAND CENTER" to "DATA LOAD MONITOR". New admin endpoints documented: `POST /api/admin/prices/load-tickers`, `POST /api/admin/prices/backfill`. |
| 2.28 | 2026-01-27 | **Simplified Crawler — Remove Phase 2, Add Promote + Auto-Track:** Replaced the two-phase crawler (which never worked in production due to Azure SQL Basic timeouts) with a single-loop design. **New `POST /api/admin/securities/promote-untracked` endpoint:** selects top N untracked by ImportanceScore DESC, inserts into TrackedSecurities, sets IsTracked=1 (temp table SQL for CA2100 compliance). **Auto-track on stock view:** `GET /api/stock/{ticker}` fires background task to mark viewed securities as tracked (user-search source). **Gap query simplified:** removed `includeUntracked` parameter and expensive untracked CTEs (SecuritiesWithPrices, NoPriceData, StaleData, AllGaps). Replaced with UNION ALL of TrackedWithGaps + TrackedNoPrices (NOT EXISTS index seek). **EODHD Loader crawler rewritten:** removed Phase 1/Phase 2 state machine, `SwitchToUntrackedPhaseAsync()`, `IsInUntrackedPhase`, separate gap count properties. New single loop: fill tracked gaps → queue empty → promote batch → re-query → continue. `PromoteUntrackedAsync()` added to StockAnalyzerApiClient. CurrentPhase shows "Crawling"/"Promoting..."/"Complete" instead of "Phase 1/Phase 2". |
| 2.27 | 2026-01-27 | **CoverageSummary Pre-Aggregation Table:** New `data.CoverageSummary` table (~500 rows) via EF Core migration to avoid expensive full-table scans on 3.5M+ row Prices table. Critical for Azure SQL Basic tier (5 DTU) where the original `GROUP BY YEAR()` + `COUNT(DISTINCT)` aggregation query timed out. **Heatmap endpoint rewritten** to read from summary table + `IMemoryCache` (30 min TTL). **Stats endpoint optimized:** result sets 4-5 (decade/year coverage) now read from summary table; `IMemoryCache` added (10 min TTL). **New `POST /api/admin/dashboard/refresh-summary` endpoint** runs the expensive aggregation once and writes results to `data.CoverageSummary`. **Build-Boris.ps1 helper script** parses TargetFramework from csproj to dynamically resolve correct build output path, preventing stale shortcut issues when TFM changes. |
| 2.26 | 2026-01-27 | **Bivariate Coverage Heatmap:** New `/api/admin/dashboard/heatmap` endpoint returning Year × ImportanceScore cross-tabulation with tracked/untracked split. Custom SkiaSharp heatmap control (`CoverageHeatmapControl.cs`) with bivariate color mapping (blue=tracked, yellow=untracked, green=both), pulsing active cell indicator (sine-wave animation at 30fps), live cell updates during crawling, hover tooltips, and bivariate legend. **Future Date Bug Fix:** Capped `LastDate` at `GETDATE()` in gaps CTE and specific dates endpoint to prevent crawler from requesting future dates from EODHD (was causing valid tickers like AA, AG to be incorrectly marked unavailable). **Reset Unavailable Endpoint:** New `POST /api/admin/securities/reset-unavailable` to roll back incorrect IsEodhdUnavailable markings (supports `?days=N` and `?all`). **Calculate-importance expanded** to process ALL active securities (not just untracked) for proper heatmap Y-axis distribution. |
| 2.25 | 2026-01-27 | **Dashboard Command Center:** New consolidated `/api/admin/dashboard/stats` endpoint returning universe counts, price record stats, ImportanceScore tier distribution, decade coverage, and year-by-year coverage in a single 5-result-set SQL batch. EODHD Loader UI redesigned with LiveChartsCore charts: horizontal bar chart for coverage by decade, stacked column chart for importance tier completion, 6 metric cards (price records, securities, tracked, data span, session, loaded). Target framework updated to `net8.0-windows10.0.19041` for LiveChartsCore.SkiaSharpView.WPF compatibility. |
| 2.24 | 2026-01-27 | **Gap Query Performance Fix:** Fixed Cloudflare 524 timeout when calling `/api/admin/prices/gaps?includeUntracked=true`. Root cause was expensive `NOT EXISTS` subquery checking 29K securities against 5M+ prices (O(n*m)). Fix: Pre-compute `SecuritiesWithPrices` CTE via `GROUP BY` (single table scan), replace `NOT EXISTS` with `LEFT JOIN + IS NULL` (O(n)). Added third CTE `StaleData` to detect securities with outdated prices (>30 days since last update). Same optimization applied to gap count summary query. |
| 2.23 | 2026-01-26 | **EODHD Unavailable Security Tracking:** Added `IsEodhdUnavailable` column to SecurityMaster via EF Core migration. Securities where EODHD has no data (typically OTC/Pink Sheet) are automatically detected and marked by the crawler. Gap detection queries filter out unavailable securities to prevent endless retry loops. New `POST /api/admin/prices/mark-eodhd-unavailable/{securityAlias}` endpoint. Crawler detects 10 consecutive dates with 0 records and marks security as unavailable (threshold accounts for extended market closures like 9/11). |
| 2.22 | 2026-01-26 | **Gap Count Query Fix:** The `/api/admin/prices/gaps` endpoint was incorrectly counting `trackedWithGaps` and `untrackedWithGaps` from the LIMITED results (TOP N) instead of actual totals. Added separate count query to compute true totals of all securities with gaps. This fix ensures the crawler UI displays accurate counts even when more gaps exist than the limit parameter. |
| 2.21 | 2026-01-26 | **Gap Detection Includes Zero-Data Securities:** `/api/admin/prices/gaps` endpoint now uses UNION query to include both (1) securities with internal gaps in existing price data, and (2) untracked securities with zero price records. Securities with no data use 2-year lookback window for expected date range. `/api/admin/prices/gaps/{securityAlias}` endpoint updated to return business days for securities with no prices (2-year range). Enables crawler to populate historical data for previously untouched securities. |
| 2.20 | 2026-01-26 | **Two-Phase Gap Detection for Crawler:** `/api/admin/prices/gaps` endpoint now supports `includeUntracked` parameter. Default behavior (false) returns only tracked securities. When true, returns all active securities with tracked ones prioritized first. Response includes `isTracked` flag per security and separate counts (`trackedWithGaps`, `untrackedWithGaps`). EODHD Loader (Boris) crawler updated with two-phase operation: Phase 1 fills tracked securities, then automatically switches to Phase 2 for untracked securities. UI shows current phase indicator. |
| 2.19 | 2026-01-25 | **Local Jenkins CI Integration:** Pre-push hook (`helpers/hooks/jenkins_pre_push.py`) triggers local Jenkins build before every `git push`. Blocks push if build fails. Helper scripts: `jenkins-local.ps1` (start/stop/build/status), `jenkins-console.ps1` (fetch logs), `jenkins-reload.ps1` (reload config). Jenkinsfile updated with correct paths (`projects/stock-analyzer/`) and JavaScript test stage. Jenkins API token authentication via `.env` credentials. Pre-commit config extended with pre-push stage. |
| 2.15 | 2026-01-22 | **Sentiment-Filtered News:** SentimentAnalyzer.cs static utility with keyword-based sentiment analysis (~50 positive/negative keywords). NewsService.GetNewsForDateWithSentimentAsync() filters headlines to match price direction. AnalysisService.DetectSignificantMovesAsync() uses sentiment-aware news. Fallback cascade: sentiment-matched company news → any company news → market news. SentimentAnalyzerTests.cs (32 tests) covers positive/negative/neutral headlines, price matching, real-world Ford "Soars" bug scenario. |
| 2.18 | 2026-01-24 | **Database Protection & Coverage API:** Bicep template modified to NOT manage the production database (prevents overwriting 3.5M+ price records from BACPAC import). Database name corrected from `stockanalyzerdb` to `stockanalyzer-db`. New `/api/admin/prices/coverage-dates` endpoint returns distinct dates with price data for coverage analysis. `GetDistinctDatesAsync()` method added to IPriceRepository. SecurityMaster extended with Country, Currency, ISIN columns via EF Core migration. EodhdService extended with `GetExchangeSymbolsAsync()` method to sync EODHD symbol metadata. |
| 2.17 | 2026-01-23 | **Sentiment-Filtered News & Production Deployment:** Deployed v3.0 to production with database-first price lookup. Fixed image prefetch thread exhaustion (reduced initial load from 50 to 5). |
| 2.14 | 2026-01-22 | **Weighted Relevance Search:** symbolSearch.js updated with scoreMatch() method and popularTickers map. Scoring: exact ticker (1000) > ticker prefix (200) > word start (100) > substring (25). Popularity boost (+10 to +50) for ~100 well-known tickers (AAPL, F, SPY, etc.). Results sorted by score descending. Ford Motor Co (F) now surfaces above "Bedford" substring matches when searching "ford". |
| 2.13 | 2026-01-22 | **Stochastic Oscillator:** New StochasticData record in TechnicalIndicators.cs, CalculateStochastic() method in AnalysisService (K=14, D=3 parameters). API /analysis endpoint includes stochastic array. Frontend Stochastic checkbox, charts.js subplot with %K (teal) and %D (orange dashed) lines, overbought/oversold zones at 80/20. 5 new unit tests for CalculateStochastic. |
| 2.12 | 2026-01-22 | **Client-Side Instant Search:** Symbol data loaded to browser at page load for sub-millisecond search. New symbolSearch.js module fetches /data/symbols.txt (~857KB, ~315KB gzipped) containing ~30K US symbols in pipe-delimited format. SymbolCache.GenerateClientFile() generates static file at startup and after daily Finnhub refresh. Search ranking: exact match > prefix match > description contains. 5-second debounced server fallback for unknown symbols. API.search() uses client-side first, api.js searchServerFallback() method for fallback. Offline-capable once symbols loaded. |
| 2.11 | 2026-01-22 | **Full-Text Search for Fast Symbol Lookup:** Added SQL Server Full-Text Search to achieve sub-10ms search latency on 30K+ symbols. EF Core migration creates FULLTEXT CATALOG and INDEX on Description column. SqlSymbolRepository uses CONTAINS() predicate for production (SQL Server), with automatic fallback to LINQ for testing (InMemory) or SQL Server Express without FTS installed. Provider detection via `IsSqlServer()`, error handling for FTS unavailability (SQL Error 7601/7609). |
| 2.10 | 2026-01-22 | **Persistent Image Cache:** Database-backed image cache replaces in-memory ConcurrentQueue for persistence across restarts. CachedImageEntity model with EF Core migration, ICachedImageRepository interface with SqlCachedImageRepository (random selection via `ORDER BY NEWID()`). ImageCacheService refactored to use IServiceScopeFactory for scoped DbContext access. Cache increased to 1000 images per type. Status page fixed: dynamic maxSize from API, added TwelveData/FMP health check cards. |
| 2.9 | 2026-01-21 | **Local Symbol Database for Fast Search:** Sub-10ms ticker search via Azure SQL cache of ~10K US stock symbols. SymbolEntity model with EF Core migration, ISymbolRepository interface with SqlSymbolRepository implementation (multi-tier ranking: exact > prefix > contains), SymbolRefreshService BackgroundService (daily Finnhub sync at 2 AM UTC, auto-seed on startup if empty), AggregatedStockDataService now queries local DB first with API fallback. Admin endpoints: POST /api/admin/symbols/refresh, GET /api/admin/symbols/status. 18 new unit tests for repository. |
| 2.8 | 2026-01-21 | **Log Injection Prevention:** Added LogSanitizer utility to sanitize user input (ticker symbols, search queries) before logging. Prevents log forging attacks via control character injection. Applied to AggregatedStockDataService, TwelveDataService, FmpService, and YahooFinanceService. Resolves 21 CodeQL security alerts. |
| 2.7 | 2026-01-21 | **GitHub Pages Documentation Refactor:** Documentation now served from GitHub Pages (psford.github.io/claudeProjects/) instead of bundled in container. Enables doc updates without container rebuild. Removed wwwroot/docs folder and MSBuild copy targets. docs.html fetches markdown client-side via CORS. Custom domain SSL (psfordtest.com) with Azure managed certificates. |
| 2.6 | 2026-01-19 | **Multi-Source News Aggregation + ML Scoring:** MarketauxService (alternative news source), HeadlineRelevanceService (weighted relevance scoring: ticker 35%, company name 25%, recency 20%, sentiment 10%, source quality 10%), AggregatedNewsService (combines sources with Jaccard deduplication), NewsItem model extended (RelevanceScore, SourceApi fields), new aggregated news endpoints, ImageProcessingService quality control (0.50 confidence threshold, 20% minimum detection size, reject images without valid detection), image cache increased to 100/30, 52 new unit tests |
| 2.5 | 2026-01-19 | **Security Hardening:** CORS restricted to known origins, HSTS header, ticker input validation (regex pattern), removed unused DirectoryBrowser |
| 2.4 | 2026-01-19 | **App Service Migration + Key Vault:** Migrated from ACI to App Service B1 for zero-downtime deploys, Azure Key Vault for secrets management, manual workflow_dispatch for production deploys, GitHub repo made public for CodeQL, GitHub link added to footer |
| 2.3 | 2026-01-18 | **Privacy-First Watchlists + Frontend Testing:** LocalStorage watchlist storage (storage.js) for privacy-first client-side persistence, export/import JSON functionality, Jest unit tests for portfolio aggregation functions (25 tests), CI/CD pipeline updated with frontend-tests job (Node.js 20.x), Escape key handler for modal dismissal, API data format fix (data vs prices array handling) |
| 2.2 | 2026-01-18 | **GitHub Pages Docs:** docs.psfordtaurus.com for documentation hosting, docs-deploy.yml workflow for auto-sync, "Latest Docs" link in app header, docs update without Docker rebuild |
| 2.1 | 2026-01-18 | **Custom Domain:** psfordtaurus.com with Cloudflare free SSL, ACI updated to port 80, flarectl CLI for DNS management |
| 2.0 | 2026-01-18 | **Production Azure Deployment:** ACI + Azure SQL in West US 2, GitHub Actions CI/CD with ACR push, EF Core migrations auto-applied |
| 1.19 | 2026-01-18 | Azure deployment: EF Core with SqlWatchlistRepository, Azure Bicep IaC (main.bicep), GitHub Actions azure-deploy.yml, DEPLOYMENT_AZURE.md guide, automatic migrations on startup |
| 1.18 | 2026-01-17 | Combined Watchlist View: TickerHolding/CombinedPortfolioResult models, UpdateHoldingsAsync/GetCombinedPortfolioAsync in WatchlistService, ±5% significant move markers with toggle, portfolio chart aggregation (equal/shares/dollars weighting), benchmark comparison (SPY/QQQ), market news API, Wikipedia-style hover cards with cat/dog toggle, holdings editor modal |
| 1.17 | 2026-01-17 | Watchlist feature: Watchlist model, IWatchlistRepository interface, JsonWatchlistRepository, WatchlistService, 8 new API endpoints, watchlist sidebar UI, multi-user ready (UserId field) |
| 1.16 | 2026-01-17 | Status dashboard (/status.html), .NET security analyzers (NetAnalyzers, Roslynator), OWASP Dependency Check, Dependabot config |
| 1.15 | 2026-01-17 | Observability: Serilog structured logging with file/console output, ASP.NET Core health checks (/health, /health/live, /health/ready) |
| 1.14 | 2026-01-17 | CI/CD security: CodeQL workflow (.github/workflows/codeql.yml), security toolchain documentation, CI_CD_SECURITY_PLAN.md |
| 1.13 | 2026-01-17 | CI/CD pipelines: GitHub Actions workflow (.github/workflows/dotnet-ci.yml), Jenkins pipeline (Jenkinsfile), Section 9.4 documentation |
| 1.12 | 2026-01-17 | Bollinger Bands: BollingerData model, CalculateBollingerBands method (20-period SMA, 2 std dev), overlaid on price chart with shaded fill |
| 1.11 | 2026-01-17 | Documentation search: Fuse.js fuzzy search across all documents (threshold 0.4), search results dropdown with highlighting, keyboard navigation. Scroll spy: TOC highlighting tracks current section using scroll events with requestAnimationFrame throttling |
| 1.10 | 2026-01-17 | Architecture visualization: Mermaid.js diagrams loaded from external .mmd files (hybrid auto/manual approach), MIME type config for .mmd files, MSBuild target for diagrams directory |
| 1.9 | 2026-01-17 | Documentation page: docs.html with tabbed markdown viewer, marked.js integration, TOC sidebar |
| 1.8 | 2026-01-17 | Stock comparison: normalizeToPercentChange helper, comparison mode in charts.js, benchmark buttons, indicator disable logic |
| 1.7 | 2026-01-16 | Technical indicators: RSI and MACD calculation methods, RsiData/MacdData models, Plotly subplot support, dynamic chart resizing |
| 1.6 | 2026-01-16 | Dark mode implementation with Tailwind CSS class-based theming, localStorage persistence |
| 1.5 | 2026-01-16 | Added YTD and 10-year time periods to chart dropdown |
| 1.4 | 2026-01-16 | Company profile integration: ISIN/CUSIP/SEDOL identifiers, company bio, Finnhub profile endpoint, OpenFIGI SEDOL lookup, chart legend/width fixes |
| 1.3 | 2026-01-16 | Server-side ML image processing with YOLOv8n ONNX, ImageProcessingService, ImageCacheService, new /api/images/* endpoints |
| 1.2 | 2026-01-16 | Added unit test documentation (Section 8), SRI for Plotly.js (Section 12.5) |
| 1.1 | 2026-01-16 | Added image caching system, Dog CEO API, CSP configuration |
| 1.0 | 2026-01-16 | Initial .NET technical specification |

---

## 14.1 Bluesky Feed Filter (separate repo)

Moved to its own repository: [psford/bsky-feed-filter](https://github.com/psford/bsky-feed-filter)

Custom Bluesky feed generator that filters out self-reposts of recent posts. Deployed on Synology NAS via Docker + Cloudflare Tunnel.

---

## 15. References

- [ASP.NET Core Minimal APIs](https://docs.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis)
- [OoplesFinance.YahooFinanceAPI](https://github.com/ooples/OoplesFinance.YahooFinanceAPI)
- [Plotly.js Documentation](https://plotly.com/javascript/)
- [Tailwind CSS Documentation](https://tailwindcss.com/docs)
- [Finnhub API Documentation](https://finnhub.io/docs/api)
- [Dog CEO API Documentation](https://dog.ceo/dog-api/documentation/)
- [Cat as a Service (cataas)](https://cataas.com/)
- [OpenFIGI API Documentation](https://www.openfigi.com/api)
