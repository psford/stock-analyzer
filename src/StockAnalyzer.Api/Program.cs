using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Events;
using StockAnalyzer.Core.Data;
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
app.MapPost("/api/admin/prices/holidays/forward-fill", async (IServiceProvider serviceProvider) =>
{
    using var scope = serviceProvider.CreateScope();
    var priceRepo = scope.ServiceProvider.GetService<IPriceRepository>();

    if (priceRepo == null)
        return Results.BadRequest(new { error = "Price repository not configured" });

    try
    {
        Log.Information("Starting holiday forward-fill via API");
        var result = await priceRepo.ForwardFillHolidaysAsync();

        if (!result.Success)
            return Results.Problem(result.Error ?? "Forward-fill failed");

        return Results.Ok(new
        {
            success = true,
            message = result.Message,
            holidaysProcessed = result.HolidaysProcessed,
            totalRecordsInserted = result.TotalRecordsInserted,
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
