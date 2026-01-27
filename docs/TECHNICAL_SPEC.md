# Technical Specification: Stock Analyzer Dashboard (.NET)

**Version:** 2.27
**Last Updated:** 2026-01-27
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
| `/api/stock/{ticker}/significant` | GET | Significant price moves (no news) | `ticker`, `threshold` (optional, default: 3.0), `period` (optional) |
| `/api/stock/{ticker}/news/move` | GET | News for specific move (lazy-loaded) | `ticker`, `date`, `change`, `limit` (optional, default: 5) |
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
public async Task<List<NewsItem>> GetNewsForDateWithSentimentAsync(
    string symbol, DateTime date, decimal priceChangePercent, int maxArticles = 5)
{
    // 1. Fetch company news
    // 2. Score each headline with SentimentAnalyzer
    // 3. Filter to articles with matchScore > 25
    // 4. Fallback: no match → general market news
}
```

**Fallback Cascade:**

| Priority | Condition | Source |
|----------|-----------|--------|
| 1 | Sentiment-matched company news exists | Company news (filtered) |
| 2 | No sentiment match | General market news (e.g., "S&P 500 rallies") |

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

**SecurityMaster Table:**
```sql
CREATE TABLE data.SecurityMaster (
    SecurityAlias INT IDENTITY(1,1) PRIMARY KEY,  -- Auto-increment for efficient joins
    PrimaryAssetId NVARCHAR(50),                   -- Future: CUSIP, ISIN, etc.
    IssueName NVARCHAR(200) NOT NULL,              -- Full name (e.g., "Apple Inc.")
    TickerSymbol NVARCHAR(20) NOT NULL,            -- Ticker (e.g., "AAPL")
    Exchange NVARCHAR(50),                          -- Exchange (e.g., "NASDAQ")
    SecurityType NVARCHAR(50),                      -- Common Stock, ETF, ADR, etc.
    Country NVARCHAR(10),                           -- Country code (e.g., "USA")
    Currency NVARCHAR(10),                          -- Currency (e.g., "USD")
    Isin NVARCHAR(20),                              -- International Securities ID Number
    IsActive BIT DEFAULT 1,                         -- Whether actively traded
    IsTracked BIT DEFAULT 0,                        -- Whether in tracked universe for gap-filling
    IsEodhdUnavailable BIT DEFAULT 0,               -- Whether EODHD has no data for this security
    ImportanceScore INT DEFAULT 5,                  -- Calculated importance (1-10, 10=most important)
    CreatedAt DATETIME2 DEFAULT GETUTCDATE(),
    UpdatedAt DATETIME2 DEFAULT GETUTCDATE()
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
- `UpsertManyAsync(securities)` - Batch upsert for data loading (500-row batches)
- `GetPricesAsync(alias, startDate, endDate)` - Date range query
- `BulkInsertAsync(prices)` - High-performance insert (1000-row batches with progress logging)
- `GetLatestPricesAsync(aliases)` - Batch latest price lookup
- `GetDistinctDatesAsync(startDate, endDate)` - Get all dates with price data (for coverage analysis)

**Files:**
- `Data/Entities/SecurityMasterEntity.cs` - Security master entity
- `Data/Entities/PriceEntity.cs` - Price entity
- `Services/ISecurityMasterRepository.cs` - Interface + DTOs
- `Services/IPriceRepository.cs` - Interface + DTOs
- `Data/SqlSecurityMasterRepository.cs` - SQL implementation
- `Data/SqlPriceRepository.cs` - SQL implementation
- `scripts/001_CreateDataSchema.sql` - Schema creation script
- `scripts/002_AddSecurityMasterAndPrices.sql` - Migration script

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
| `POST /api/admin/prices/bulk-load` | Start bulk historical load (body: `{StartDate, EndDate}`) |
| `POST /api/admin/securities/calculate-importance` | Calculate importance scores for all active securities |
| `POST /api/admin/securities/promote-untracked` | Promote untracked securities to tracked (query: `count`, default 100, max 500) |
| `POST /api/admin/securities/reset-unavailable` | Reset IsEodhdUnavailable flag (query: `days`, `all`) |
| `GET /api/admin/dashboard/stats` | Consolidated dashboard stats: universe, prices, tiers, decade/year coverage |
| `GET /api/admin/dashboard/heatmap` | Bivariate heatmap data: Year x ImportanceScore with tracked/untracked split |
| `POST /api/admin/dashboard/refresh-summary` | Refresh the CoverageSummary pre-aggregation table |

**Dashboard Stats (`/api/admin/dashboard/stats`):**
- Consolidated endpoint returning all dashboard metrics
- Result sets 1-3 use direct SQL (SecurityMaster counts, Prices aggregate, tier distribution)
- Result sets 4-5 (decade/year coverage) read from `data.CoverageSummary` table (instant)
- **Caching:** `IMemoryCache` with 10-minute TTL (key: `dashboard:stats`)
- Used by EODHD Loader's Data Command Center UI
- Returns: universe counts (total/tracked/untracked/unavailable), price record stats (total/distinct/oldest/latest), ImportanceScore tier distribution with completion status, coverage by decade, coverage by year
- **Performance:** 60s query timeout for direct SQL; summary table reads are instant
- **Response structure:** `{ success, timestamp, universe, prices, importanceTiers[], coverageByDecade[], coverageByYear[] }`

**Heatmap Data (`/api/admin/dashboard/heatmap`):**
- Reads from pre-aggregated `data.CoverageSummary` table (instant response, even on Azure SQL Basic tier)
- **Caching:** `IMemoryCache` with 30-minute TTL (key: `dashboard:heatmap`)
- If summary table is empty, returns `stale: true` with guidance to call `refresh-summary`
- Used by EODHD Loader's bivariate coverage heatmap (SkiaSharp custom control)
- Response: `{ success, cells[{ year, score, trackedRecords, untrackedRecords, trackedSecurities, untrackedSecurities }], metadata{ minYear, maxYear, totalCells, maxTrackedRecords, maxUntrackedRecords } }`
- ~230 cells (47 years × ~5 populated scores)

**Refresh Summary (`/api/admin/dashboard/refresh-summary`):**
- Runs the expensive aggregation query on `data.Prices` × `data.SecurityMaster` and writes results to `data.CoverageSummary`
- **5-minute timeout** — designed for infrequent execution (post-deploy, post-crawl)
- SQL: `GROUP BY YEAR(p.EffectiveDate), sm.ImportanceScore` with tracked/untracked splits, COUNT(DISTINCT) for securities, COUNT(DISTINCT EffectiveDate) for trading days
- Invalidates both `dashboard:heatmap` and `dashboard:stats` cache keys on completion
- Returns: `{ success, message, cellCount }`
- **When to call:** After deployment, after running calculate-importance, after crawl sessions

**CoverageSummary Table (`data.CoverageSummary`):**
- Pre-aggregated Year × ImportanceScore grid (~500 rows max: ~50 years × 10 scores)
- Columns: Id (PK), Year, ImportanceScore, TrackedRecords, UntrackedRecords, TrackedSecurities, UntrackedSecurities, TradingDays, LastUpdatedAt
- Unique index on `(Year, ImportanceScore)` — each cell has exactly one row
- **Purpose:** Avoids expensive full-table scans on the 3.5M+ row Prices table; critical for Azure SQL Basic tier (5 DTU)
- Populated by `POST /api/admin/dashboard/refresh-summary`; consumed by heatmap and stats endpoints

**Reset Unavailable (`/api/admin/securities/reset-unavailable`):**
- Resets `IsEodhdUnavailable = false` for securities incorrectly marked unavailable
- Default: Reset securities marked in last 7 days (by `UpdatedAt`); `?days=N` to customize
- `?all`: Reset ALL unavailable active securities
- Returns list of reset securities with ticker and exchange

**Gap Detection (`/api/admin/prices/gaps`):**
- Returns only tracked securities (`IsTracked = 1`) with price gaps
- Response includes `isTracked` flag and `importanceScore` per security, plus summary counts
- Used by EODHD Loader crawler for single-loop gap filling with batch promotion
- **Ordering:** Priority → ImportanceScore DESC → SecurityType → TickerLength → MissingDays DESC
- **Date capping:** `LastDate` is capped at `GETDATE()` to exclude future price data; `ActualPriceCount` excludes future dates
- **Query Structure (2 sources combined via UNION ALL):**
  1. **TrackedWithGaps:** Tracked securities with existing prices that have internal gaps (expected > actual in date range). Pre-computes `SecuritiesWithPrices` CTE using `GROUP BY` for O(n) scan
  2. **TrackedNoPrices:** Tracked securities with zero price records (uses `NOT EXISTS` index seek on `Prices(SecurityAlias)`, efficient since tracked securities are a small subset)
- **Separate gap count query:** Computes true totals independently of LIMIT parameter

**Promote Untracked (`/api/admin/securities/promote-untracked`):**
- Selects top N untracked securities ordered by `ImportanceScore DESC`, then by `TickerSymbol`
- For each: inserts into `data.TrackedSecurities` (source: `auto-promote`) and sets `IsTracked = 1`
- Uses temp table (`#PromoteAliases`) for parameterized SQL (avoids CA2100 SQL injection warnings)
- Returns: `{ success, promoted, tickers[], message }`
- Count range: 1-500 (default: 100, clamped via `Math.Clamp`)
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
- Scoring algorithm (base score: 5):
  - **Security Type:** Common Stock +2, ETF +1, Preferred/Warrant/Right -2, OTC indicators -3
  - **Exchange:** NYSE/NASDAQ +2, ARCA/BATS +1, OTC/PINK/GREY -2, Unknown -1
  - **Ticker Length:** 1-3 chars +1, 5+ chars -1
  - **Name Patterns:** Inc/Corp/Ltd +1, Warrant/Right/Unit -2, Liquidating/Bankrupt -3
- Run on-demand after adding new securities; scores are persisted in SecurityMaster.ImportanceScore

**Bulk Load Flow:**
1. Call `/api/admin/prices/sync-securities` to populate SecurityMaster
2. Call `/api/admin/prices/bulk-load` with date range
3. Service runs in background, logs progress every 100 tickers
4. Check `/api/admin/prices/status` for completion

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

## 15. References

- [ASP.NET Core Minimal APIs](https://docs.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis)
- [OoplesFinance.YahooFinanceAPI](https://github.com/ooples/OoplesFinance.YahooFinanceAPI)
- [Plotly.js Documentation](https://plotly.com/javascript/)
- [Tailwind CSS Documentation](https://tailwindcss.com/docs)
- [Finnhub API Documentation](https://finnhub.io/docs/api)
- [Dog CEO API Documentation](https://dog.ceo/dog-api/documentation/)
- [Cat as a Service (cataas)](https://cataas.com/)
- [OpenFIGI API Documentation](https://www.openfigi.com/api)
