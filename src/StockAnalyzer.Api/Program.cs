using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Events;
using StockAnalyzer.Core.Data;
using StockAnalyzer.Core.Data.Entities;
using StockAnalyzer.Core.Models;
using StockAnalyzer.Core.Services;

// Configure Serilog before building the app
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "StockAnalyzer")
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}")
    .WriteTo.File(
        path: "logs/stockanalyzer-.log",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 7,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}")
    .CreateLogger();

try
{
    Log.Information("Starting Stock Analyzer API");

var builder = WebApplication.CreateBuilder(args);

// Use Serilog for logging
builder.Host.UseSerilog();

// Add services to the container
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddMemoryCache();

// Configure CORS for frontend - restrict to known origins
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins(
                "https://psfordtaurus.com",
                "https://www.psfordtaurus.com",
                "http://localhost:5000",
                "https://localhost:5001")
              .WithMethods("GET", "POST", "PUT", "DELETE", "OPTIONS")
              .WithHeaders("Content-Type", "Authorization", "Accept");
    });
});

// Register stock data providers (multi-provider with fallback)
// Priority: TwelveData (1) → FMP (2) → Yahoo (3)
builder.Services.AddSingleton<IStockDataProvider>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var logger = sp.GetRequiredService<ILogger<TwelveDataService>>();
    var apiKey = config["StockDataProviders:TwelveData:ApiKey"]
              ?? Environment.GetEnvironmentVariable("TWELVEDATA_API_KEY")
              ?? "";
    return new TwelveDataService(apiKey, logger);
});
builder.Services.AddSingleton<IStockDataProvider>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var logger = sp.GetRequiredService<ILogger<FmpService>>();
    var apiKey = config["StockDataProviders:FMP:ApiKey"]
              ?? Environment.GetEnvironmentVariable("FMP_API_KEY")
              ?? "";
    return new FmpService(apiKey, logger);
});
builder.Services.AddSingleton<IStockDataProvider>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<YahooFinanceService>>();
    return new YahooFinanceService(logger);
});
// AggregatedStockDataService with optional symbol repository for local search
// Uses IServiceScopeFactory to create scopes for the scoped ISymbolRepository
builder.Services.AddSingleton<AggregatedStockDataService>(sp =>
{
    var providers = sp.GetServices<IStockDataProvider>();
    var cache = sp.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>();
    var logger = sp.GetRequiredService<ILogger<AggregatedStockDataService>>();
    var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
    return new AggregatedStockDataService(providers, cache, logger, scopeFactory);
});

// Keep StockDataService for backward compatibility (deprecated, use AggregatedStockDataService)
builder.Services.AddSingleton<StockDataService>();

// Register news services
builder.Services.AddSingleton(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var apiKey = config["Finnhub:ApiKey"] ?? Environment.GetEnvironmentVariable("FINNHUB_API_KEY") ?? "";
    return new NewsService(apiKey);
});
builder.Services.AddSingleton(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var apiToken = config["Marketaux:ApiToken"] ?? Environment.GetEnvironmentVariable("MARKETAUX_API_TOKEN") ?? "";
    return new MarketauxService(apiToken);
});
builder.Services.AddSingleton<HeadlineRelevanceService>();
builder.Services.AddSingleton(sp =>
{
    var finnhubService = sp.GetRequiredService<NewsService>();
    var marketauxService = sp.GetRequiredService<MarketauxService>();
    var relevanceService = sp.GetRequiredService<HeadlineRelevanceService>();
    return new AggregatedNewsService(finnhubService, marketauxService, relevanceService);
});
builder.Services.AddSingleton(sp =>
{
    var newsService = sp.GetRequiredService<NewsService>();
    return new AnalysisService(newsService);
});

// Register watchlist services - use SQL if connection string present, otherwise JSON file
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (!string.IsNullOrEmpty(connectionString))
{
    // Azure SQL / SQL Server mode
    builder.Services.AddDbContext<StockAnalyzerDbContext>(options =>
        options.UseSqlServer(connectionString));
    builder.Services.AddScoped<IWatchlistRepository, SqlWatchlistRepository>();
    builder.Services.AddScoped<WatchlistService>();

    // Symbol database for fast local search
    // SymbolCache is singleton - holds ~30K symbols in memory for sub-millisecond lookups
    builder.Services.AddSingleton<SymbolCache>();
    builder.Services.AddScoped<ISymbolRepository, SqlSymbolRepository>();
    // SymbolRefreshService needs wwwroot path for regenerating client-side symbols file
    builder.Services.AddSingleton(sp =>
    {
        var serviceProvider = sp;
        var logger = sp.GetRequiredService<ILogger<SymbolRefreshService>>();
        var config = sp.GetRequiredService<IConfiguration>();
        var cache = sp.GetRequiredService<SymbolCache>();
        var env = sp.GetRequiredService<IWebHostEnvironment>();

        // Add wwwroot path to config for SymbolRefreshService
        var configRoot = config as IConfigurationRoot;
        var memConfig = new Dictionary<string, string?> { ["WebRoot:Path"] = env.WebRootPath };
        var combinedConfig = new ConfigurationBuilder()
            .AddConfiguration(config)
            .AddInMemoryCollection(memConfig)
            .Build();

        return new SymbolRefreshService(serviceProvider, logger, combinedConfig, cache);
    });
    builder.Services.AddHostedService(sp => sp.GetRequiredService<SymbolRefreshService>());

    // Cached image repository for persistent image storage
    builder.Services.AddScoped<ICachedImageRepository>(sp =>
    {
        var context = sp.GetRequiredService<StockAnalyzerDbContext>();
        var logger = sp.GetRequiredService<ILogger<SqlCachedImageRepository>>();
        var config = sp.GetRequiredService<IConfiguration>();
        var cacheSize = config.GetValue<int>("ImageProcessing:CacheSize", 1000);
        return new SqlCachedImageRepository(context, logger, cacheSize);
    });

    // Security master and price repositories (data schema)
    builder.Services.AddScoped<ISecurityMasterRepository, SqlSecurityMasterRepository>();
    builder.Services.AddScoped<IPriceRepository, SqlPriceRepository>();

    Log.Information("Using SQL database for watchlist storage, symbol search, and image cache");
}
else
{
    // Local JSON file mode (development/fallback)
    builder.Services.AddSingleton<IWatchlistRepository>(sp =>
    {
        var config = sp.GetRequiredService<IConfiguration>();
        var storagePath = config["Watchlist:StoragePath"] ?? "data/watchlists.json";
        var logger = sp.GetRequiredService<ILogger<JsonWatchlistRepository>>();
        return new JsonWatchlistRepository(storagePath, logger);
    });
    builder.Services.AddSingleton<WatchlistService>();
    Log.Information("Using JSON file for watchlist storage");
}

// Register image processing services
builder.Services.AddSingleton(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var modelPath = config["ImageProcessing:ModelPath"] ?? "MLModels/yolov8n.onnx";
    var targetWidth = config.GetValue<int>("ImageProcessing:TargetWidth", 320);
    var targetHeight = config.GetValue<int>("ImageProcessing:TargetHeight", 150);
    return new ImageProcessingService(modelPath, targetWidth, targetHeight);
});
builder.Services.AddSingleton(sp =>
{
    var processor = sp.GetRequiredService<ImageProcessingService>();
    var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
    var logger = sp.GetRequiredService<ILogger<ImageCacheService>>();
    var config = sp.GetRequiredService<IConfiguration>();
    var cacheSize = config.GetValue<int>("ImageProcessing:CacheSize", 1000);
    var refillThreshold = config.GetValue<int>("ImageProcessing:RefillThreshold", 100);
    return new ImageCacheService(processor, scopeFactory, logger, cacheSize, refillThreshold);
});
builder.Services.AddHostedService(sp => sp.GetRequiredService<ImageCacheService>());

// Register FinBERT sentiment analysis services (optional - requires model file)
var finbertModelPath = Path.Combine(builder.Environment.WebRootPath ?? "wwwroot", "MLModels", "finbert-onnx", "model.onnx");
if (File.Exists(finbertModelPath))
{
    Log.Information("FinBERT model found - enabling ML-based sentiment analysis");
    builder.Services.AddSingleton(sp =>
    {
        return new FinBertSentimentService(finbertModelPath);
    });
}
else
{
    Log.Information("FinBERT model not found at {Path} - using keyword/VADER ensemble only", finbertModelPath);
    // Don't register FinBertSentimentService - SentimentCacheService will get null from GetService
}

// Sentiment cache repository (only with SQL)
if (!string.IsNullOrEmpty(connectionString))
{
    builder.Services.AddScoped<ISentimentCacheRepository, SqlSentimentCacheRepository>();
}

// Sentiment cache service (background processor for FinBERT)
builder.Services.AddSingleton(sp =>
{
    var finbert = sp.GetService<FinBertSentimentService>();
    var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
    var logger = sp.GetRequiredService<ILogger<SentimentCacheService>>();
    return new SentimentCacheService(finbert, scopeFactory, logger);
});
builder.Services.AddHostedService(sp => sp.GetRequiredService<SentimentCacheService>());

// Register EODHD service for historical price data
builder.Services.AddSingleton(sp =>
{
    var httpClient = new HttpClient();
    var logger = sp.GetRequiredService<ILogger<EodhdService>>();
    var config = sp.GetRequiredService<IConfiguration>();
    return new EodhdService(httpClient, logger, config);
});

// Register PriceRefreshService (background service for daily price updates)
builder.Services.AddSingleton(sp =>
{
    var logger = sp.GetRequiredService<ILogger<PriceRefreshService>>();
    var config = sp.GetRequiredService<IConfiguration>();
    return new PriceRefreshService(sp, logger, config);
});
builder.Services.AddHostedService(sp => sp.GetRequiredService<PriceRefreshService>());

// Add health checks
// External API checks report Degraded (not Unhealthy) when unavailable - app can still function with fallback providers
builder.Services.AddHealthChecks()
    .AddCheck("self", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy("API is running"))
    .AddUrlGroup(new Uri("https://finnhub.io"), name: "finnhub-api", tags: new[] { "external" }, failureStatus: Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Degraded)
    .AddUrlGroup(new Uri("https://api.twelvedata.com"), name: "twelvedata-api", tags: new[] { "external" }, failureStatus: Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Degraded)
    .AddUrlGroup(new Uri("https://financialmodelingprep.com"), name: "fmp-api", tags: new[] { "external" }, failureStatus: Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Degraded)
    .AddUrlGroup(new Uri("https://query1.finance.yahoo.com"), name: "yahoo-finance", tags: new[] { "external" }, failureStatus: Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Degraded);

// Serve static files from wwwroot

var app = builder.Build();

// Apply database migrations automatically in production (Azure)
if (!string.IsNullOrEmpty(connectionString))
{
    var runMigrations = builder.Configuration["RUN_MIGRATIONS"] ?? "false";
    if (runMigrations.Equals("true", StringComparison.OrdinalIgnoreCase) || app.Environment.IsProduction())
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StockAnalyzerDbContext>();
        Log.Information("Applying database migrations...");
        db.Database.Migrate();
        Log.Information("Database migrations applied successfully");
    }
}

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AllowFrontend");

// Security headers middleware
app.Use(async (context, next) =>
{
    // HSTS - force HTTPS for 1 year (only in production)
    if (!app.Environment.IsDevelopment())
    {
        context.Response.Headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
    }
    // Anti-clickjacking
    context.Response.Headers["X-Frame-Options"] = "DENY";
    // Prevent MIME type sniffing
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    // XSS protection (legacy browsers)
    context.Response.Headers["X-XSS-Protection"] = "1; mode=block";
    // Referrer policy
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    // Permissions policy
    context.Response.Headers["Permissions-Policy"] = "accelerometer=(), camera=(), geolocation=(), gyroscope=(), magnetometer=(), microphone=(), payment=(), usb=()";
    // Content Security Policy
    // - Tailwind CSS now built locally (no CDN needed, no 'unsafe-inline' for styles)
    // - Plotly.js requires 'unsafe-eval' and 'unsafe-inline' for chart rendering
    // - marked.js from jsdelivr for markdown rendering in docs
    context.Response.Headers["Content-Security-Policy"] =
        "default-src 'self'; " +
        "script-src 'self' 'unsafe-inline' 'unsafe-eval' https://cdn.plot.ly https://cdn.jsdelivr.net; " +
        "style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data: blob:; " +
        "font-src 'self' https:; " +
        "connect-src 'self' https://psford.github.io";

    await next();
});

app.UseDefaultFiles();

// Configure static files with custom MIME types for .mmd (Mermaid) files
var contentTypeProvider = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider();
contentTypeProvider.Mappings[".mmd"] = "text/plain";
app.UseStaticFiles(new StaticFileOptions
{
    ContentTypeProvider = contentTypeProvider
});

// API Endpoints

// Ticker validation helper - allows 1-10 alphanumeric chars plus dots, dashes, carets (e.g., BRK.B, BRK-B, ^GSPC)
static bool IsValidTicker(string? ticker) =>
    !string.IsNullOrWhiteSpace(ticker) &&
    ticker.Length <= 10 &&
    System.Text.RegularExpressions.Regex.IsMatch(ticker, @"^[A-Za-z0-9\.\-\^]+$");

static IResult InvalidTickerResult() =>
    Results.BadRequest(new { error = "Invalid ticker symbol. Use 1-10 alphanumeric characters, dots, dashes, or carets." });

// GET /api/stock/{ticker} - Get stock information with company profile and identifiers
app.MapGet("/api/stock/{ticker}", async (string ticker, AggregatedStockDataService stockService, NewsService newsService) =>
{
    if (!IsValidTicker(ticker))
        return InvalidTickerResult();

    var info = await stockService.GetStockInfoAsync(ticker);
    if (info == null)
        return Results.NotFound(new { error = "Stock not found", symbol = ticker });

    // Fetch company profile from Finnhub (includes ISIN, CUSIP, company name)
    var profile = await newsService.GetCompanyProfileAsync(ticker);

    // Try to get SEDOL from OpenFIGI if we have an ISIN
    string? sedol = null;
    if (!string.IsNullOrEmpty(profile?.Isin))
    {
        sedol = await newsService.GetSedolFromIsinAsync(profile.Isin);
    }

    // Merge profile data with stock info
    var enrichedInfo = info with
    {
        LongName = profile?.Name ?? info.LongName,
        ShortName = profile?.Name ?? info.ShortName,
        Exchange = profile?.Exchange ?? info.Exchange,
        Industry = profile?.Industry ?? info.Industry,
        Country = profile?.Country ?? info.Country,
        Website = profile?.WebUrl ?? info.Website,
        Isin = profile?.Isin,
        Cusip = profile?.Cusip,
        Sedol = sedol
    };

    return Results.Ok(enrichedInfo);
})
.WithName("GetStockInfo")
.WithOpenApi()
.Produces<StockAnalyzer.Core.Models.StockInfo>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status404NotFound);

// GET /api/stock/{ticker}/history - Get historical data
app.MapGet("/api/stock/{ticker}/history", async (
    string ticker,
    string? period,
    AggregatedStockDataService stockService) =>
{
    if (!IsValidTicker(ticker))
        return InvalidTickerResult();

    var data = await stockService.GetHistoricalDataAsync(ticker, period ?? "1y");
    return data != null
        ? Results.Ok(data)
        : Results.NotFound(new { error = "Historical data not found", symbol = ticker });
})
.WithName("GetStockHistory")
.WithOpenApi()
.Produces<StockAnalyzer.Core.Models.HistoricalDataResult>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status404NotFound);

