# Technical Specification: Stock Analyzer Dashboard (.NET)

**Version:** 2.7
**Last Updated:** 2026-01-20
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
│  │               │  │ - /api/trending│  │                        │ │
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
| `/api/stock/{ticker}/history` | GET | Historical OHLCV data | `ticker`, `period` (optional, default: 1y) |
| `/api/stock/{ticker}/news` | GET | Company news | `ticker`, `days` (optional, default: 30) |
| `/api/stock/{ticker}/significant` | GET | Significant price moves | `ticker`, `threshold` (optional, default: 3.0) |
| `/api/stock/{ticker}/analysis` | GET | Performance metrics + MAs | `ticker`, `period` (optional) |
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
    public string? Exchange { get; init; }

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
    public string? Exchange { get; init; }
    public string? Type { get; init; }
    public string DisplayName => $"{Symbol} - {ShortName}" +
        (Exchange != null ? $" ({Exchange})" : "");
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

### 4.8 CompanyProfile

```csharp
public record CompanyProfile
{
    public required string Symbol { get; init; }
    public string? Name { get; init; }
    public string? Country { get; init; }
    public string? Currency { get; init; }
    public string? Exchange { get; init; }
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

### 4.9 TickerHolding

```csharp
public record TickerHolding
{
    public required string Ticker { get; init; }
    public decimal? Shares { get; init; }      // Number of shares (null if using dollar mode)
    public decimal? DollarValue { get; init; } // Dollar amount (null if using shares mode)
}
```

### 4.10 CombinedPortfolioResult

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
| Ticker Mention | 35% | Ticker symbol appears in headline/summary |
| Company Name | 25% | Company name appears in text |
| Recency | 20% | Exponential decay (24hr half-life) |
| Sentiment Data | 10% | Having sentiment indicates better coverage |
| Source Quality | 10% | Premium sources (Reuters, Bloomberg, CNBC, etc.) |

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

Implements `BackgroundService` for continuous cache maintenance.

| Method | Description |
|--------|-------------|
| `GetCatImage()` | Dequeue processed cat image from cache |
| `GetDogImage()` | Dequeue processed dog image from cache |
| `GetCacheStatus()` | Return (cats, dogs) count tuple |
| `ExecuteAsync(token)` | Background loop monitoring cache levels |

**Cache Configuration:**
- **Cache Size:** 100 images per type (configurable)
- **Refill Threshold:** 30 images (triggers background refill)
- **Storage:** `ConcurrentQueue<byte[]>` for thread-safe access
- **Refill Delay:** 500ms between cache checks

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
│   ├── storage.js      # LocalStorage watchlist persistence
│   └── watchlist.js    # Watchlist sidebar and combined view
├── tests/              # Frontend JavaScript unit tests
│   └── portfolio.test.js  # Portfolio aggregation tests
└── package.json        # Jest test configuration
```

### 6.1.1 Documentation Page (docs.html)

The documentation page provides four tabs:
- **Project Guidelines** - CLAUDE.md with project rules and best practices
- **Functional Spec** - User-facing feature documentation
- **Technical Spec** - Developer documentation
- **Architecture** - Interactive Mermaid.js diagrams loaded from .mmd files

### 6.1.2 Architecture Diagrams (Hybrid Approach)

Diagrams are stored as separate `.mmd` files in `wwwroot/docs/diagrams/` for maintainability:

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
1. Edit the corresponding `.mmd` file in `wwwroot/docs/diagrams/`
2. Mermaid syntax documentation: https://mermaid.js.org/
3. Changes are live-reloaded (no build required for static files)

**Auto-generation (optional):**
To regenerate `project-structure.mmd` from the solution:
```bash
dotnet tool install -g mermaid-graph
dotnet mermaid-graph --sln . --output src/StockAnalyzer.Api/wwwroot/docs/diagrams/project-structure.mmd --direction TD
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
    getSignificantMoves(ticker, threshold) { ... },
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

**storage.js** - LocalStorage watchlist persistence:
- Privacy-first client-side storage (no PII sent to server)
- CRUD operations on localStorage
- Export watchlists to JSON file
- Import watchlists from JSON file
- Storage usage tracking

**watchlist.js** - Watchlist sidebar and combined view:
- Watchlist CRUD operations
- Ticker management (add/remove)
- Combined view toggle and state
- Holdings editor modal
- Portfolio chart rendering
- ±5% significant move markers
- Hover cards with market news
- Escape key handler for modal dismissal

### 6.3 Dark Mode Implementation

The application supports light and dark color themes via Tailwind CSS class-based dark mode.

**Configuration:**
```javascript
// tailwind.config in index.html
tailwind.config = {
    darkMode: 'class',  // Enable class-based dark mode
    ...
}
```

**Implementation Details:**

| Component | Implementation |
|-----------|---------------|
| Toggle Button | Sun/moon icons in header, click handler toggles `dark` class on `<html>` |
| Persistence | localStorage key `darkMode` stores `'true'` or `'false'` |
| System Preference | `window.matchMedia('(prefers-color-scheme: dark)')` for initial state |
| Static Elements | Tailwind `dark:` prefix classes (e.g., `dark:bg-gray-800 dark:text-white`) |
| Dynamic Elements | JavaScript renders `dark:` classes in template strings |
| Plotly Charts | `Charts.getThemeColors()` returns colors based on `document.documentElement.classList.contains('dark')` |

**Initialization Flow:**
```
Page Load → initDarkMode()
                ↓
    Check localStorage('darkMode')
                ↓
    If null → Check system preference
                ↓
    Apply 'dark' class to <html> if needed
                ↓
    Update icon visibility (sun/moon)