// GET /api/stock/{ticker}/news - Get company news
app.MapGet("/api/stock/{ticker}/news", async (
    string ticker,
    int? days,
    NewsService newsService) =>
{
    if (!IsValidTicker(ticker))
        return InvalidTickerResult();

    var fromDate = DateTime.Now.AddDays(-(days ?? 30));
    var result = await newsService.GetCompanyNewsAsync(ticker, fromDate);
    return Results.Ok(result);
})
.WithName("GetStockNews")
.WithOpenApi()
.Produces<StockAnalyzer.Core.Models.NewsResult>(StatusCodes.Status200OK);

// GET /api/stock/{ticker}/significant - Get significant price moves (no news - use /news/move for lazy loading)
app.MapGet("/api/stock/{ticker}/significant", async (
    string ticker,
    decimal? threshold,
    string? period,
    AggregatedStockDataService stockService,
    AnalysisService analysisService) =>
{
    if (!IsValidTicker(ticker))
        return InvalidTickerResult();

    var historyPeriod = period ?? "1y";
    var thresholdValue = threshold ?? 3.0m;

    var history = await stockService.GetHistoricalDataAsync(ticker, historyPeriod);
    if (history == null)
        return Results.NotFound(new { error = "Historical data not found", symbol = ticker });

    // Don't fetch news - let frontend lazy-load via /news/move endpoint
    var moves = await analysisService.DetectSignificantMovesAsync(
        ticker,
        history.Data,
        thresholdValue,
        includeNews: false);

    return Results.Ok(moves);
})
.WithName("GetSignificantMoves")
.WithOpenApi()
.Produces<StockAnalyzer.Core.Models.SignificantMovesResult>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status404NotFound);

// GET /api/stock/{ticker}/news/move - Get news for a specific significant move date (lazy loaded on hover)
app.MapGet("/api/stock/{ticker}/news/move", async (
    string ticker,
    DateTime date,
    decimal change,
    int? limit,
    NewsService newsService,
    IMemoryCache cache) =>
{
    if (!IsValidTicker(ticker))
        return InvalidTickerResult();

    // Cache key includes ticker, date, and price direction
    var cacheKey = $"movenews:{ticker.ToUpper()}:{date:yyyy-MM-dd}:{(change >= 0 ? "up" : "down")}";
    if (cache.TryGetValue(cacheKey, out List<NewsItem>? cachedNews) && cachedNews != null)
    {
        return Results.Ok(new { articles = cachedNews });
    }

    var news = await newsService.GetNewsForDateWithSentimentAsync(
        ticker,
        date,
        change,
        maxArticles: limit ?? 5);

    // Cache for 30 minutes (news doesn't change for historical dates)
    cache.Set(cacheKey, news, TimeSpan.FromMinutes(30));

    return Results.Ok(new { articles = news });
})
.WithName("GetMoveNews")
.WithOpenApi()
.Produces(StatusCodes.Status200OK)
.Produces(StatusCodes.Status400BadRequest);

// GET /api/stock/{ticker}/analysis - Get performance metrics, moving averages, and technical indicators
app.MapGet("/api/stock/{ticker}/analysis", async (
    string ticker,
    string? period,
    AggregatedStockDataService stockService,
    AnalysisService analysisService) =>
{
    if (!IsValidTicker(ticker))
        return InvalidTickerResult();

    var history = await stockService.GetHistoricalDataAsync(ticker, period ?? "1y");
    if (history == null)
        return Results.NotFound(new { error = "Historical data not found", symbol = ticker });

    var movingAverages = analysisService.CalculateMovingAverages(history.Data);
    var performance = analysisService.CalculatePerformance(history.Data);
    var rsi = analysisService.CalculateRsi(history.Data);
    var macd = analysisService.CalculateMacd(history.Data);
    var bollingerBands = analysisService.CalculateBollingerBands(history.Data);
    var stochastic = analysisService.CalculateStochastic(history.Data);

    return Results.Ok(new
    {
        symbol = ticker.ToUpper(),
        period = period ?? "1y",
        performance,
        movingAverages,
        rsi,
        macd,
        bollingerBands,
        stochastic
    });
})
.WithName("GetStockAnalysis")
.WithOpenApi()
.Produces(StatusCodes.Status200OK)
.Produces(StatusCodes.Status404NotFound);

// GET /api/search - Search for tickers by symbol or company name
app.MapGet("/api/search", async (string q, AggregatedStockDataService stockService) =>
{
    if (string.IsNullOrWhiteSpace(q))
        return Results.BadRequest(new { error = "Query parameter 'q' is required" });

    var results = await stockService.SearchAsync(q);
    return Results.Ok(new
    {
        query = q,
        results = results.Select(r => new
        {
            symbol = r.Symbol,
            shortName = r.ShortName,
            longName = r.LongName,
            exchange = r.Exchange,
            type = r.Type,
            displayName = r.DisplayName
        })
    });
})
.WithName("SearchTickers")
.WithOpenApi()
.Produces(StatusCodes.Status200OK)
.Produces(StatusCodes.Status400BadRequest);

// GET /api/trending - Get trending stocks
app.MapGet("/api/trending", async (int? count, AggregatedStockDataService stockService) =>
{
    var trending = await stockService.GetTrendingStocksAsync(count ?? 10);
    return Results.Ok(new
    {
        count = trending.Count,
        stocks = trending.Select(t => new { symbol = t.Symbol, name = t.Name })
    });
})
.WithName("GetTrendingStocks")
.WithOpenApi()
.Produces(StatusCodes.Status200OK);

// Image API endpoints

// GET /api/images/cat - Get a processed cat image
app.MapGet("/api/images/cat", async (HttpContext context, ImageCacheService cache) =>
{
    // Prevent browser caching - each request should get a new random image
    context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate, max-age=0";
    context.Response.Headers.Pragma = "no-cache";
    context.Response.Headers.Expires = "0";

    var image = await cache.GetCatImageAsync();
    return image != null
        ? Results.File(image, "image/jpeg")
        : Results.NotFound(new { error = "No cat images available. Cache may be warming up." });
})
.WithName("GetCatImage")
.WithOpenApi()
.Produces(StatusCodes.Status200OK, contentType: "image/jpeg")
.Produces(StatusCodes.Status404NotFound);

// GET /api/images/dog - Get a processed dog image
app.MapGet("/api/images/dog", async (HttpContext context, ImageCacheService cache) =>
{
    // Prevent browser caching - each request should get a new random image
    context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate, max-age=0";
    context.Response.Headers.Pragma = "no-cache";
    context.Response.Headers.Expires = "0";

    var image = await cache.GetDogImageAsync();
    return image != null
        ? Results.File(image, "image/jpeg")
        : Results.NotFound(new { error = "No dog images available. Cache may be warming up." });
})
.WithName("GetDogImage")
.WithOpenApi()
.Produces(StatusCodes.Status200OK, contentType: "image/jpeg")
.Produces(StatusCodes.Status404NotFound);

// GET /api/images/status - Get cache status (includes maxSize for UI progress bars)
app.MapGet("/api/images/status", async (ImageCacheService cache) =>
{
    var (cats, dogs, maxSize) = await cache.GetCacheStatusAsync();
    return Results.Ok(new
    {
        cats,
        dogs,
        maxSize,
        timestamp = DateTime.UtcNow
    });
})
.WithName("GetImageCacheStatus")
.WithOpenApi()
.Produces(StatusCodes.Status200OK);

// Admin endpoints for symbol database management

// POST /api/admin/symbols/refresh - Manually trigger symbol refresh from Finnhub
app.MapPost("/api/admin/symbols/refresh", async (SymbolRefreshService? refreshService) =>
{
    if (refreshService == null)
        return Results.BadRequest(new { error = "Symbol refresh service not configured" });

    try
    {
        var count = await refreshService.RefreshSymbolsAsync();
        return Results.Ok(new { message = "Refresh complete", symbolsUpdated = count });
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Manual symbol refresh failed");
        return Results.Problem(ex.Message);
    }
})
.WithName("RefreshSymbols")
.WithOpenApi()
.Produces(StatusCodes.Status200OK)
.Produces(StatusCodes.Status400BadRequest)
.Produces(StatusCodes.Status500InternalServerError);

// GET /api/admin/symbols/status - Get symbol database status
app.MapGet("/api/admin/symbols/status", async (IServiceProvider serviceProvider) =>
{
    using var scope = serviceProvider.CreateScope();
    var repo = scope.ServiceProvider.GetService<ISymbolRepository>();
    var refreshService = serviceProvider.GetService<SymbolRefreshService>();

    if (repo == null)
        return Results.Ok(new { enabled = false, message = "Symbol database not configured (no SQL connection)" });

    var count = await repo.GetActiveCountAsync();
    var lastRefresh = await repo.GetLastRefreshTimeAsync();
    var (_, apiKeyConfigured) = refreshService?.GetStatus() ?? (DateTime.MinValue, false);

    return Results.Ok(new
    {
        enabled = true,
        activeSymbols = count,
        lastRefresh,
        finnhubApiKeyConfigured = apiKeyConfigured
    });
})
.WithName("GetSymbolStatus")
.WithOpenApi()
.Produces(StatusCodes.Status200OK);

// GET /api/admin/symbols/export - Export all symbols for client-side search (pipe-delimited)
app.MapGet("/api/admin/symbols/export", (SymbolCache cache) =>
{
    if (!cache.IsLoaded)
        return Results.Problem("Symbol cache not loaded");

    // Format: SYMBOL|Description\n (most compact text format)
    var sb = new System.Text.StringBuilder();
    foreach (var symbol in cache.GetAllActive())
    {
        sb.Append(symbol.Symbol);
        sb.Append('|');
        sb.Append(symbol.Description);
        sb.Append('\n');
    }

    return Results.Text(sb.ToString(), "text/plain");
})
.WithName("ExportSymbols")
.ExcludeFromDescription();

// Admin endpoints for price database management

// GET /api/admin/prices/status - Get price database status
app.MapGet("/api/admin/prices/status", async (IServiceProvider serviceProvider) =>
{
    using var scope = serviceProvider.CreateScope();
    var priceRepo = scope.ServiceProvider.GetService<IPriceRepository>();
    var securityRepo = scope.ServiceProvider.GetService<ISecurityMasterRepository>();
    var eodhd = serviceProvider.GetService<EodhdService>();

    if (priceRepo == null || securityRepo == null)
        return Results.Ok(new { enabled = false, message = "Price database not configured (no SQL connection)" });

    var totalPrices = await priceRepo.GetTotalCountAsync();
    var activeSecurities = await securityRepo.GetActiveCountAsync();

    // Get latest date from a sample of securities
    DateTime? latestDate = null;
    if (totalPrices > 0)
    {
        var securities = await securityRepo.GetAllActiveAsync();
        var sampleAliases = securities.Take(5).Select(s => s.SecurityAlias);
        var latestPrices = await priceRepo.GetLatestPricesAsync(sampleAliases);
        if (latestPrices.Count > 0)
        {
            latestDate = latestPrices.Values.Max(p => p.EffectiveDate);
        }
    }

    return Results.Ok(new
    {
        enabled = true,
        totalPriceRecords = totalPrices,
        activeSecurities,
        latestPriceDate = latestDate?.ToString("yyyy-MM-dd"),
        eodhdApiConfigured = eodhd?.IsAvailable ?? false
    });
})
.WithName("GetPriceStatus")
.WithOpenApi()
.Produces(StatusCodes.Status200OK);

// GET /api/admin/prices/test-eodhd - Test EODHD API connectivity (debug endpoint)
app.MapGet("/api/admin/prices/test-eodhd", async (IServiceProvider serviceProvider, IConfiguration config, string? date) =>
{
    var eodhd = serviceProvider.GetService<EodhdService>();
    if (eodhd == null)
        return Results.Ok(new { error = "EodhdService not registered" });

    // Check what API key sources are available
    var keyFromConfig1 = config["Eodhd:ApiKey"];
    var keyFromConfig2 = config["EodhdApiKey"];
    var keyFromEnv = Environment.GetEnvironmentVariable("EODHD_API_KEY");

    if (!eodhd.IsAvailable)
        return Results.Ok(new
        {
            error = "EODHD API key not configured",
            isAvailable = false,
            keySourcesChecked = new
            {
                eodhdApiKey = !string.IsNullOrEmpty(keyFromConfig1) ? "present" : "missing",
                eodhdApiKeyAlt = !string.IsNullOrEmpty(keyFromConfig2) ? "present" : "missing",
                envVar = !string.IsNullOrEmpty(keyFromEnv) ? "present" : "missing"
            }
        });

    var testDate = string.IsNullOrEmpty(date)
        ? DateTime.Today.AddDays(-1)
        : DateTime.Parse(date);

    try
    {
        // Test raw HTTP call first
        using var httpClient = new HttpClient();
        var apiKey = keyFromConfig1 ?? keyFromConfig2 ?? keyFromEnv ?? "";
        var testUrl = $"https://eodhd.com/api/eod-bulk-last-day/US?api_token={apiKey}&fmt=json&date={testDate:yyyy-MM-dd}&filter=extended";
        var rawResponse = await httpClient.GetStringAsync(testUrl);
        var rawLength = rawResponse.Length;
        var rawSample = rawResponse.Length > 500 ? rawResponse.Substring(0, 500) : rawResponse;

        // Test via service
        var data = await eodhd.GetBulkEodDataAsync(testDate, "US", CancellationToken.None);
        return Results.Ok(new
        {
            isAvailable = true,
            testDate = testDate.ToString("yyyy-MM-dd"),
            recordsReturned = data.Count,
            rawResponseLength = rawLength,
            rawResponseSample = rawSample,
            sampleTickers = data.Take(5).Select(r => new { r.Code, r.Close, r.Date }).ToList()
        });
    }
    catch (Exception ex)
    {
        return Results.Ok(new { error = ex.Message, stackTrace = ex.StackTrace });
    }
})
.WithName("TestEodhd")
.WithOpenApi();

// POST /api/admin/prices/sync-securities - Sync SecurityMaster from Symbols table
app.MapPost("/api/admin/prices/sync-securities", async (PriceRefreshService? refreshService) =>
{
    if (refreshService == null)
        return Results.BadRequest(new { error = "Price refresh service not configured" });

    try
    {
        var count = await refreshService.SyncSecurityMasterFromSymbolsAsync();
        return Results.Ok(new { message = "Security sync complete", securitiesUpserted = count });
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Security sync failed");
        return Results.Problem(ex.Message);
    }
})
.WithName("SyncSecurities")
.WithOpenApi()
.Produces(StatusCodes.Status200OK)
.Produces(StatusCodes.Status400BadRequest)
.Produces(StatusCodes.Status500InternalServerError);

// POST /api/admin/prices/sync-eodhd - Sync SecurityMaster from EODHD exchange symbols
app.MapPost("/api/admin/prices/sync-eodhd", async (
    PriceRefreshService? refreshService,
    HttpRequest request) =>
{
    if (refreshService == null)
        return Results.BadRequest(new { error = "Price refresh service not configured" });

    var body = await request.ReadFromJsonAsync<EodhdSyncRequest>();
    var exchange = body?.Exchange ?? "US";

    try
    {
        var result = await refreshService.SyncSecurityMasterFromEodhdAsync(exchange);

        if (!string.IsNullOrEmpty(result.ErrorMessage))
            return Results.Problem(result.ErrorMessage);

        return Results.Ok(new
        {
            message = $"EODHD sync complete for {exchange}",
            exchange = result.Exchange,
            totalSymbols = result.TotalSymbols,
            securitiesUpserted = result.SecuritiesUpserted
        });
    }
    catch (Exception ex)
    {
        Log.Error(ex, "EODHD sync failed for {Exchange}", exchange);
        return Results.Problem(ex.Message);
    }
})
.WithName("SyncEodhd")
.WithOpenApi()
.Produces(StatusCodes.Status200OK)
.Produces(StatusCodes.Status400BadRequest)
.Produces(StatusCodes.Status500InternalServerError);

// POST /api/admin/prices/refresh-date - Refresh prices for a specific date
app.MapPost("/api/admin/prices/refresh-date", async (
    PriceRefreshService? refreshService,
    HttpRequest request) =>
{
    if (refreshService == null)
        return Results.BadRequest(new { error = "Price refresh service not configured" });

    var body = await request.ReadFromJsonAsync<RefreshDateRequest>();
    if (body == null || string.IsNullOrEmpty(body.Date))
        return Results.BadRequest(new { error = "Date parameter required (format: yyyy-MM-dd)" });

    if (!DateTime.TryParse(body.Date, out var date))
        return Results.BadRequest(new { error = "Invalid date format. Use yyyy-MM-dd" });

    try
    {
        var result = await refreshService.RefreshDateAsync(date, CancellationToken.None);
        return Results.Ok(new
        {
            message = $"Prices refreshed for {date:yyyy-MM-dd}",
            date = date.ToString("yyyy-MM-dd"),
            recordsFetched = result.RecordsFetched,
            recordsMatched = result.RecordsMatched,
            recordsUnmatched = result.RecordsUnmatched,
            recordsInserted = result.RecordsInserted
        });
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Price refresh failed for {Date}", date);
        return Results.Problem(ex.Message);
    }
})
.WithName("RefreshPricesForDate")
.WithOpenApi()
.Produces(StatusCodes.Status200OK)
.Produces(StatusCodes.Status400BadRequest)
.Produces(StatusCodes.Status500InternalServerError);

// POST /api/admin/prices/bulk-load - Start bulk historical data load
app.MapPost("/api/admin/prices/bulk-load", async (
    PriceRefreshService? refreshService,
    HttpRequest request) =>
{
    if (refreshService == null)
        return Results.BadRequest(new { error = "Price refresh service not configured" });

    var body = await request.ReadFromJsonAsync<BulkLoadRequest>();
    if (body == null || string.IsNullOrEmpty(body.StartDate) || string.IsNullOrEmpty(body.EndDate))
        return Results.BadRequest(new { error = "StartDate and EndDate required (format: yyyy-MM-dd)" });

    if (!DateTime.TryParse(body.StartDate, out var startDate))
        return Results.BadRequest(new { error = "Invalid StartDate format. Use yyyy-MM-dd" });

    if (!DateTime.TryParse(body.EndDate, out var endDate))
        return Results.BadRequest(new { error = "Invalid EndDate format. Use yyyy-MM-dd" });

    if (startDate > endDate)
        return Results.BadRequest(new { error = "StartDate must be before EndDate" });

    try
    {
        // Run bulk load in background - this can take a long time
        _ = Task.Run(async () =>
        {
            try
            {
                var result = await refreshService.BulkLoadHistoricalDataAsync(startDate, endDate);
                Log.Information("Bulk load completed: {Processed}/{Total} days, {Errors} errors",
                    result.DaysProcessed, result.TotalDays, result.Errors.Count);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Bulk load failed");
            }
        });

        return Results.Accepted(value: new
        {
            message = $"Bulk load started for {startDate:yyyy-MM-dd} to {endDate:yyyy-MM-dd}",
            note = "Check logs for progress. This operation runs in the background."
        });
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Failed to start bulk load");
        return Results.Problem(ex.Message);
    }
})
.WithName("BulkLoadPrices")
.WithOpenApi()
.Produces(StatusCodes.Status202Accepted)
.Produces(StatusCodes.Status400BadRequest)
.Produces(StatusCodes.Status500InternalServerError);

// GET /api/admin/prices/coverage-dates - Get all distinct dates with price data in a range
app.MapGet("/api/admin/prices/coverage-dates", async (
    string? startDate,
    string? endDate,
    IServiceProvider serviceProvider) =>
{
    using var scope = serviceProvider.CreateScope();
    var priceRepo = scope.ServiceProvider.GetService<IPriceRepository>();

    if (priceRepo == null)
        return Results.BadRequest(new { error = "Price repository not configured" });

    // Default to last 2 years if not specified
    var start = DateTime.TryParse(startDate, out var s) ? s : DateTime.Today.AddYears(-2);
    var end = DateTime.TryParse(endDate, out var e) ? e : DateTime.Today;

    var dates = await priceRepo.GetDistinctDatesAsync(start, end);
    return Results.Ok(new
    {
        startDate = start.ToString("yyyy-MM-dd"),
        endDate = end.ToString("yyyy-MM-dd"),
        datesWithData = dates.Select(d => d.ToString("yyyy-MM-dd")).ToList(),
        count = dates.Count
    });
})
.WithName("GetCoverageDates")
.WithOpenApi()
.Produces(StatusCodes.Status200OK)
.Produces(StatusCodes.Status400BadRequest);

// POST /api/admin/prices/load-tickers - Load historical data for specific tickers
app.MapPost("/api/admin/prices/load-tickers", async (
    PriceRefreshService? refreshService,
    HttpRequest request) =>
{
    if (refreshService == null)
        return Results.BadRequest(new { error = "Price refresh service not configured" });

    var body = await request.ReadFromJsonAsync<TickerLoadRequest>();
    if (body == null || body.Tickers == null || body.Tickers.Length == 0)
        return Results.BadRequest(new { error = "Tickers array required" });

    if (string.IsNullOrEmpty(body.StartDate) || string.IsNullOrEmpty(body.EndDate))
        return Results.BadRequest(new { error = "StartDate and EndDate required (format: yyyy-MM-dd)" });

    if (!DateTime.TryParse(body.StartDate, out var startDate))
        return Results.BadRequest(new { error = "Invalid StartDate format. Use yyyy-MM-dd" });

    if (!DateTime.TryParse(body.EndDate, out var endDate))
        return Results.BadRequest(new { error = "Invalid EndDate format. Use yyyy-MM-dd" });

    try
    {
        var result = await refreshService.LoadHistoricalDataForTickersAsync(
            body.Tickers, startDate, endDate);

        return Results.Ok(new
        {
            message = "Ticker load complete",
            tickersRequested = result.TotalTickers,
            tickersProcessed = result.TickersProcessed,
            recordsInserted = result.TotalRecordsInserted,
            errors = result.Errors
        });
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Failed to load ticker data");
        return Results.Problem(ex.Message);
    }
})
.WithName("LoadTickerPrices")
.WithOpenApi()
.Produces(StatusCodes.Status200OK)
.Produces(StatusCodes.Status400BadRequest)
.Produces(StatusCodes.Status500InternalServerError);

// POST /api/admin/prices/backfill - Optimized parallel backfill for multiple tickers
app.MapPost("/api/admin/prices/backfill", async (
    PriceRefreshService? refreshService,
    HttpRequest request) =>
{
    if (refreshService == null)
        return Results.BadRequest(new { error = "Price refresh service not configured" });

    var body = await request.ReadFromJsonAsync<BackfillRequest>();
    if (body == null || body.Tickers == null || body.Tickers.Length == 0)
        return Results.BadRequest(new { error = "Tickers array required" });

    if (string.IsNullOrEmpty(body.StartDate) || string.IsNullOrEmpty(body.EndDate))
        return Results.BadRequest(new { error = "StartDate and EndDate required (format: yyyy-MM-dd)" });

    if (!DateTime.TryParse(body.StartDate, out var startDate))
        return Results.BadRequest(new { error = "Invalid StartDate format. Use yyyy-MM-dd" });

    if (!DateTime.TryParse(body.EndDate, out var endDate))
        return Results.BadRequest(new { error = "Invalid EndDate format. Use yyyy-MM-dd" });

    var maxConcurrency = body.MaxConcurrency ?? 10;
    if (maxConcurrency < 1 || maxConcurrency > 50)
        return Results.BadRequest(new { error = "MaxConcurrency must be between 1 and 50" });

    try
    {
        Log.Information("Starting parallel backfill for {Count} tickers with concurrency {Concurrency}",
            body.Tickers.Length, maxConcurrency);

        var result = await refreshService.BackfillTickersParallelAsync(
            body.Tickers, startDate, endDate, maxConcurrency);

        return Results.Ok(new
        {
            message = "Parallel backfill complete",
            tickersRequested = result.TotalTickers,
            tickersProcessed = result.TickersProcessed,
            recordsInserted = result.TotalRecordsInserted,
            errors = result.Errors,
            wasCancelled = result.WasCancelled
        });
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Failed to run parallel backfill");
        return Results.Problem(ex.Message);
    }
})
.WithName("BackfillTickerPrices")
.WithOpenApi()
.Produces(StatusCodes.Status200OK)
.Produces(StatusCodes.Status400BadRequest)
.Produces(StatusCodes.Status500InternalServerError);

// ============================================================================
// Data Export Endpoints (for syncing production data to local development)
// ============================================================================

// GET /api/admin/data/securities - Export all active securities from SecurityMaster
app.MapGet("/api/admin/data/securities", async (IServiceProvider serviceProvider) =>
{
    using var scope = serviceProvider.CreateScope();
    var securityRepo = scope.ServiceProvider.GetService<ISecurityMasterRepository>();

    if (securityRepo == null)
        return Results.BadRequest(new { error = "Security repository not configured" });

    try
    {
        Log.Information("Exporting securities for data sync");
        var securities = await securityRepo.GetAllActiveAsync();

        return Results.Ok(new
        {
            success = true,
            count = securities.Count,
            securities = securities.Select(s => new
            {
                securityAlias = s.SecurityAlias,
                tickerSymbol = s.TickerSymbol,
                issueName = s.IssueName,
                exchange = s.Exchange,
                securityType = s.SecurityType,
                country = s.Country,
                currency = s.Currency,
                isin = s.Isin,
                isActive = s.IsActive
            })
        });
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Security export failed");
        return Results.Problem(ex.Message);
    }
})
.WithName("ExportSecurities")
.WithOpenApi()
.Produces(StatusCodes.Status200OK)
.Produces(StatusCodes.Status400BadRequest)
.Produces(StatusCodes.Status500InternalServerError);

// GET /api/admin/data/prices - Export prices with pagination (for large datasets)
app.MapGet("/api/admin/data/prices", async (
    IServiceProvider serviceProvider,
    DateTime? startDate,
    DateTime? endDate,
    int page = 1,
    int pageSize = 10000) =>
{
    using var scope = serviceProvider.CreateScope();
    var context = scope.ServiceProvider.GetService<StockAnalyzerDbContext>();

    if (context == null)
        return Results.BadRequest(new { error = "Database context not configured" });

    try
    {
        // Default to last 2 years if no dates specified
        var end = endDate ?? DateTime.Today;
        var start = startDate ?? end.AddYears(-2);

        // Clamp page size to prevent abuse
        pageSize = Math.Min(pageSize, 50000);
        var skip = (page - 1) * pageSize;

        Log.Information("Exporting prices: {Start:yyyy-MM-dd} to {End:yyyy-MM-dd}, page {Page}, pageSize {PageSize}",
            start, end, page, pageSize);

        // Get total count for pagination info
        var totalCount = await context.Prices
            .AsNoTracking()
            .Where(p => p.EffectiveDate >= start.Date && p.EffectiveDate <= end.Date)
            .CountAsync();

        var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);

        // Get paginated prices
        var prices = await context.Prices
            .AsNoTracking()
            .Where(p => p.EffectiveDate >= start.Date && p.EffectiveDate <= end.Date)
            .OrderBy(p => p.EffectiveDate)
            .ThenBy(p => p.SecurityAlias)
            .Skip(skip)
            .Take(pageSize)
            .Select(p => new
            {
                securityAlias = p.SecurityAlias,
                effectiveDate = p.EffectiveDate.ToString("yyyy-MM-dd"),
                open = p.Open,
                high = p.High,
                low = p.Low,
                close = p.Close,
                volume = p.Volume,
                adjustedClose = p.AdjustedClose
            })
            .ToListAsync();

        return Results.Ok(new
        {
            success = true,
            startDate = start.ToString("yyyy-MM-dd"),
            endDate = end.ToString("yyyy-MM-dd"),
            page,
            pageSize,
            totalPages,
            totalCount,
            count = prices.Count,
            hasMore = page < totalPages,
            prices
        });
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Price export failed");
        return Results.Problem(ex.Message);
    }
})
.WithName("ExportPrices")
.WithOpenApi()
.Produces(StatusCodes.Status200OK)
.Produces(StatusCodes.Status400BadRequest)
.Produces(StatusCodes.Status500InternalServerError);

// GET /api/admin/data/prices/summary - Get summary of available price data
app.MapGet("/api/admin/data/prices/summary", async (IServiceProvider serviceProvider) =>
{
    using var scope = serviceProvider.CreateScope();
    var context = scope.ServiceProvider.GetService<StockAnalyzerDbContext>();

    if (context == null)
        return Results.BadRequest(new { error = "Database context not configured" });

    try
    {
        var dateRange = await context.Prices
            .AsNoTracking()
            .GroupBy(_ => 1)
            .Select(g => new
            {
                MinDate = g.Min(p => p.EffectiveDate),
                MaxDate = g.Max(p => p.EffectiveDate),
                TotalRecords = g.Count(),
                DistinctSecurities = g.Select(p => p.SecurityAlias).Distinct().Count()
            })
            .FirstOrDefaultAsync();

        if (dateRange == null)
        {
            return Results.Ok(new
            {
                success = true,
                hasData = false,
                message = "No price data in database"
            });
        }

        return Results.Ok(new
        {
            success = true,
            hasData = true,
            startDate = dateRange.MinDate.ToString("yyyy-MM-dd"),
            endDate = dateRange.MaxDate.ToString("yyyy-MM-dd"),
            totalRecords = dateRange.TotalRecords,
            distinctSecurities = dateRange.DistinctSecurities
        });
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Price summary failed");
        return Results.Problem(ex.Message);
    }
})
.WithName("PriceSummary")
.WithOpenApi()
.Produces(StatusCodes.Status200OK)
.Produces(StatusCodes.Status400BadRequest)
.Produces(StatusCodes.Status500InternalServerError);