```

**Theme Toggle Flow:**
```
User clicks toggle → Toggle 'dark' class on <html>
                          ↓
                   Save to localStorage
                          ↓
                   Update icons
                          ↓
                   If chart exists → Re-render with new theme colors
```

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
| AnalysisService | 14 | Moving averages, significant moves, performance, RSI, MACD calculations |
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
│       │   ├── DesignTimeDbContextFactory.cs
│       │   ├── Entities/
│       │   │   └── WatchlistEntity.cs       # EF Core entities
│       │   └── Migrations/                   # EF Core migrations
│       ├── Models/
│       │   ├── StockInfo.cs
│       │   ├── CompanyProfile.cs
│       │   ├── HistoricalData.cs
│       │   ├── NewsItem.cs
│       │   ├── SearchResult.cs
│       │   ├── SignificantMove.cs
│       │   ├── Watchlist.cs
│       │   └── TechnicalIndicators.cs    # RsiData, MacdData records
│       └── Services/
│           ├── StockDataService.cs
│           ├── NewsService.cs
│           ├── AnalysisService.cs
│           ├── WatchlistService.cs
│           ├── IWatchlistRepository.cs
│           ├── JsonWatchlistRepository.cs   # Local file storage
│           ├── ImageProcessingService.cs   # ML detection + cropping
│           └── ImageCacheService.cs        # Background cache management
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

**Pipeline Stages:**
```groovy
Checkout → Restore → Build → Test → Publish
```

**Docker Agent:** `mcr.microsoft.com/dotnet/sdk:8.0`

**Starting Jenkins:**
```bash
docker run -d --name jenkins \
  -p 8080:8080 -p 50000:50000 \
  -v jenkins_home:/var/jenkins_home \
  -v /var/run/docker.sock:/var/run/docker.sock \
  jenkins/jenkins:lts

# Install Docker CLI and fix permissions
docker exec -u root jenkins apt-get update && apt-get install -y docker.io
docker exec -u root jenkins chmod 666 /var/run/docker.sock
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
| Dependabot | Dependency scanning | GitHub (enabled) |

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
- Azure SQL Server + Database (Basic tier, 5 DTU)
- Azure Key Vault (secrets management)

#### CI/CD Pipeline

**Workflow:** `.github/workflows/azure-deploy.yml`

**Triggers:**
- Manual only via `workflow_dispatch` (production deploys require confirmation)

**Jobs:**
1. `preflight` - Validate confirmation, test Azure/ACR credentials
2. `build-and-test` - Build .NET solution, run tests
3. `build-container` - Build Docker image, push to ACR with `prod-{run_number}` tag
4. `deploy` - Update App Service container, restart, health check

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

| Resource | SRI Status | Reason |
|----------|------------|--------|
| Plotly.js 2.27.0 | ✅ Enabled | Static versioned file |
| Tailwind CSS CDN | ❌ Not applicable | Dynamic JIT compiler |

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

Frontend fetches all data in parallel for better performance:
```javascript
const [stockInfo, history, analysis, significantMoves, news] = await Promise.all([
    API.getStockInfo(ticker),
    API.getHistory(ticker, period),
    API.getAnalysis(ticker, period),
    API.getSignificantMoves(ticker, 3),
    API.getNews(ticker, 30)
]);
```

---

## 14. Version History

| Version | Date | Changes |
|---------|------|---------|
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

## 15. References

- [ASP.NET Core Minimal APIs](https://docs.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis)
- [OoplesFinance.YahooFinanceAPI](https://github.com/ooples/OoplesFinance.YahooFinanceAPI)
- [Plotly.js Documentation](https://plotly.com/javascript/)
- [Tailwind CSS Documentation](https://tailwindcss.com/docs)
- [Finnhub API Documentation](https://finnhub.io/docs/api)
- [Dog CEO API Documentation](https://dog.ceo/dog-api/documentation/)
- [Cat as a Service (cataas)](https://cataas.com/)
- [OpenFIGI API Documentation](https://www.openfigi.com/api)