// POST /api/admin/data/seed-tracked-securities - Seed TrackedSecurities from existing price data
// Run this AFTER the EF Core migration has created the schema
app.MapPost("/api/admin/data/seed-tracked-securities", async (IServiceProvider serviceProvider) =>
{
    using var scope = serviceProvider.CreateScope();
    var context = scope.ServiceProvider.GetService<StockAnalyzerDbContext>();

    if (context == null)
        return Results.BadRequest(new { error = "Database context not configured" });

    try
    {
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        int securitiesSeeded = 0;
        int securitiesUpdated = 0;

        // Insert into TrackedSecurities for securities with prices
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
                INSERT INTO data.TrackedSecurities (SecurityAlias, Source, Priority, Notes, AddedBy)
                SELECT DISTINCT p.SecurityAlias, 'Legacy', 1, 'Auto-added from existing price data', 'migration'
                FROM data.Prices p
                INNER JOIN data.SecurityMaster sm ON sm.SecurityAlias = p.SecurityAlias
                WHERE NOT EXISTS (SELECT 1 FROM data.TrackedSecurities ts WHERE ts.SecurityAlias = p.SecurityAlias);

                SELECT @@ROWCOUNT";
            cmd.CommandTimeout = 120;
            securitiesSeeded = (int)(await cmd.ExecuteScalarAsync() ?? 0);
        }

        // Update IsTracked flag on SecurityMaster
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
                UPDATE sm
                SET IsTracked = 1
                FROM data.SecurityMaster sm
                WHERE EXISTS (SELECT 1 FROM data.TrackedSecurities ts WHERE ts.SecurityAlias = sm.SecurityAlias)
                  AND sm.IsTracked = 0;

                SELECT @@ROWCOUNT";
            cmd.CommandTimeout = 120;
            securitiesUpdated = (int)(await cmd.ExecuteScalarAsync() ?? 0);
        }

        // Verify counts
        int trackedInSM = 0;
        int trackedRecords = 0;
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT
                    (SELECT COUNT(*) FROM data.SecurityMaster WHERE IsTracked = 1),
                    (SELECT COUNT(*) FROM data.TrackedSecurities)";
            cmd.CommandTimeout = 30;
            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                trackedInSM = reader.GetInt32(0);
                trackedRecords = reader.GetInt32(1);
            }
        }

        return Results.Ok(new
        {
            success = true,
            message = "Tracked securities seeded successfully",
            securitiesSeeded,
            securitiesUpdated,
            verification = new
            {
                trackedInSecurityMaster = trackedInSM,
                trackedSecuritiesRecords = trackedRecords,
                inSync = trackedInSM == trackedRecords
            }
        });
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Seeding tracked securities failed");
        return Results.Problem($"Seeding failed: {ex.Message}");
    }
})
.WithName("SeedTrackedSecurities")
.WithOpenApi()
.Produces(StatusCodes.Status200OK)
.Produces(StatusCodes.Status400BadRequest)
.Produces(StatusCodes.Status500InternalServerError);

// POST /api/admin/data/reset-tracked - Reset tracking and import S&P 500 tickers
// This clears all existing tracking and sets up proper S&P 500 tracking
app.MapPost("/api/admin/data/reset-tracked", async (IServiceProvider serviceProvider, ResetTrackedRequest request) =>
{
    using var scope = serviceProvider.CreateScope();
    var context = scope.ServiceProvider.GetService<StockAnalyzerDbContext>();

    if (context == null)
        return Results.BadRequest(new { error = "Database context not configured" });

    if (request.Tickers == null || request.Tickers.Count == 0)
        return Results.BadRequest(new { error = "No tickers provided" });

    try
    {
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        int cleared = 0;
        int reset = 0;
        int matched = 0;
        int tracked = 0;

        // Step 1: Clear TrackedSecurities table
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "DELETE FROM data.TrackedSecurities; SELECT @@ROWCOUNT";
            cmd.CommandTimeout = 60;
            cleared = (int)(await cmd.ExecuteScalarAsync() ?? 0);
        }

        // Step 2: Reset all IsTracked flags to 0
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "UPDATE data.SecurityMaster SET IsTracked = 0 WHERE IsTracked = 1; SELECT @@ROWCOUNT";
            cmd.CommandTimeout = 60;
            reset = (int)(await cmd.ExecuteScalarAsync() ?? 0);
        }

        // Step 3: Clean ticker list (no SQL escaping needed - using parameters)
        var cleanTickers = request.Tickers
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim().ToUpperInvariant())
            .Distinct()
            .ToList();

        if (cleanTickers.Count == 0)
            return Results.BadRequest(new { error = "No valid tickers provided" });

        var source = request.Source ?? "S&P 500";
        var priority = request.Priority ?? 1;

        // Step 4: Insert tickers into temp table using parameterized batches
        // Using temp table approach for large ticker lists (S&P 500 has ~500)
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "CREATE TABLE #ImportTickers (Ticker NVARCHAR(20) PRIMARY KEY)";
            cmd.CommandTimeout = 30;
            await cmd.ExecuteNonQueryAsync();
        }

        // Insert tickers in batches of 100 using parameterized queries
        const int batchSize = 100;
        for (int i = 0; i < cleanTickers.Count; i += batchSize)
        {
            var batch = cleanTickers.Skip(i).Take(batchSize).ToList();
            using var cmd = (SqlCommand)connection.CreateCommand();

            var paramNames = new List<string>();
            for (int j = 0; j < batch.Count; j++)
            {
                var paramName = $"@t{j}";
                paramNames.Add($"({paramName})");
                cmd.Parameters.AddWithValue(paramName, batch[j]);
            }

            // paramNames contains only parameter placeholders (@t0, @t1, etc.) - safe
#pragma warning disable CA2100
            cmd.CommandText = $"INSERT INTO #ImportTickers (Ticker) VALUES {string.Join(",", paramNames)}";
#pragma warning restore CA2100
            cmd.CommandTimeout = 30;
            await cmd.ExecuteNonQueryAsync();
        }

        // Step 5: Insert into TrackedSecurities from temp table (fully parameterized)
        using (var cmd = (SqlCommand)connection.CreateCommand())
        {
            cmd.CommandText = @"
                INSERT INTO data.TrackedSecurities (SecurityAlias, Source, Priority, Notes, AddedBy)
                SELECT sm.SecurityAlias, @source, @priority, 'Imported via reset-tracked API', 'api'
                FROM data.SecurityMaster sm
                INNER JOIN #ImportTickers t ON sm.TickerSymbol = t.Ticker
                WHERE sm.IsActive = 1
                  AND NOT EXISTS (SELECT 1 FROM data.TrackedSecurities ts WHERE ts.SecurityAlias = sm.SecurityAlias);
                SELECT @@ROWCOUNT";
            cmd.Parameters.AddWithValue("@source", source);
            cmd.Parameters.AddWithValue("@priority", priority);
            cmd.CommandTimeout = 60;
            matched = (int)(await cmd.ExecuteScalarAsync() ?? 0);
        }

        // Step 6: Update IsTracked flag on SecurityMaster
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
                UPDATE sm
                SET IsTracked = 1
                FROM data.SecurityMaster sm
                WHERE EXISTS (SELECT 1 FROM data.TrackedSecurities ts WHERE ts.SecurityAlias = sm.SecurityAlias)
                  AND sm.IsTracked = 0;
                SELECT @@ROWCOUNT";
            cmd.CommandTimeout = 60;
            tracked = (int)(await cmd.ExecuteScalarAsync() ?? 0);
        }

        // Step 7: Get list of tickers that weren't found (using temp table)
        var notFound = new List<string>();
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT t.Ticker
                FROM #ImportTickers t
                WHERE NOT EXISTS (
                    SELECT 1 FROM data.SecurityMaster sm
                    WHERE sm.TickerSymbol = t.Ticker AND sm.IsActive = 1
                )";
            cmd.CommandTimeout = 30;
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                notFound.Add(reader.GetString(0));
            }
        }

        // Cleanup temp table
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "DROP TABLE #ImportTickers";
            await cmd.ExecuteNonQueryAsync();
        }

        Log.Information("Reset tracked securities: cleared {Cleared}, reset {Reset}, matched {Matched}, tracked {Tracked}",
            cleared, reset, matched, tracked);

        return Results.Ok(new
        {
            success = true,
            message = $"Tracking reset. {matched} securities now tracked as '{source}'",
            details = new
            {
                trackedSecuritiesCleared = cleared,
                isTrackedFlagsReset = reset,
                tickersProvided = cleanTickers.Count,
                tickersMatched = matched,
                isTrackedFlagsSet = tracked,
                tickersNotFound = notFound.Count,
                notFoundTickers = notFound.Take(20).ToList() // Show first 20
            }
        });
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Reset tracked securities failed");
        return Results.Problem($"Reset failed: {ex.Message}");
    }
})
.WithName("ResetTrackedSecurities")
.WithOpenApi()
.Produces(StatusCodes.Status200OK)
.Produces(StatusCodes.Status400BadRequest)
.Produces(StatusCodes.Status500InternalServerError);

// GET /api/admin/data/prices/monitor - Rich monitoring stats for crawler display
// Uses efficient SQL queries instead of EF LINQ to avoid full table scans
app.MapGet("/api/admin/data/prices/monitor", async (IServiceProvider serviceProvider) =>
{
    using var scope = serviceProvider.CreateScope();
    var context = scope.ServiceProvider.GetService<StockAnalyzerDbContext>();

    if (context == null)
        return Results.BadRequest(new { error = "Database context not configured" });

    try
    {
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        // Use simple, efficient queries that leverage indexes
        // Query 1: Basic counts (uses clustered index scan, but COUNT is optimized)
        int totalRecords = 0;
        DateTime? minDate = null;
        DateTime? maxDate = null;

        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT COUNT(*) as TotalRecords,
                       MIN(EffectiveDate) as MinDate,
                       MAX(EffectiveDate) as MaxDate
                FROM data.Prices WITH (NOLOCK)";
            cmd.CommandTimeout = 30;

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                totalRecords = reader.GetInt32(0);
                if (!reader.IsDBNull(1))
                {
                    minDate = reader.GetDateTime(1);
                    maxDate = reader.GetDateTime(2);
                }
            }
        }

        if (totalRecords == 0 || minDate == null)
        {
            return Results.Ok(new
            {
                success = true,
                hasData = false,
                message = "No price data in database"
            });
        }

        // Query 2: Distinct counts (separate queries are faster than one big GROUP BY)
        int distinctSecurities = 0;
        int distinctDates = 0;

        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(DISTINCT SecurityAlias) FROM data.Prices WITH (NOLOCK)";
            cmd.CommandTimeout = 30;
            distinctSecurities = (int)(await cmd.ExecuteScalarAsync() ?? 0);
        }

        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(DISTINCT EffectiveDate) FROM data.Prices WITH (NOLOCK)";
            cmd.CommandTimeout = 30;
            distinctDates = (int)(await cmd.ExecuteScalarAsync() ?? 0);
        }

        // Query 3: Coverage by decade (efficient grouping)
        var coverageByDecade = new List<object>();
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT (YEAR(EffectiveDate) / 10) * 10 as Decade,
                       COUNT(*) as Records,
                       COUNT(DISTINCT SecurityAlias) as Securities,
                       COUNT(DISTINCT EffectiveDate) as TradingDays
                FROM data.Prices WITH (NOLOCK)
                GROUP BY (YEAR(EffectiveDate) / 10) * 10
                ORDER BY Decade";
            cmd.CommandTimeout = 60;

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                coverageByDecade.Add(new
                {
                    decade = $"{reader.GetInt32(0)}s",
                    records = reader.GetInt32(1),
                    securities = reader.GetInt32(2),
                    tradingDays = reader.GetInt32(3)
                });
            }
        }

        // Query 4: Recent activity - just get the 10 most recent distinct dates
        var recentActivity = new List<object>();
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT TOP 10 EffectiveDate, MAX(CreatedAt) as LoadedAt
                FROM data.Prices WITH (NOLOCK)
                GROUP BY EffectiveDate
                ORDER BY MAX(CreatedAt) DESC";
            cmd.CommandTimeout = 30;

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                recentActivity.Add(new
                {
                    date = reader.GetDateTime(0).ToString("yyyy-MM-dd"),
                    loadedAt = reader.GetDateTime(1).ToString("yyyy-MM-dd HH:mm:ss")
                });
            }
        }

        var yearsOfData = maxDate!.Value.Year - minDate!.Value.Year + 1;
        var avgRecordsPerDay = distinctDates > 0 ? totalRecords / distinctDates : 0;

        return Results.Ok(new
        {
            success = true,
            hasData = true,
            totalRecords,
            distinctSecurities,
            distinctDates,
            startDate = minDate!.Value.ToString("yyyy-MM-dd"),
            endDate = maxDate!.Value.ToString("yyyy-MM-dd"),
            yearsOfData,
            avgRecordsPerDay,
            coverageByDecade,
            recentActivity
        });
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Price monitor stats failed");
        return Results.Problem(ex.Message);
    }
})
.WithName("PriceMonitor")
.WithOpenApi()
.Produces(StatusCodes.Status200OK)
.Produces(StatusCodes.Status400BadRequest)
.Produces(StatusCodes.Status500InternalServerError);

// GET /api/admin/prices/gaps - Find missing price data for securities
// By default only checks tracked securities (IsTracked = 1)
// Set includeUntracked=true to also include untracked securities (prioritized after tracked)
app.MapGet("/api/admin/prices/gaps", async (IServiceProvider serviceProvider, string? market, int? limit, bool? includeUntracked) =>
{
    using var scope = serviceProvider.CreateScope();
    var context = scope.ServiceProvider.GetService<StockAnalyzerDbContext>();

    if (context == null)
        return Results.BadRequest(new { error = "Database context not configured" });

    try
    {
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        var marketFilter = market?.ToUpperInvariant() ?? "US";
        var maxResults = limit ?? 50;
        var includeUntrackedSecurities = includeUntracked ?? false;

        // Strategy: Find securities with gaps in their price history
        // When includeUntracked=false (default): Only tracked securities (IsTracked = 1)
        // When includeUntracked=true: All active securities, but tracked ones come first
        //
        // Performance optimization for Azure SQL Basic (5 DTU):
        // Internal gap analysis (comparing expected vs actual business days) is ONLY done for
        // tracked securities (~420). This avoids expensive correlated subqueries across 23K+ securities.
        // Untracked securities are detected via lighter queries: NoPriceData and StaleData.

        using var cmd = connection.CreateCommand();

        // Two query paths optimized for Azure SQL Basic (5 DTU):
        // Path A (tracked only): Lean query with no 5.2M-row Prices scan
        // Path B (include untracked): Full query with SecuritiesWithPrices CTE
        if (includeUntrackedSecurities)
        {
            cmd.CommandText = @"
                WITH TwoYearBusinessDays AS (
                    SELECT COUNT(*) AS DayCount
                    FROM data.BusinessCalendar bc WITH (NOLOCK)
                    WHERE bc.IsBusinessDay = 1
                      AND bc.EffectiveDate >= DATEADD(YEAR, -2, GETDATE())
                      AND bc.EffectiveDate <= GETDATE()
                ),
                SecuritiesWithPrices AS (
                    SELECT SecurityAlias, MAX(EffectiveDate) AS LatestPriceDate
                    FROM data.Prices WITH (NOLOCK)
                    GROUP BY SecurityAlias
                ),
                SecurityDateRange AS (
                    -- Internal gap analysis: tracked only (avoids 23K+ correlated subqueries)
                    SELECT
                        sm.SecurityAlias, sm.TickerSymbol, sm.IsTracked, sm.SecurityType,
                        sm.ImportanceScore, COALESCE(ts.Priority, 999) AS Priority,
                        MIN(p.EffectiveDate) AS FirstDate,
                        CASE WHEN MAX(p.EffectiveDate) > CAST(GETDATE() AS DATE)
                             THEN CAST(GETDATE() AS DATE) ELSE MAX(p.EffectiveDate) END AS LastDate,
                        COUNT(CASE WHEN p.EffectiveDate <= CAST(GETDATE() AS DATE) THEN 1 END) AS ActualPriceCount
                    FROM data.SecurityMaster sm WITH (NOLOCK)
                    INNER JOIN data.Prices p WITH (NOLOCK) ON p.SecurityAlias = sm.SecurityAlias
                    LEFT JOIN data.TrackedSecurities ts WITH (NOLOCK) ON ts.SecurityAlias = sm.SecurityAlias
                    WHERE sm.IsActive = 1 AND sm.IsEodhdUnavailable = 0 AND sm.IsTracked = 1
                    GROUP BY sm.SecurityAlias, sm.TickerSymbol, sm.IsTracked, sm.SecurityType, sm.ImportanceScore, ts.Priority
                ),
                ExpectedCounts AS (
                    SELECT sdr.*, (SELECT COUNT(*) FROM data.BusinessCalendar bc WITH (NOLOCK)
                         WHERE bc.IsBusinessDay = 1 AND bc.EffectiveDate >= sdr.FirstDate
                           AND bc.EffectiveDate <= sdr.LastDate) AS ExpectedPriceCount
                    FROM SecurityDateRange sdr
                ),
                GapsInExistingData AS (
                    SELECT SecurityAlias, TickerSymbol, IsTracked, SecurityType, ImportanceScore,
                           Priority, FirstDate, LastDate, ActualPriceCount, ExpectedPriceCount,
                           (ExpectedPriceCount - ActualPriceCount) AS MissingDays
                    FROM ExpectedCounts WHERE ExpectedPriceCount > ActualPriceCount
                ),
                NoPriceData AS (
                    SELECT sm.SecurityAlias, sm.TickerSymbol, sm.IsTracked, sm.SecurityType,
                           sm.ImportanceScore, COALESCE(ts.Priority, 999) AS Priority,
                           DATEADD(YEAR, -2, GETDATE()) AS FirstDate, CAST(GETDATE() AS DATE) AS LastDate,
                           0 AS ActualPriceCount,
                           (SELECT DayCount FROM TwoYearBusinessDays) AS ExpectedPriceCount,
                           (SELECT DayCount FROM TwoYearBusinessDays) AS MissingDays
                    FROM data.SecurityMaster sm WITH (NOLOCK)
                    LEFT JOIN SecuritiesWithPrices swp ON swp.SecurityAlias = sm.SecurityAlias
                    LEFT JOIN data.TrackedSecurities ts WITH (NOLOCK) ON ts.SecurityAlias = sm.SecurityAlias
                    WHERE sm.IsActive = 1 AND sm.IsEodhdUnavailable = 0 AND sm.IsTracked = 0
                      AND swp.SecurityAlias IS NULL
                ),
                StaleData AS (
                    SELECT sm.SecurityAlias, sm.TickerSymbol, sm.IsTracked, sm.SecurityType,
                           sm.ImportanceScore, COALESCE(ts.Priority, 999) AS Priority,
                           swp.LatestPriceDate AS FirstDate, CAST(GETDATE() AS DATE) AS LastDate,
                           0 AS ActualPriceCount,
                           DATEDIFF(DAY, swp.LatestPriceDate, GETDATE()) AS ExpectedPriceCount,
                           DATEDIFF(DAY, swp.LatestPriceDate, GETDATE()) AS MissingDays
                    FROM data.SecurityMaster sm WITH (NOLOCK)
                    INNER JOIN SecuritiesWithPrices swp ON swp.SecurityAlias = sm.SecurityAlias
                    LEFT JOIN data.TrackedSecurities ts WITH (NOLOCK) ON ts.SecurityAlias = sm.SecurityAlias
                    WHERE sm.IsActive = 1 AND sm.IsEodhdUnavailable = 0 AND sm.IsTracked = 0
                      AND swp.LatestPriceDate < DATEADD(DAY, -30, GETDATE())
                      AND sm.SecurityAlias NOT IN (SELECT SecurityAlias FROM GapsInExistingData)
                ),
                AllGaps AS (
                    SELECT * FROM GapsInExistingData UNION ALL
                    SELECT * FROM NoPriceData UNION ALL
                    SELECT * FROM StaleData
                )
                SELECT TOP (@limit)
                    SecurityAlias, TickerSymbol, IsTracked, FirstDate, LastDate,
                    ActualPriceCount, ExpectedPriceCount, MissingDays, ImportanceScore
                FROM AllGaps
                ORDER BY IsTracked DESC, Priority, ImportanceScore DESC,
                    CASE WHEN SecurityType = 'Common Stock' THEN 0 ELSE 1 END,
                    LEN(TickerSymbol), MissingDays DESC, TickerSymbol";
        }
        else
        {
            // Tracked-only: lean query — no SecuritiesWithPrices scan (saves ~90s on 5 DTU)
            cmd.CommandText = @"
                WITH SecurityDateRange AS (
                    SELECT
                        sm.SecurityAlias, sm.TickerSymbol, sm.IsTracked, sm.SecurityType,
                        sm.ImportanceScore, COALESCE(ts.Priority, 999) AS Priority,
                        MIN(p.EffectiveDate) AS FirstDate,
                        CASE WHEN MAX(p.EffectiveDate) > CAST(GETDATE() AS DATE)
                             THEN CAST(GETDATE() AS DATE) ELSE MAX(p.EffectiveDate) END AS LastDate,
                        COUNT(CASE WHEN p.EffectiveDate <= CAST(GETDATE() AS DATE) THEN 1 END) AS ActualPriceCount
                    FROM data.SecurityMaster sm WITH (NOLOCK)
                    INNER JOIN data.Prices p WITH (NOLOCK) ON p.SecurityAlias = sm.SecurityAlias
                    LEFT JOIN data.TrackedSecurities ts WITH (NOLOCK) ON ts.SecurityAlias = sm.SecurityAlias
                    WHERE sm.IsActive = 1 AND sm.IsEodhdUnavailable = 0 AND sm.IsTracked = 1
                    GROUP BY sm.SecurityAlias, sm.TickerSymbol, sm.IsTracked, sm.SecurityType, sm.ImportanceScore, ts.Priority
                ),
                ExpectedCounts AS (
                    SELECT sdr.*, (SELECT COUNT(*) FROM data.BusinessCalendar bc WITH (NOLOCK)
                         WHERE bc.IsBusinessDay = 1 AND bc.EffectiveDate >= sdr.FirstDate
                           AND bc.EffectiveDate <= sdr.LastDate) AS ExpectedPriceCount
                    FROM SecurityDateRange sdr
                )
                SELECT TOP (@limit)
                    SecurityAlias, TickerSymbol, IsTracked, FirstDate, LastDate,
                    ActualPriceCount, ExpectedPriceCount,
                    (ExpectedPriceCount - ActualPriceCount) AS MissingDays, ImportanceScore
                FROM ExpectedCounts
                WHERE ExpectedPriceCount > ActualPriceCount
                ORDER BY Priority, MissingDays DESC, TickerSymbol";
        }

        var limitParam = cmd.CreateParameter();
        limitParam.ParameterName = "@limit";
        limitParam.Value = maxResults;
        cmd.Parameters.Add(limitParam);

        cmd.CommandTimeout = includeUntrackedSecurities ? 300 : 120; // 5 min untracked, 2 min tracked

        var securitiesWithGaps = new List<object>();

        using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                securitiesWithGaps.Add(new
                {
                    securityAlias = reader.GetInt32(0),
                    ticker = reader.GetString(1),
                    isTracked = reader.GetBoolean(2),
                    firstDate = reader.GetDateTime(3).ToString("yyyy-MM-dd"),
                    lastDate = reader.GetDateTime(4).ToString("yyyy-MM-dd"),
                    actualDays = reader.GetInt32(5),
                    expectedDays = reader.GetInt32(6),
                    missingDays = reader.GetInt32(7),
                    importanceScore = reader.GetInt32(8)
                });
            }
        }

        // Get summary stats based on mode
        int totalSecurities = 0;
        int securitiesWithData = 0;
        int totalPriceRecords = 0;
        int totalTrackedSecurities = 0;
        int totalUntrackedSecurities = 0;
        int totalTrackedWithGaps = 0;
        int totalUntrackedWithGaps = 0;
        int totalAllMissingDays = 0;

        // Count ALL securities with gaps (not limited by TOP clause)
        using (var gapCountCmd = connection.CreateCommand())
        {
            if (includeUntrackedSecurities)
            {
                gapCountCmd.CommandText = @"
                    WITH TwoYearBusinessDays AS (
                        SELECT COUNT(*) AS DayCount FROM data.BusinessCalendar bc WITH (NOLOCK)
                        WHERE bc.IsBusinessDay = 1 AND bc.EffectiveDate >= DATEADD(YEAR, -2, GETDATE())
                          AND bc.EffectiveDate <= GETDATE()
                    ),
                    SecuritiesWithPrices AS (
                        SELECT SecurityAlias, MAX(EffectiveDate) AS LatestPriceDate
                        FROM data.Prices WITH (NOLOCK) GROUP BY SecurityAlias
                    ),
                    SecurityDateRange AS (
                        SELECT sm.SecurityAlias, sm.IsTracked,
                               MIN(p.EffectiveDate) AS FirstDate, MAX(p.EffectiveDate) AS LastDate,
                               COUNT(*) AS ActualPriceCount
                        FROM data.SecurityMaster sm WITH (NOLOCK)
                        INNER JOIN data.Prices p WITH (NOLOCK) ON p.SecurityAlias = sm.SecurityAlias
                        WHERE sm.IsActive = 1 AND sm.IsEodhdUnavailable = 0 AND sm.IsTracked = 1
                        GROUP BY sm.SecurityAlias, sm.IsTracked
                    ),
                    ExpectedCounts AS (
                        SELECT sdr.SecurityAlias, sdr.IsTracked, sdr.ActualPriceCount,
                               (SELECT COUNT(*) FROM data.BusinessCalendar bc WITH (NOLOCK)
                                WHERE bc.IsBusinessDay = 1 AND bc.EffectiveDate >= sdr.FirstDate
                                  AND bc.EffectiveDate <= sdr.LastDate) AS ExpectedPriceCount
                        FROM SecurityDateRange sdr
                    ),
                    GapsInExistingData AS (
                        SELECT SecurityAlias, IsTracked, (ExpectedPriceCount - ActualPriceCount) AS MissingDays
                        FROM ExpectedCounts WHERE ExpectedPriceCount > ActualPriceCount
                    ),
                    NoPriceData AS (
                        SELECT sm.SecurityAlias, sm.IsTracked,
                               (SELECT DayCount FROM TwoYearBusinessDays) AS MissingDays
                        FROM data.SecurityMaster sm WITH (NOLOCK)
                        LEFT JOIN SecuritiesWithPrices swp ON swp.SecurityAlias = sm.SecurityAlias
                        WHERE sm.IsActive = 1 AND sm.IsEodhdUnavailable = 0 AND sm.IsTracked = 0
                          AND swp.SecurityAlias IS NULL
                    ),
                    StaleData AS (
                        SELECT sm.SecurityAlias, sm.IsTracked,
                               DATEDIFF(DAY, swp.LatestPriceDate, GETDATE()) AS MissingDays
                        FROM data.SecurityMaster sm WITH (NOLOCK)
                        INNER JOIN SecuritiesWithPrices swp ON swp.SecurityAlias = sm.SecurityAlias
                        WHERE sm.IsActive = 1 AND sm.IsEodhdUnavailable = 0 AND sm.IsTracked = 0
                          AND swp.LatestPriceDate < DATEADD(DAY, -30, GETDATE())
                          AND sm.SecurityAlias NOT IN (SELECT SecurityAlias FROM GapsInExistingData)
                    ),
                    AllGaps AS (
                        SELECT * FROM GapsInExistingData UNION ALL
                        SELECT * FROM NoPriceData UNION ALL
                        SELECT * FROM StaleData
                    )
                    SELECT
                        SUM(CASE WHEN IsTracked = 1 THEN 1 ELSE 0 END) AS TrackedWithGaps,
                        SUM(CASE WHEN IsTracked = 0 THEN 1 ELSE 0 END) AS UntrackedWithGaps,
                        SUM(MissingDays) AS TotalMissingDays
                    FROM AllGaps";
            }
            else
            {
                // Tracked-only: lean count query — no SecuritiesWithPrices scan
                gapCountCmd.CommandText = @"
                    WITH SecurityDateRange AS (
                        SELECT sm.SecurityAlias, sm.IsTracked,
                               MIN(p.EffectiveDate) AS FirstDate, MAX(p.EffectiveDate) AS LastDate,
                               COUNT(*) AS ActualPriceCount
                        FROM data.SecurityMaster sm WITH (NOLOCK)
                        INNER JOIN data.Prices p WITH (NOLOCK) ON p.SecurityAlias = sm.SecurityAlias
                        WHERE sm.IsActive = 1 AND sm.IsEodhdUnavailable = 0 AND sm.IsTracked = 1
                        GROUP BY sm.SecurityAlias, sm.IsTracked
                    ),
                    ExpectedCounts AS (
                        SELECT sdr.SecurityAlias, sdr.IsTracked, sdr.ActualPriceCount,
                               (SELECT COUNT(*) FROM data.BusinessCalendar bc WITH (NOLOCK)
                                WHERE bc.IsBusinessDay = 1 AND bc.EffectiveDate >= sdr.FirstDate
                                  AND bc.EffectiveDate <= sdr.LastDate) AS ExpectedPriceCount
                        FROM SecurityDateRange sdr
                    )
                    SELECT
                        SUM(CASE WHEN ExpectedPriceCount > ActualPriceCount THEN 1 ELSE 0 END) AS TrackedWithGaps,
                        0 AS UntrackedWithGaps,
                        SUM(CASE WHEN ExpectedPriceCount > ActualPriceCount
                            THEN ExpectedPriceCount - ActualPriceCount ELSE 0 END) AS TotalMissingDays
                    FROM ExpectedCounts";
            }

            gapCountCmd.CommandTimeout = includeUntrackedSecurities ? 300 : 120;

            using var gapCountReader = await gapCountCmd.ExecuteReaderAsync();
            if (await gapCountReader.ReadAsync())
            {
                totalTrackedWithGaps = gapCountReader.IsDBNull(0) ? 0 : gapCountReader.GetInt32(0);
                totalUntrackedWithGaps = gapCountReader.IsDBNull(1) ? 0 : gapCountReader.GetInt32(1);
                totalAllMissingDays = gapCountReader.IsDBNull(2) ? 0 : gapCountReader.GetInt32(2);
            }
        }

        using (var statsCmd = connection.CreateCommand())
        {
            if (includeUntrackedSecurities)
            {
                statsCmd.CommandText = @"
                    SELECT
                        (SELECT COUNT(*) FROM data.SecurityMaster WITH (NOLOCK)
                         WHERE IsActive = 1 AND IsEodhdUnavailable = 0) AS TotalSecurities,
                        (SELECT COUNT(DISTINCT p.SecurityAlias) FROM data.Prices p WITH (NOLOCK)
                         INNER JOIN data.SecurityMaster sm WITH (NOLOCK) ON sm.SecurityAlias = p.SecurityAlias
                         WHERE sm.IsActive = 1 AND sm.IsEodhdUnavailable = 0) AS SecuritiesWithData,
                        (SELECT COUNT(*) FROM data.Prices p WITH (NOLOCK)
                         INNER JOIN data.SecurityMaster sm WITH (NOLOCK) ON sm.SecurityAlias = p.SecurityAlias
                         WHERE sm.IsActive = 1 AND sm.IsEodhdUnavailable = 0) AS TotalPriceRecords,
                        (SELECT COUNT(*) FROM data.SecurityMaster WITH (NOLOCK)
                         WHERE IsActive = 1 AND IsEodhdUnavailable = 0 AND IsTracked = 1) AS TotalTrackedSecurities,
                        (SELECT COUNT(*) FROM data.SecurityMaster WITH (NOLOCK)
                         WHERE IsActive = 1 AND IsEodhdUnavailable = 0 AND IsTracked = 0) AS TotalUntrackedSecurities";
            }
            else
            {
                // Tracked-only: avoid COUNT(DISTINCT) and COUNT(*) on full Prices table
                statsCmd.CommandText = @"
                    SELECT
                        (SELECT COUNT(*) FROM data.SecurityMaster WITH (NOLOCK)
                         WHERE IsActive = 1 AND IsEodhdUnavailable = 0 AND IsTracked = 1) AS TotalSecurities,
                        (SELECT COUNT(DISTINCT p.SecurityAlias) FROM data.Prices p WITH (NOLOCK)
                         INNER JOIN data.SecurityMaster sm WITH (NOLOCK) ON sm.SecurityAlias = p.SecurityAlias
                         WHERE sm.IsActive = 1 AND sm.IsEodhdUnavailable = 0 AND sm.IsTracked = 1) AS SecuritiesWithData,
                        (SELECT COUNT(*) FROM data.Prices p WITH (NOLOCK)
                         INNER JOIN data.SecurityMaster sm WITH (NOLOCK) ON sm.SecurityAlias = p.SecurityAlias
                         WHERE sm.IsActive = 1 AND sm.IsEodhdUnavailable = 0 AND sm.IsTracked = 1) AS TotalPriceRecords,
                        (SELECT COUNT(*) FROM data.SecurityMaster WITH (NOLOCK)
                         WHERE IsActive = 1 AND IsEodhdUnavailable = 0 AND IsTracked = 1) AS TotalTrackedSecurities,
                        0 AS TotalUntrackedSecurities";
            }

            statsCmd.CommandTimeout = includeUntrackedSecurities ? 180 : 60;

            using var statsReader = await statsCmd.ExecuteReaderAsync();
            if (await statsReader.ReadAsync())
            {
                totalSecurities = statsReader.GetInt32(0);
                securitiesWithData = statsReader.GetInt32(1);
                totalPriceRecords = statsReader.GetInt32(2);
                totalTrackedSecurities = statsReader.GetInt32(3);
                totalUntrackedSecurities = statsReader.GetInt32(4);
            }
        }

        var totalGapsCount = totalTrackedWithGaps + totalUntrackedWithGaps;
        var securitiesComplete = totalGapsCount == 0 ? securitiesWithData :
            securitiesWithData - totalGapsCount;

        return Results.Ok(new
        {
            success = true,
            market = marketFilter,
            includeUntracked = includeUntrackedSecurities,
            summary = new
            {
                totalSecurities,
                totalTrackedSecurities,
                totalUntrackedSecurities,
                securitiesWithData,
                securitiesWithGaps = totalGapsCount,
                trackedWithGaps = totalTrackedWithGaps,
                untrackedWithGaps = totalUntrackedWithGaps,
                securitiesComplete,
                totalPriceRecords,
                totalMissingDays = totalAllMissingDays
            },
            completionPercent = totalSecurities > 0
                ? Math.Round(securitiesComplete * 100.0 / totalSecurities, 1)
                : 0,
            gaps = securitiesWithGaps
        });
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Price gaps query failed");
        return Results.Problem(ex.Message);
    }
})
.WithName("PriceGaps")
.WithOpenApi()
.Produces(StatusCodes.Status200OK)
.Produces(StatusCodes.Status400BadRequest)
.Produces(StatusCodes.Status500InternalServerError);

// GET /api/admin/prices/gaps/{securityAlias} - Get specific missing dates for a security
app.MapGet("/api/admin/prices/gaps/{securityAlias}", async (IServiceProvider serviceProvider, int securityAlias, int? limit) =>
{
    using var scope = serviceProvider.CreateScope();
    var context = scope.ServiceProvider.GetService<StockAnalyzerDbContext>();

    if (context == null)
        return Results.BadRequest(new { error = "Database context not configured" });

    try
    {
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        var maxResults = limit ?? 100;

        // Get security info and date range
        string? ticker = null;
        DateTime? firstDate = null;
        DateTime? lastDate = null;

        using (var infoCmd = connection.CreateCommand())
        {
            infoCmd.CommandText = @"
                SELECT
                    sm.TickerSymbol,
                    MIN(p.EffectiveDate) AS FirstDate,
                    MAX(p.EffectiveDate) AS LastDate
                FROM data.SecurityMaster sm WITH (NOLOCK)
                LEFT JOIN data.Prices p WITH (NOLOCK) ON p.SecurityAlias = sm.SecurityAlias
                WHERE sm.SecurityAlias = @securityAlias
                GROUP BY sm.TickerSymbol";

            var aliasParam = infoCmd.CreateParameter();
            aliasParam.ParameterName = "@securityAlias";
            aliasParam.Value = securityAlias;
            infoCmd.Parameters.Add(aliasParam);
            infoCmd.CommandTimeout = 30;

            using var infoReader = await infoCmd.ExecuteReaderAsync();
            if (await infoReader.ReadAsync())
            {
                ticker = infoReader.GetString(0);
                if (!infoReader.IsDBNull(1))
                {
                    firstDate = infoReader.GetDateTime(1);
                    lastDate = infoReader.GetDateTime(2);
                }
            }
        }

        if (ticker == null)
            return Results.NotFound(new { error = $"Security {securityAlias} not found" });

        // For securities with no price data, use last 2 years as the date range
        if (firstDate == null)
        {
            firstDate = DateTime.Today.AddYears(-2);
            lastDate = DateTime.Today;
        }

        // Never look for gaps beyond today - future dates cannot have price data
        if (lastDate > DateTime.Today)
        {
            lastDate = DateTime.Today;
        }

        // Find missing trading days within the security's date range
        var missingDates = new List<string>();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT TOP (@limit) bc.EffectiveDate
            FROM data.BusinessCalendar bc WITH (NOLOCK)
            WHERE bc.IsBusinessDay = 1
              AND bc.EffectiveDate >= @firstDate
              AND bc.EffectiveDate <= @lastDate
              AND NOT EXISTS (
                SELECT 1 FROM data.Prices p WITH (NOLOCK)
                WHERE p.SecurityAlias = @securityAlias
                  AND p.EffectiveDate = bc.EffectiveDate
              )
            ORDER BY bc.EffectiveDate DESC";

        var limitParam = cmd.CreateParameter();
        limitParam.ParameterName = "@limit";
        limitParam.Value = maxResults;
        cmd.Parameters.Add(limitParam);

        var firstParam = cmd.CreateParameter();
        firstParam.ParameterName = "@firstDate";
        firstParam.Value = firstDate!.Value;  // Null check at line 1671 ensures this is non-null
        cmd.Parameters.Add(firstParam);

        var lastParam = cmd.CreateParameter();
        lastParam.ParameterName = "@lastDate";
        lastParam.Value = lastDate!.Value;  // Set alongside firstDate at line 1663
        cmd.Parameters.Add(lastParam);

        var secParam = cmd.CreateParameter();
        secParam.ParameterName = "@securityAlias";
        secParam.Value = securityAlias;
        cmd.Parameters.Add(secParam);

        cmd.CommandTimeout = 30;

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            missingDates.Add(reader.GetDateTime(0).ToString("yyyy-MM-dd"));
        }

        return Results.Ok(new
        {
            success = true,
            securityAlias,
            ticker,
            firstDate = firstDate.Value.ToString("yyyy-MM-dd"),
            lastDate = lastDate.Value.ToString("yyyy-MM-dd"),
            missingCount = missingDates.Count,
            missingDates
        });
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Security gaps query failed for {SecurityAlias}", securityAlias);
        return Results.Problem(ex.Message);
    }
})
.WithName("SecurityGaps")
.WithOpenApi()
.Produces(StatusCodes.Status200OK)
.Produces(StatusCodes.Status404NotFound)
.Produces(StatusCodes.Status500InternalServerError);

// POST /api/admin/prices/mark-eodhd-unavailable - Mark a security as having no EODHD data available
// Called by the crawler when EODHD returns no data for a ticker
app.MapPost("/api/admin/prices/mark-eodhd-unavailable/{securityAlias}", async (IServiceProvider serviceProvider, int securityAlias) =>
{
    using var scope = serviceProvider.CreateScope();
    var context = scope.ServiceProvider.GetService<StockAnalyzerDbContext>();

    if (context == null)
        return Results.BadRequest(new { error = "Database context not configured" });

    try
    {
        var security = await context.SecurityMaster.FindAsync(securityAlias);

        if (security == null)
            return Results.NotFound(new { error = $"Security {securityAlias} not found" });

        if (security.IsEodhdUnavailable)
        {
            return Results.Ok(new
            {
                success = true,
                message = $"Security {security.TickerSymbol} was already marked as EODHD unavailable",
                ticker = security.TickerSymbol,
                securityAlias
            });
        }

        security.IsEodhdUnavailable = true;
        security.UpdatedAt = DateTime.UtcNow;
        await context.SaveChangesAsync();

        Log.Information("Marked {Ticker} (alias {Alias}) as EODHD unavailable", security.TickerSymbol, securityAlias);

        return Results.Ok(new
        {
            success = true,
            message = $"Marked {security.TickerSymbol} as EODHD unavailable - will be skipped in future gap-filling",
            ticker = security.TickerSymbol,
            securityAlias
        });
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Failed to mark security {SecurityAlias} as EODHD unavailable", securityAlias);
        return Results.Problem(ex.Message);
    }
})
.WithName("MarkEodhdUnavailable")
.WithOpenApi()
.Produces(StatusCodes.Status200OK)
.Produces(StatusCodes.Status404NotFound)
.Produces(StatusCodes.Status500InternalServerError);

// POST /api/admin/securities/reset-unavailable - Reset IsEodhdUnavailable flag for all or recently-marked securities
// Use this to roll back incorrect unavailable markings (e.g., caused by future-date bug)
app.MapPost("/api/admin/securities/reset-unavailable", async (IServiceProvider serviceProvider, HttpContext httpContext) =>
{
    using var scope = serviceProvider.CreateScope();
    var context = scope.ServiceProvider.GetService<StockAnalyzerDbContext>();

    if (context == null)
        return Results.BadRequest(new { error = "Database context not configured" });

    try
    {
        // Optional: only reset securities marked unavailable within the last N days
        var daysBack = 7; // default: last 7 days
        if (httpContext.Request.Query.TryGetValue("days", out var daysStr) && int.TryParse(daysStr, out var d))
            daysBack = d;

        var resetAll = httpContext.Request.Query.ContainsKey("all");

        List<object> resetSecurities;

        if (resetAll)
        {
            // Reset ALL unavailable securities
            var unavailable = await context.SecurityMaster
                .Where(s => s.IsEodhdUnavailable && s.IsActive)
                .ToListAsync();

            foreach (var sec in unavailable)
            {
                sec.IsEodhdUnavailable = false;
                sec.UpdatedAt = DateTime.UtcNow;
            }

            await context.SaveChangesAsync();

            resetSecurities = unavailable.Select(s => (object)new
            {
                securityAlias = s.SecurityAlias,
                ticker = s.TickerSymbol,
                exchange = s.Exchange
            }).ToList();

            Log.Information("Reset IsEodhdUnavailable for ALL {Count} securities", unavailable.Count);
        }
        else
        {
            // Reset securities marked unavailable within the last N days (by UpdatedAt)
            var cutoff = DateTime.UtcNow.AddDays(-daysBack);
            var recentlyMarked = await context.SecurityMaster
                .Where(s => s.IsEodhdUnavailable && s.IsActive && s.UpdatedAt >= cutoff)
                .ToListAsync();

            foreach (var sec in recentlyMarked)
            {
                sec.IsEodhdUnavailable = false;
                sec.UpdatedAt = DateTime.UtcNow;
            }

            await context.SaveChangesAsync();

            resetSecurities = recentlyMarked.Select(s => (object)new
            {
                securityAlias = s.SecurityAlias,
                ticker = s.TickerSymbol,
                exchange = s.Exchange
            }).ToList();

            Log.Information("Reset IsEodhdUnavailable for {Count} securities marked in last {Days} days", recentlyMarked.Count, daysBack);
        }

        return Results.Ok(new
        {
            success = true,
            message = resetAll
                ? $"Reset {resetSecurities.Count} unavailable securities (all)"
                : $"Reset {resetSecurities.Count} securities marked unavailable in last {daysBack} days",
            count = resetSecurities.Count,
            securities = resetSecurities
        });
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Failed to reset unavailable securities");
        return Results.Problem(ex.Message);
    }
})
.WithName("ResetUnavailableSecurities")
.WithOpenApi()
.Produces(StatusCodes.Status200OK)
.Produces(StatusCodes.Status500InternalServerError);

// POST /api/admin/securities/calculate-importance - Calculate importance scores for untracked securities
// Scores are based on security type, exchange, ticker length, and name patterns (1-10 scale, 10=most important)
app.MapPost("/api/admin/securities/calculate-importance", async (IServiceProvider serviceProvider) =>
{
    using var scope = serviceProvider.CreateScope();
    var context = scope.ServiceProvider.GetService<StockAnalyzerDbContext>();

    if (context == null)
        return Results.BadRequest(new { error = "Database context not configured" });

    // Local function to calculate importance score
    int CalculateImportanceScore(SecurityMasterEntity sec)
    {
        var score = 5; // Base score

        // Security Type scoring
        var secType = sec.SecurityType?.ToUpperInvariant() ?? "";
        if (secType.Contains("COMMON STOCK"))
            score += 2;
        else if (secType.Contains("ETF") || secType.Contains("EXCHANGE TRADED"))
            score += 1;
        else if (secType.Contains("PREFERRED") || secType.Contains("WARRANT") || secType.Contains("RIGHT"))
            score -= 2;
        // OTC indicators in security type
        if (secType.Contains("OTC") || secType.Contains("PINK") || secType.Contains("GREY"))
            score -= 3;

        // Exchange scoring
        var exchange = sec.Exchange?.ToUpperInvariant() ?? "";
        if (exchange.Contains("NYSE") && !exchange.Contains("ARCA"))
            score += 2;
        else if (exchange.Contains("NASDAQ"))
            score += 2;
        else if (exchange.Contains("ARCA") || exchange.Contains("BATS") || exchange.Contains("IEX"))
            score += 1;
        else if (exchange.Contains("OTC") || exchange.Contains("PINK") || exchange.Contains("GREY"))
            score -= 2;
        else if (string.IsNullOrEmpty(exchange))
            score -= 1;

        // Ticker length scoring (shorter = typically major exchanges)
        var tickerLen = sec.TickerSymbol?.Length ?? 0;
        if (tickerLen >= 1 && tickerLen <= 3)
            score += 1;
        else if (tickerLen >= 5)
            score -= 1;

        // Name pattern scoring
        var name = sec.IssueName?.ToUpperInvariant() ?? "";
        // Positive patterns (established companies)
        if (name.Contains(" INC") || name.Contains(" CORP") || name.Contains(" LTD") || name.Contains(" CO ") || name.Contains(" COMPANY"))
            score += 1;
        // Negative patterns (special securities)
        if (name.Contains("WARRANT") || name.Contains("RIGHT") || name.Contains(" UNIT") || name.Contains("UNITS"))
            score -= 2;
        // Strong negative patterns (distressed/special situations)
        if (name.Contains("LIQUIDATING") || name.Contains("BANKRUPT") || name.Contains("LIQUIDATION"))
            score -= 3;

        // Clamp to 1-10
        return Math.Clamp(score, 1, 10);
    }

    try
    {
        Log.Information("Starting importance score calculation for all active securities");

        // Get all active securities (tracked + untracked) so heatmap has proper score distribution
        var untrackedSecurities = await context.SecurityMaster
            .Where(s => s.IsActive)
            .ToListAsync();

        if (!untrackedSecurities.Any())
        {
            return Results.Ok(new
            {
                success = true,
                message = "No active securities found",
                updated = 0,
                distribution = new Dictionary<int, int>()
            });
        }

        var scoreDistribution = new Dictionary<int, int>();
        var updated = 0;

        foreach (var security in untrackedSecurities)
        {
            var score = CalculateImportanceScore(security);

            if (security.ImportanceScore != score)
            {
                security.ImportanceScore = score;
                security.UpdatedAt = DateTime.UtcNow;
                updated++;
            }

            // Track distribution
            if (!scoreDistribution.ContainsKey(score))
                scoreDistribution[score] = 0;
            scoreDistribution[score]++;
        }

        await context.SaveChangesAsync();

        Log.Information("Updated importance scores for {Count} securities", updated);

        return Results.Ok(new
        {
            success = true,
            message = $"Calculated importance scores for {untrackedSecurities.Count} active securities",
            totalProcessed = untrackedSecurities.Count,
            updated,
            distribution = scoreDistribution.OrderByDescending(x => x.Key).ToDictionary(x => x.Key, x => x.Value)
        });
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Failed to calculate importance scores");
        return Results.Problem(ex.Message);
    }
})
.WithName("CalculateImportanceScores")
.WithOpenApi()
.Produces(StatusCodes.Status200OK)
.Produces(StatusCodes.Status400BadRequest)
.Produces(StatusCodes.Status500InternalServerError);

// GET /api/admin/dashboard/stats - Consolidated dashboard stats for EODHD Loader
// Result sets 1-3 use direct SQL (fast queries on SecurityMaster + indexed Prices).
// Result sets 4-5 read from CoverageSummary table (instant, avoids full Prices scan).
// All results cached for 10 minutes via IMemoryCache.
app.MapGet("/api/admin/dashboard/stats", async (IServiceProvider serviceProvider, IMemoryCache cache) =>
{
    const string cacheKey = "dashboard:stats";
    if (cache.TryGetValue(cacheKey, out object? cached) && cached != null)
        return Results.Ok(cached);

    using var scope = serviceProvider.CreateScope();
    var context = scope.ServiceProvider.GetService<StockAnalyzerDbContext>();

    if (context == null)
        return Results.BadRequest(new { error = "Database context not configured" });

    try
    {
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        // Result sets 1-3: Direct SQL (fast - SecurityMaster is small, Prices COUNT uses index)
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            -- Universe counts
            SELECT
                COUNT(*) AS TotalSecurities,
                SUM(CASE WHEN IsTracked = 1 THEN 1 ELSE 0 END) AS Tracked,
                SUM(CASE WHEN IsTracked = 0 THEN 1 ELSE 0 END) AS Untracked,
                SUM(CASE WHEN IsEodhdUnavailable = 1 THEN 1 ELSE 0 END) AS Unavailable
            FROM data.SecurityMaster WITH (NOLOCK)
            WHERE IsActive = 1;

            -- Price record counts
            SELECT
                COUNT(*) AS TotalRecords,
                COUNT(DISTINCT SecurityAlias) AS DistinctSecurities,
                MIN(EffectiveDate) AS OldestDate,
                MAX(EffectiveDate) AS LatestDate
            FROM data.Prices WITH (NOLOCK);

            -- ImportanceScore tier distribution with completion status
            SELECT
                sm.ImportanceScore,
                COUNT(*) AS Total,
                SUM(CASE WHEN p.SecurityAlias IS NOT NULL THEN 1 ELSE 0 END) AS WithPrices,
                SUM(CASE WHEN sm.IsEodhdUnavailable = 1 THEN 1 ELSE 0 END) AS Unavailable
            FROM data.SecurityMaster sm WITH (NOLOCK)
            LEFT JOIN (SELECT DISTINCT SecurityAlias FROM data.Prices WITH (NOLOCK)) p
                ON p.SecurityAlias = sm.SecurityAlias
            WHERE sm.IsActive = 1 AND sm.IsTracked = 0
            GROUP BY sm.ImportanceScore
            ORDER BY sm.ImportanceScore DESC;
        ";
        cmd.CommandTimeout = 60;

        object? universeData = null;
        object? pricesData = null;
        var tiers = new List<object>();

        using var reader = await cmd.ExecuteReaderAsync();

        // Result set 1: Universe counts
        if (await reader.ReadAsync())
        {
            universeData = new
            {
                totalSecurities = reader.GetInt32(0),
                tracked = reader.GetInt32(1),
                untracked = reader.GetInt32(2),
                unavailable = reader.GetInt32(3)
            };
        }

        // Result set 2: Price records
        if (await reader.NextResultAsync() && await reader.ReadAsync())
        {
            pricesData = new
            {
                totalRecords = reader.GetInt32(0),
                distinctSecurities = reader.GetInt32(1),
                oldestDate = reader.IsDBNull(2) ? null : reader.GetDateTime(2).ToString("yyyy-MM-dd"),
                latestDate = reader.IsDBNull(3) ? null : reader.GetDateTime(3).ToString("yyyy-MM-dd")
            };
        }

        // Result set 3: ImportanceScore tiers
        if (await reader.NextResultAsync())
        {
            while (await reader.ReadAsync())
            {
                tiers.Add(new
                {
                    score = reader.GetInt32(0),
                    total = reader.GetInt32(1),
                    withPrices = reader.GetInt32(2),
                    unavailable = reader.GetInt32(3)
                });
            }
        }

        // Close reader before querying CoverageSummary via EF (can't have two open readers)
        await reader.CloseAsync();

        // Result sets 4-5: Read from CoverageSummary table (instant, avoids YEAR() full scan)
        var summaryRows = await context.CoverageSummary
            .OrderByDescending(s => s.Year)
            .ToListAsync();

        // Build decade coverage from summary
        var decades = summaryRows
            .GroupBy(r => (r.Year / 10) * 10)
            .OrderByDescending(g => g.Key)
            .Select(g => new
            {
                decade = $"{g.Key}s",
                records = g.Sum(r => r.TrackedRecords + r.UntrackedRecords),
                securities = g.Sum(r => r.TrackedSecurities + r.UntrackedSecurities),
                tradingDays = g.Max(r => r.TradingDays),
                firstDate = $"{g.Key}-01-01",
                lastDate = $"{g.Key + 9}-12-31"
            })
            .ToList();

        // Build year coverage from summary
        var years = summaryRows
            .GroupBy(r => r.Year)
            .OrderByDescending(g => g.Key)
            .Select(g => new
            {
                year = g.Key,
                records = g.Sum(r => r.TrackedRecords + r.UntrackedRecords),
                securities = g.Sum(r => r.TrackedSecurities + r.UntrackedSecurities),
                tradingDays = g.Max(r => r.TradingDays)
            })
            .ToList();

        var result = new
        {
            success = true,
            timestamp = DateTime.UtcNow.ToString("o"),
            universe = universeData,
            prices = pricesData,
            importanceTiers = tiers,
            coverageByDecade = decades,
            coverageByYear = years
        };

        cache.Set(cacheKey, result, TimeSpan.FromMinutes(10));
        return Results.Ok(result);
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message);
    }
})
.WithName("GetDashboardStats")
.WithOpenApi()
.Produces(StatusCodes.Status200OK);

// GET /api/admin/dashboard/heatmap - Bivariate heatmap data (Year x ImportanceScore)
// Reads from pre-aggregated CoverageSummary table for instant response on Azure SQL Basic tier.
// Call POST /api/admin/dashboard/refresh-summary to populate/refresh the summary table.
app.MapGet("/api/admin/dashboard/heatmap", async (IServiceProvider serviceProvider, IMemoryCache cache) =>
{
    const string cacheKey = "dashboard:heatmap";
    if (cache.TryGetValue(cacheKey, out object? cached) && cached != null)
        return Results.Ok(cached);

    using var scope = serviceProvider.CreateScope();
    var context = scope.ServiceProvider.GetService<StockAnalyzerDbContext>();

    if (context == null)
        return Results.BadRequest(new { error = "Database context not configured" });

    try
    {
        var summaryRows = await context.CoverageSummary
            .OrderBy(s => s.Year).ThenBy(s => s.ImportanceScore)
            .ToListAsync();

        if (!summaryRows.Any())
        {
            var emptyResult = new
            {
                success = true,
                cells = new List<object>(),
                metadata = new { minYear = 0, maxYear = 0, totalCells = 0, maxTrackedRecords = 0L, maxUntrackedRecords = 0L },
                stale = true,
                message = "Summary table empty. Call POST /api/admin/dashboard/refresh-summary to populate."
            };
            return Results.Ok(emptyResult);
        }

        var cells = summaryRows.Select(r => new
        {
            year = r.Year,
            score = r.ImportanceScore,
            trackedRecords = r.TrackedRecords,
            untrackedRecords = r.UntrackedRecords,
            trackedSecurities = r.TrackedSecurities,
            untrackedSecurities = r.UntrackedSecurities
        }).ToList();

        var result = new
        {
            success = true,
            cells,
            metadata = new
            {
                minYear = summaryRows.Min(r => r.Year),
                maxYear = summaryRows.Max(r => r.Year),
                totalCells = summaryRows.Count,
                maxTrackedRecords = summaryRows.Max(r => r.TrackedRecords),
                maxUntrackedRecords = summaryRows.Max(r => r.UntrackedRecords)
            }
        };

        cache.Set(cacheKey, result, TimeSpan.FromMinutes(30));
        return Results.Ok(result);
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message);
    }
})
.WithName("GetDashboardHeatmap")
.WithOpenApi()
.Produces(StatusCodes.Status200OK);

// POST /api/admin/dashboard/refresh-summary - Refresh the CoverageSummary pre-aggregation table
// Runs the expensive aggregation query on data.Prices (may take 2-5 min on Azure SQL Basic tier).
// Call after deployments and after crawl sessions to keep heatmap data current.
app.MapPost("/api/admin/dashboard/refresh-summary", async (IServiceProvider serviceProvider, IMemoryCache cache) =>
{
    using var scope = serviceProvider.CreateScope();
    var context = scope.ServiceProvider.GetService<StockAnalyzerDbContext>();

    if (context == null)
        return Results.BadRequest(new { error = "Database context not configured" });

    try
    {
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT
                YEAR(p.EffectiveDate) AS [Year],
                sm.ImportanceScore AS Score,
                SUM(CASE WHEN sm.IsTracked = 1 THEN 1 ELSE 0 END) AS TrackedRecords,
                SUM(CASE WHEN sm.IsTracked = 0 THEN 1 ELSE 0 END) AS UntrackedRecords,
                COUNT(DISTINCT CASE WHEN sm.IsTracked = 1 THEN sm.SecurityAlias END) AS TrackedSecurities,
                COUNT(DISTINCT CASE WHEN sm.IsTracked = 0 THEN sm.SecurityAlias END) AS UntrackedSecurities,
                COUNT(DISTINCT p.EffectiveDate) AS TradingDays
            FROM data.Prices p WITH (NOLOCK)
            INNER JOIN data.SecurityMaster sm WITH (NOLOCK)
                ON p.SecurityAlias = sm.SecurityAlias
            WHERE sm.IsActive = 1
            GROUP BY YEAR(p.EffectiveDate), sm.ImportanceScore
            ORDER BY [Year], Score;
        ";
        cmd.CommandTimeout = 300; // 5 min — this is the expensive query, runs infrequently

        var rows = new List<CoverageSummaryEntity>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new CoverageSummaryEntity
            {
                Year = reader.GetInt32(0),
                ImportanceScore = reader.GetInt32(1),
                TrackedRecords = reader.GetInt32(2),
                UntrackedRecords = reader.GetInt32(3),
                TrackedSecurities = reader.GetInt32(4),
                UntrackedSecurities = reader.GetInt32(5),
                TradingDays = reader.GetInt32(6),
                LastUpdatedAt = DateTime.UtcNow
            });
        }

        // Close the reader before executing DML
        await reader.CloseAsync();

        // Delete existing rows and re-insert
        await context.Database.ExecuteSqlRawAsync("DELETE FROM data.CoverageSummary");
        context.CoverageSummary.AddRange(rows);
        await context.SaveChangesAsync();

        // Invalidate caches
        cache.Remove("dashboard:heatmap");
        cache.Remove("dashboard:stats");

        Log.Information("Refreshed CoverageSummary: {CellCount} cells", rows.Count);

        return Results.Ok(new
        {
            success = true,
            message = $"Refreshed {rows.Count} summary cells",
            cellCount = rows.Count
        });
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Failed to refresh coverage summary");
        return Results.Problem(ex.Message);
    }
})
.WithName("RefreshDashboardSummary")
.WithOpenApi()
.Produces(StatusCodes.Status200OK);

// GET /api/admin/prices/holidays/analyze - Analyze holidays missing price data
app.MapGet("/api/admin/prices/holidays/analyze", async (IServiceProvider serviceProvider) =>
{
    using var scope = serviceProvider.CreateScope();
    var priceRepo = scope.ServiceProvider.GetService<IPriceRepository>();

    if (priceRepo == null)
        return Results.BadRequest(new { error = "Price repository not configured" });

    try
    {
        var result = await priceRepo.AnalyzeHolidaysAsync();

        if (!result.Success)
            return Results.Problem(result.Error ?? "Analysis failed");

        return Results.Ok(new
        {
            success = true,
            dataStartDate = result.DataStartDate.ToString("yyyy-MM-dd"),
            dataEndDate = result.DataEndDate.ToString("yyyy-MM-dd"),
            totalDatesWithData = result.TotalDatesWithData,
            missingHolidayCount = result.MissingHolidays.Count,
            holidaysWithPriorData = result.HolidaysWithPriorData,
            holidaysWithoutPriorData = result.HolidaysWithoutPriorData,
            missingHolidays = result.MissingHolidays.Select(h => new
            {
                name = h.HolidayName,
                date = h.HolidayDate.ToString("yyyy-MM-dd"),
                priorTradingDay = h.PriorTradingDay.ToString("yyyy-MM-dd"),
                hasPriorData = h.HasPriorDayData
            })
        });
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Holiday analysis failed");
        return Results.Problem(ex.Message);
    }
})
.WithName("AnalyzeHolidays")
.WithOpenApi()
.Produces(StatusCodes.Status200OK)
.Produces(StatusCodes.Status400BadRequest)
.Produces(StatusCodes.Status500InternalServerError);

// POST /api/admin/prices/holidays/forward-fill - Forward-fill price data for US market holidays
// Add ?limit=N to process in batches (for long-running operations that would timeout)
app.MapPost("/api/admin/prices/holidays/forward-fill", async (IServiceProvider serviceProvider, int? limit) =>
{
    using var scope = serviceProvider.CreateScope();
    var priceRepo = scope.ServiceProvider.GetService<IPriceRepository>();

    if (priceRepo == null)
        return Results.BadRequest(new { error = "Price repository not configured" });

    try
    {
        Log.Information("Starting holiday forward-fill via API (limit: {Limit})", limit ?? -1);
        var result = await priceRepo.ForwardFillHolidaysAsync(limit);

        if (!result.Success)
            return Results.Problem(result.Error ?? "Forward-fill failed");

        return Results.Ok(new
        {
            success = true,
            message = result.Message,
            holidaysProcessed = result.HolidaysProcessed,
            totalRecordsInserted = result.TotalRecordsInserted,
            remainingDays = result.RemainingDays,
            holidaysFilled = result.HolidaysFilled.Select(h => new
            {
                name = h.HolidayName,
                date = h.Date.ToString("yyyy-MM-dd"),
                recordsInserted = h.RecordsInserted
            })
        });
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Holiday forward-fill failed");
        return Results.Problem(ex.Message);
    }
})
.WithName("ForwardFillHolidays")
.WithOpenApi()
.Produces(StatusCodes.Status200OK)
.Produces(StatusCodes.Status400BadRequest)
.Produces(StatusCodes.Status500InternalServerError);

// POST /api/admin/calendar/populate-us - Populate US business calendar
app.MapPost("/api/admin/calendar/populate-us", async (
    StockAnalyzerDbContext dbContext,
    string? startDate,
    string? endDate) =>
{
    try
    {
        // Default range: 1980-01-01 to 2030-12-31
        var start = DateTime.TryParse(startDate, out var s) ? DateOnly.FromDateTime(s) : new DateOnly(1980, 1, 1);
        var end = DateTime.TryParse(endDate, out var e) ? DateOnly.FromDateTime(e) : new DateOnly(2030, 12, 31);

        Log.Information("Populating US business calendar from {Start} to {End}", start, end);

        // Ensure US source exists
        var usSource = await dbContext.Sources.FirstOrDefaultAsync(s => s.SourceId == 1);
        if (usSource == null)
        {
            dbContext.Sources.Add(new SourceEntity
            {
                SourceId = 1,
                SourceShortName = "US",
                SourceLongName = "US Business Day Calendar"
            });
            await dbContext.SaveChangesAsync();
        }

        // Delete existing entries in range (for re-population)
        var existingCount = await dbContext.BusinessCalendar
            .Where(bc => bc.SourceId == 1 && bc.EffectiveDate >= start.ToDateTime(TimeOnly.MinValue) && bc.EffectiveDate <= end.ToDateTime(TimeOnly.MinValue))
            .ExecuteDeleteAsync();

        Log.Information("Deleted {Count} existing calendar entries", existingCount);

        // Generate all dates in range
        var entries = new List<BusinessCalendarEntity>();
        var currentMonth = new DateOnly(start.Year, start.Month, 1);

        for (var date = start; date <= end; date = date.AddDays(1))
        {
            var isWeekday = date.DayOfWeek != DayOfWeek.Saturday && date.DayOfWeek != DayOfWeek.Sunday;
            var isBusinessDay = UsMarketCalendar.IsTradingDay(date);
            var isHoliday = isWeekday && !isBusinessDay; // Weekday non-trading day = holiday
            var isMonthEnd = date == new DateOnly(date.Year, date.Month, DateTime.DaysInMonth(date.Year, date.Month));

            entries.Add(new BusinessCalendarEntity
            {
                SourceId = 1,
                EffectiveDate = date.ToDateTime(TimeOnly.MinValue),
                IsBusinessDay = isBusinessDay,
                IsHoliday = isHoliday,
                IsMonthEnd = isMonthEnd,
                IsLastBusinessDayMonthEnd = false // Will calculate in second pass
            });
        }

        // Calculate last business day of each month
        var entriesByMonth = entries.GroupBy(e => new { e.EffectiveDate.Year, e.EffectiveDate.Month });
        foreach (var month in entriesByMonth)
        {
            var lastBusinessDay = month
                .Where(e => e.IsBusinessDay)
                .OrderByDescending(e => e.EffectiveDate)
                .FirstOrDefault();

            if (lastBusinessDay != null)
            {
                lastBusinessDay.IsLastBusinessDayMonthEnd = true;
            }
        }

        // Batch insert
        dbContext.BusinessCalendar.AddRange(entries);
        await dbContext.SaveChangesAsync();

        var businessDays = entries.Count(e => e.IsBusinessDay);
        var holidays = entries.Count(e => e.IsHoliday);

        Log.Information("Populated US calendar: {Total} dates, {BusinessDays} business days, {Holidays} holidays",
            entries.Count, businessDays, holidays);

        return Results.Ok(new
        {
            success = true,
            startDate = start.ToString("yyyy-MM-dd"),
            endDate = end.ToString("yyyy-MM-dd"),
            totalDates = entries.Count,
            businessDays,
            weekends = entries.Count - businessDays - holidays,
            holidays,
            monthEndDates = entries.Count(e => e.IsMonthEnd),
            lastBusinessDayMonthEnds = entries.Count(e => e.IsLastBusinessDayMonthEnd)
        });
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Failed to populate US business calendar");
        return Results.Problem(ex.Message);
    }
})
.WithName("PopulateUsBusinessCalendar")
.WithOpenApi()
.Produces(StatusCodes.Status200OK)
.Produces(StatusCodes.Status500InternalServerError);

// GET /api/admin/calendar/us - Get US business calendar info
app.MapGet("/api/admin/calendar/us", async (
    StockAnalyzerDbContext dbContext,
    string? startDate,
    string? endDate) =>
{
    var start = DateTime.TryParse(startDate, out var s) ? s : DateTime.Today.AddYears(-1);
    var end = DateTime.TryParse(endDate, out var e) ? e : DateTime.Today;

    var entries = await dbContext.BusinessCalendar
        .Where(bc => bc.SourceId == 1 && bc.EffectiveDate >= start && bc.EffectiveDate <= end)
        .OrderBy(bc => bc.EffectiveDate)
        .ToListAsync();

    return Results.Ok(new
    {
        startDate = start.ToString("yyyy-MM-dd"),
        endDate = end.ToString("yyyy-MM-dd"),
        totalDates = entries.Count,
        businessDays = entries.Count(e => e.IsBusinessDay),
        nonBusinessDays = entries.Count(e => !e.IsBusinessDay),
        holidays = entries.Where(e => !e.IsBusinessDay && e.EffectiveDate.DayOfWeek != DayOfWeek.Saturday && e.EffectiveDate.DayOfWeek != DayOfWeek.Sunday)
            .Select(e => e.EffectiveDate.ToString("yyyy-MM-dd"))
            .ToList()
    });
})
.WithName("GetUsBusinessCalendar")
.WithOpenApi()
.Produces(StatusCodes.Status200OK);

// Health check endpoints
app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var result = new
        {
            status = report.Status.ToString(),
            timestamp = DateTime.UtcNow,
            duration = report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                duration = e.Value.Duration.TotalMilliseconds,
                description = e.Value.Description,
                exception = e.Value.Exception?.Message
            })
        };
        await context.Response.WriteAsJsonAsync(result);
    }
});

// Liveness probe (just checks if app is running)
app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Name == "self"
});

// Readiness probe (checks all dependencies)
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("external")
});

// GET /api/version - Application version information
app.MapGet("/api/version", () =>
{
    var assembly = System.Reflection.Assembly.GetExecutingAssembly();
    var version = assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    var infoAttr = assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
        .FirstOrDefault() as System.Reflection.AssemblyInformationalVersionAttribute;
    var informationalVersion = infoAttr?.InformationalVersion ?? version;

    return Results.Ok(new
    {
        version = informationalVersion.Split('+')[0], // Strip build metadata if present
        buildDate = File.GetLastWriteTimeUtc(assembly.Location).ToString("yyyy-MM-dd"),
        environment = app.Environment.EnvironmentName
    });
})
.WithName("GetVersion")
.WithOpenApi()
.Produces(StatusCodes.Status200OK);

// Watchlist API endpoints

// GET /api/watchlists - List all watchlists
app.MapGet("/api/watchlists", async (WatchlistService watchlistService) =>
{
    var watchlists = await watchlistService.GetAllAsync();
    return Results.Ok(watchlists);
})
.WithName("GetWatchlists")
.WithOpenApi()
.Produces<List<StockAnalyzer.Core.Models.Watchlist>>(StatusCodes.Status200OK);

// POST /api/watchlists - Create a new watchlist
app.MapPost("/api/watchlists", async (StockAnalyzer.Core.Models.CreateWatchlistRequest request, WatchlistService watchlistService) =>
{
    if (string.IsNullOrWhiteSpace(request.Name))
        return Results.BadRequest(new { error = "Watchlist name is required" });

    var watchlist = await watchlistService.CreateAsync(request.Name);
    return Results.Created($"/api/watchlists/{watchlist.Id}", watchlist);
})
.WithName("CreateWatchlist")
.WithOpenApi()
.Produces<StockAnalyzer.Core.Models.Watchlist>(StatusCodes.Status201Created)
.Produces(StatusCodes.Status400BadRequest);

// GET /api/watchlists/{id} - Get a watchlist by ID
app.MapGet("/api/watchlists/{id}", async (string id, WatchlistService watchlistService) =>
{
    var watchlist = await watchlistService.GetByIdAsync(id);
    return watchlist != null
        ? Results.Ok(watchlist)
        : Results.NotFound(new { error = "Watchlist not found", id });
})
.WithName("GetWatchlist")
.WithOpenApi()
.Produces<StockAnalyzer.Core.Models.Watchlist>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status404NotFound);

// PUT /api/watchlists/{id} - Rename a watchlist
app.MapPut("/api/watchlists/{id}", async (string id, StockAnalyzer.Core.Models.UpdateWatchlistRequest request, WatchlistService watchlistService) =>
{
    if (string.IsNullOrWhiteSpace(request.Name))
        return Results.BadRequest(new { error = "Watchlist name is required" });

    var watchlist = await watchlistService.RenameAsync(id, request.Name);
    return watchlist != null
        ? Results.Ok(watchlist)
        : Results.NotFound(new { error = "Watchlist not found", id });
})
.WithName("UpdateWatchlist")
.WithOpenApi()
.Produces<StockAnalyzer.Core.Models.Watchlist>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status400BadRequest)
.Produces(StatusCodes.Status404NotFound);

// DELETE /api/watchlists/{id} - Delete a watchlist
app.MapDelete("/api/watchlists/{id}", async (string id, WatchlistService watchlistService) =>
{
    var deleted = await watchlistService.DeleteAsync(id);
    return deleted
        ? Results.NoContent()
        : Results.NotFound(new { error = "Watchlist not found", id });
})
.WithName("DeleteWatchlist")
.WithOpenApi()
.Produces(StatusCodes.Status204NoContent)
.Produces(StatusCodes.Status404NotFound);

// POST /api/watchlists/{id}/tickers - Add a ticker to a watchlist
app.MapPost("/api/watchlists/{id}/tickers", async (string id, StockAnalyzer.Core.Models.AddTickerRequest request, WatchlistService watchlistService) =>
{
    if (string.IsNullOrWhiteSpace(request.Ticker))
        return Results.BadRequest(new { error = "Ticker is required" });

    var watchlist = await watchlistService.AddTickerAsync(id, request.Ticker);
    return watchlist != null
        ? Results.Ok(watchlist)
        : Results.NotFound(new { error = "Watchlist not found", id });
})
.WithName("AddTickerToWatchlist")
.WithOpenApi()
.Produces<StockAnalyzer.Core.Models.Watchlist>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status400BadRequest)
.Produces(StatusCodes.Status404NotFound);

// DELETE /api/watchlists/{id}/tickers/{ticker} - Remove a ticker from a watchlist
app.MapDelete("/api/watchlists/{id}/tickers/{ticker}", async (string id, string ticker, WatchlistService watchlistService) =>
{
    var watchlist = await watchlistService.RemoveTickerAsync(id, ticker);
    return watchlist != null
        ? Results.Ok(watchlist)
        : Results.NotFound(new { error = "Watchlist not found", id });
})
.WithName("RemoveTickerFromWatchlist")
.WithOpenApi()
.Produces<StockAnalyzer.Core.Models.Watchlist>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status404NotFound);

// GET /api/watchlists/{id}/quotes - Get current quotes for all tickers in a watchlist
app.MapGet("/api/watchlists/{id}/quotes", async (string id, WatchlistService watchlistService) =>
{
    var quotes = await watchlistService.GetQuotesAsync(id);
    return quotes != null
        ? Results.Ok(quotes)
        : Results.NotFound(new { error = "Watchlist not found", id });
})
.WithName("GetWatchlistQuotes")
.WithOpenApi()
.Produces<WatchlistQuotes>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status404NotFound);

// PUT /api/watchlists/{id}/holdings - Update holdings for a watchlist
app.MapPut("/api/watchlists/{id}/holdings", async (
    string id,
    UpdateHoldingsRequest request,
    WatchlistService watchlistService) =>
{
    var validModes = new[] { "equal", "shares", "dollars" };
    if (!validModes.Contains(request.WeightingMode.ToLower()))
    {
        return Results.BadRequest(new { error = "Invalid weighting mode. Must be: equal, shares, or dollars" });
    }

    try
    {
        var watchlist = await watchlistService.UpdateHoldingsAsync(id, request);
        return watchlist != null
            ? Results.Ok(watchlist)
            : Results.NotFound(new { error = "Watchlist not found", id });
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
})
.WithName("UpdateWatchlistHoldings")
.WithOpenApi()
.Produces<StockAnalyzer.Core.Models.Watchlist>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status400BadRequest)
.Produces(StatusCodes.Status404NotFound);

// GET /api/watchlists/{id}/combined - Get combined portfolio performance
app.MapGet("/api/watchlists/{id}/combined", async (
    string id,
    string? period,
    string? benchmark,
    WatchlistService watchlistService) =>
{
    var result = await watchlistService.GetCombinedPortfolioAsync(
        id,
        period ?? "1y",
        benchmark);

    return result != null
        ? Results.Ok(result)
        : Results.NotFound(new { error = "Watchlist not found or no data available", id });
})
.WithName("GetCombinedPortfolio")
.WithOpenApi()
.Produces<CombinedPortfolioResult>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status404NotFound);

// GET /api/news/market - Get general market news
app.MapGet("/api/news/market", async (string? category, NewsService newsService) =>
{
    var result = await newsService.GetMarketNewsAsync(category ?? "general");
    return Results.Ok(result);
})
.WithName("GetMarketNews")
.WithOpenApi()
.Produces<NewsResult>(StatusCodes.Status200OK);

// GET /api/stock/{ticker}/news/aggregated - Get aggregated news from multiple sources with relevance scoring
app.MapGet("/api/stock/{ticker}/news/aggregated", async (
    string ticker,
    int? days,
    int? limit,
    AggregatedNewsService aggregatedNewsService) =>
{
    if (!IsValidTicker(ticker))
        return InvalidTickerResult();

    var result = await aggregatedNewsService.GetAggregatedNewsAsync(
        ticker,
        days ?? 7,
        limit ?? 20);
    return Results.Ok(result);
})
.WithName("GetAggregatedNews")
.WithOpenApi()
.Produces<AggregatedNewsResult>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status400BadRequest);

// GET /api/news/market/aggregated - Get aggregated market news from multiple sources
app.MapGet("/api/news/market/aggregated", async (int? limit, AggregatedNewsService aggregatedNewsService) =>
{
    var result = await aggregatedNewsService.GetAggregatedMarketNewsAsync(limit ?? 20);
    return Results.Ok(result);
})
.WithName("GetAggregatedMarketNews")
.WithOpenApi()
.Produces<AggregatedNewsResult>(StatusCodes.Status200OK);

// Add request logging
app.UseSerilogRequestLogging(options =>
{
    options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000}ms";
});

// Load symbol cache into memory at startup (non-blocking, logs timing)
// Also generates static symbols file for client-side search
_ = Task.Run(async () =>
{
    try
    {
        using var scope = app.Services.CreateScope();
        var repo = scope.ServiceProvider.GetService<ISymbolRepository>() as SqlSymbolRepository;
        if (repo != null)
        {
            await repo.LoadCacheAsync();

            // Generate static file for client-side search
            var cache = app.Services.GetService<SymbolCache>();
            var wwwrootPath = Path.Combine(app.Environment.ContentRootPath, "wwwroot");
            cache?.GenerateClientFile(wwwrootPath);
        }
    }
    catch (Exception ex)
    {
        Log.Warning(ex, "Failed to pre-load symbol cache (searches will use DB fallback)");
    }
});

Log.Information("Stock Analyzer API started successfully");

app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

// Request models for admin endpoints
public class ResetTrackedRequest
{
    public List<string> Tickers { get; set; } = new();
    public string? Source { get; set; }
    public int? Priority { get; set; }
}
