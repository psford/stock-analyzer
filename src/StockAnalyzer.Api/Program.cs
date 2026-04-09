using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Events;
using StockAnalyzer.Api;
using StockAnalyzer.Core.Data;
using StockAnalyzer.Core.Data.Entities;
using StockAnalyzer.Core.Helpers;
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
        var logger = sp.GetRequiredService<ILogger<TwelveDataService>>();
        var apiKey = EndpointRegistry.Resolve("twelveData.apiKey");
        return new TwelveDataService(apiKey, logger);
    });
    builder.Services.AddSingleton<IStockDataProvider>(sp =>
    {
        var logger = sp.GetRequiredService<ILogger<FmpService>>();
        var apiKey = EndpointRegistry.Resolve("fmp.apiKey");
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

    // Wikipedia fallback for company descriptions
    builder.Services.AddSingleton(sp =>
        new WikipediaService(new HttpClient(), sp.GetRequiredService<IMemoryCache>(),
            sp.GetRequiredService<ILogger<WikipediaService>>()));

    // Register news services
    builder.Services.AddSingleton(sp =>
    {
        var apiKey = EndpointRegistry.Resolve("finnhub.apiKey");
        return new NewsService(apiKey);
    });
    builder.Services.AddSingleton(sp =>
    {
        var apiToken = EndpointRegistry.Resolve("marketaux.apiKey");
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
    // Database connection resolved via EndpointRegistry from endpoints.json
    string? connectionString = null;
    try
    {
        connectionString = EndpointRegistry.Resolve("database");
    }
    catch (InvalidOperationException ex) when (ex.Message.Contains("not set") || ex.Message.Contains("Environment variable"))
    {
        // Database connection not configured - will use JSON fallback
        Log.Information("Database connection not configured, will use JSON file for watchlist storage");
    }
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

        // Warm up DB connection pool on startup (avoids cold-start penalty on first request)
        builder.Services.AddHostedService<DbWarmupService>();

        // Security master and price repositories (data schema)
        builder.Services.AddScoped<ISecurityMasterRepository, SqlSecurityMasterRepository>();
        builder.Services.AddScoped<IPriceRepository, SqlPriceRepository>();
        builder.Services.AddScoped<ReturnCalculationService>();

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

        // Resolve EODHD API key from registry and add to configuration
        var eodhdApiKey = EndpointRegistry.Resolve("eodhd.apiKey");
        var eodhdConfig = new ConfigurationBuilder()
            .AddConfiguration(config)
            .AddInMemoryCollection(new Dictionary<string, string?> { ["EodhdApiKey"] = eodhdApiKey })
            .Build();

        return new EodhdService(httpClient, logger, eodhdConfig);
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

    // Validate all endpoints - but allow database to be missing in development (JSON mode)
    try
    {
        EndpointRegistry.ValidateAll();
    }
    catch (AggregateException ex) when (ex.InnerExceptions.Any(e =>
        e.Message.Contains("WSL_SQL_CONNECTION") && e.Message.Contains("not set")))
    {
        // Database connection not configured - but re-throw any OTHER validation errors
        var nonDbErrors = ex.InnerExceptions
            .Where(e => !(e.Message.Contains("WSL_SQL_CONNECTION") && e.Message.Contains("not set")))
            .ToList();

        if (nonDbErrors.Count > 0)
        {
            throw new AggregateException(
                $"Endpoint validation failed with {nonDbErrors.Count} error(s):\n" +
                string.Join("\n", nonDbErrors.Select(e => $"  - {e.Message}")),
                nonDbErrors);
        }

        // Database connection not configured - this is OK in development/JSON mode
        Log.Information("Note: Database connection not configured. Using JSON file mode for watchlist storage.");
    }

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
        // - flatpickr from cdnjs for desktop date picker
        context.Response.Headers["Content-Security-Policy"] =
            "default-src 'self'; " +
            "script-src 'self' 'unsafe-inline' 'unsafe-eval' https://cdn.plot.ly https://cdn.jsdelivr.net https://cdnjs.cloudflare.com; " +
            "style-src 'self' 'unsafe-inline' https://cdnjs.cloudflare.com; " +
            "img-src 'self' data: blob:; " +
            "font-src 'self' https:; " +
            "connect-src 'self' https://psford.github.io https://stockanalyzerblob.z13.web.core.windows.net http://localhost:8001";

        await next();
    });

    // Automatic JS cache-busting: compute content hash at startup,
    // serve index.html with ?v={hash} instead of manual version strings.
    // Hash changes whenever any JS file is modified → browsers fetch new versions.
    var jsVersionHash = ComputeJsContentHash(app.Environment.WebRootPath);
    Log.Information("JS content hash for cache-busting: {Hash}", jsVersionHash);

    // Cache the versioned HTML at startup (recomputed on each app restart).
    // Replaces any ?v=... on local JS/CSS refs with the content hash.
    var indexHtmlPath = Path.Combine(app.Environment.WebRootPath, "index.html");
    var versionedIndexHtml = System.Text.RegularExpressions.Regex.Replace(
        File.ReadAllText(indexHtmlPath),
        @"\?v=[^""']+",
        $"?v={jsVersionHash}");

    app.Use(async (context, next) =>
    {
        var reqPath = context.Request.Path.Value;
        if (reqPath == "/" || string.Equals(reqPath, "/index.html", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
            context.Response.Headers.Pragma = "no-cache";
            await context.Response.WriteAsync(versionedIndexHtml);
            return;
        }
        await next();
    });

    // UseDefaultFiles no longer needed for index.html (handled above),
    // but keep it for any other default documents.
    app.UseDefaultFiles();

    // Configure static files with custom MIME types for .mmd (Mermaid) files
    var contentTypeProvider = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider();
    contentTypeProvider.Mappings[".mmd"] = "text/plain";
    app.UseStaticFiles(new StaticFileOptions
    {
        ContentTypeProvider = contentTypeProvider
    });

    // API Endpoints

    // Concurrency guards for heavy endpoints (Azure SQL Basic: 5 DTU / 60 workers)
    // These are app-lifetime objects — disposal is handled by process exit.
#pragma warning disable CA2000
    var refreshSummarySemaphore = new SemaphoreSlim(1, 1);
    var bulkLoadSemaphore = new SemaphoreSlim(1, 1);
    var calculateImportanceSemaphore = new SemaphoreSlim(1, 1);
    var autoTrackSemaphore = new SemaphoreSlim(3, 3);
    var backfillCoverageSemaphore = new SemaphoreSlim(1, 1);
    var backfillMicCodesSemaphore = new SemaphoreSlim(1, 1);
#pragma warning restore CA2000

    // HashSets for MIC code-based scoring (reused across ~30K security iterations)
    var bonusMics = new HashSet<string> { "XNYS", "XNAS" };
    var penaltyMics = new HashSet<string> { "OTCM", "PINX", "XOTC" };

    // Ticker validation helper - allows 1-10 alphanumeric chars plus dots, dashes, carets (e.g., BRK.B, BRK-B, ^GSPC)
    static bool IsValidTicker(string? ticker) =>
        !string.IsNullOrWhiteSpace(ticker) &&
        ticker.Length <= 10 &&
        System.Text.RegularExpressions.Regex.IsMatch(ticker, @"^[A-Za-z0-9\.\-\^]+$");

    static IResult InvalidTickerResult() =>
        Results.BadRequest(new { error = "Invalid ticker symbol. Use 1-10 alphanumeric characters, dots, dashes, or carets." });

    // GET /api/stock/{ticker} - Get stock information with company profile and identifiers
    app.MapGet("/api/stock/{ticker}", async (string ticker, AggregatedStockDataService stockService, NewsService newsService, WikipediaService wikipediaService, IServiceProvider serviceProvider) =>
    {
        if (!IsValidTicker(ticker))
            return InvalidTickerResult();

        var info = await stockService.GetStockInfoAsync(ticker);
        if (info == null)
            return Results.NotFound(new { error = "Stock not found", symbol = ticker });

        // Auto-track: mark this security as tracked (user interest signal)
        // Fire-and-forget with concurrency guard — skip if at capacity (best-effort)
        _ = Task.Run(async () =>
        {
            if (!autoTrackSemaphore.Wait(0)) return; // Skip if at capacity
            try
            {
                using var scope = serviceProvider.CreateScope();
                var ctx = scope.ServiceProvider.GetService<StockAnalyzerDbContext>();
                if (ctx == null) return;

                var security = await ctx.SecurityMaster
                    .FirstOrDefaultAsync(s => s.TickerSymbol == ticker && s.IsActive && !s.IsTracked);
                if (security != null)
                {
                    security.IsTracked = true;
                    security.UpdatedAt = DateTime.UtcNow;

                    // Also add to TrackedSecurities detail table if not already present
                    var alreadyTracked = await ctx.TrackedSecurities
                        .AnyAsync(ts => ts.SecurityAlias == security.SecurityAlias);
                    if (!alreadyTracked)
                    {
                        ctx.TrackedSecurities.Add(new TrackedSecurityEntity
                        {
                            SecurityAlias = security.SecurityAlias,
                            Source = "user-search",
                            Priority = 3,
                            Notes = "Auto-tracked from web UI stock lookup",
                            AddedBy = "web-ui"
                        });
                    }

                    await ctx.SaveChangesAsync();
                    Log.Information("Auto-tracked {Ticker} from web UI stock lookup", ticker);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to auto-track {Ticker} from web UI", ticker);
            }
            finally
            {
                autoTrackSemaphore.Release();
            }
        });

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
            MicCode = info.MicCode,                        // From SecurityMaster
            ExchangeName = info.ExchangeName,              // Joined from MicExchange
            Industry = profile?.Industry ?? info.Industry,
            Country = profile?.Country ?? info.Country,
            Website = profile?.WebUrl ?? info.Website,
            Isin = profile?.Isin,
            Cusip = profile?.Cusip,
            Sedol = sedol,
            IpoDate = profile?.IpoDate
        };

        // Company bio: check DB cache first, then fall back to Wikipedia
        {
            using var bioScope = serviceProvider.CreateScope();
            var bioCtx = bioScope.ServiceProvider.GetRequiredService<StockAnalyzerDbContext>();

            var security = await bioCtx.SecurityMaster
                .Include(s => s.MicExchange)
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.TickerSymbol == ticker && s.IsActive);

            if (security != null)
            {
                // Enrich with MicCode from SecurityMaster (external providers don't have this)
                enrichedInfo = enrichedInfo with
                {
                    MicCode = security.MicCode,
                    ExchangeName = security.MicExchange?.ExchangeName
                };

                // Check CompanyBio cache
                var cachedBio = await bioCtx.CompanyBios
                    .AsNoTracking()
                    .FirstOrDefaultAsync(b => b.SecurityAlias == security.SecurityAlias);

                if (cachedBio != null)
                {
                    // Serve from DB — no external calls needed
                    enrichedInfo = enrichedInfo with { Description = cachedBio.Description };
                }
                else
                {
                    // No cached bio — determine best description and store it
                    var description = enrichedInfo.Description;
                    var source = "provider";

                    if (string.IsNullOrWhiteSpace(description) ||
                        description.Length < WikipediaService.MinDescriptionLength)
                    {
                        var companyName = enrichedInfo.LongName ?? enrichedInfo.ShortName ?? ticker;
                        var wikiDesc = await wikipediaService.GetCompanyDescriptionAsync(companyName);
                        if (wikiDesc != null)
                        {
                            description = wikiDesc;
                            source = "wikipedia";
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(description))
                    {
                        enrichedInfo = enrichedInfo with { Description = description };

                        // Fire-and-forget: persist to CompanyBio table
                        var alias = security.SecurityAlias;
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                using var storeScope = serviceProvider.CreateScope();
                                var storeCtx = storeScope.ServiceProvider.GetRequiredService<StockAnalyzerDbContext>();
                                storeCtx.CompanyBios.Add(new CompanyBioEntity
                                {
                                    SecurityAlias = alias,
                                    Description = description,
                                    Source = source,
                                    FetchedAt = DateTime.UtcNow,
                                    UpdatedAt = DateTime.UtcNow
                                });
                                await storeCtx.SaveChangesAsync();
                                Log.Information("Cached company bio for {Ticker} (source: {Source})", ticker, source);
                            }
                            catch (Exception ex)
                            {
                                Log.Warning(ex, "Failed to cache company bio for {Ticker}", ticker);
                            }
                        });
                    }
                }
            }
            else
            {
                // Ticker not in SecurityMaster — fetch from Wikipedia without caching
                if (string.IsNullOrWhiteSpace(enrichedInfo.Description) ||
                    enrichedInfo.Description.Length < WikipediaService.MinDescriptionLength)
                {
                    var companyName = enrichedInfo.LongName ?? enrichedInfo.ShortName ?? ticker;
                    var wikiDesc = await wikipediaService.GetCompanyDescriptionAsync(companyName);
                    if (wikiDesc != null)
                    {
                        enrichedInfo = enrichedInfo with { Description = wikiDesc };
                    }
                }
            }
        }

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
        string? from,
        string? to,
        AggregatedStockDataService stockService) =>
    {
        if (!IsValidTicker(ticker))
            return InvalidTickerResult();

        HistoricalDataResult? data;
        if (!string.IsNullOrEmpty(from) && DateTime.TryParse(from, out var fromDate)
            && !string.IsNullOrEmpty(to) && DateTime.TryParse(to, out var toDate))
        {
            data = await stockService.GetHistoricalDataAsync(ticker, fromDate, toDate);
        }
        else
        {
            data = await stockService.GetHistoricalDataAsync(ticker, period ?? "1y");
        }
        return data != null
            ? Results.Ok(data)
            : Results.NotFound(new { error = "Historical data not found", symbol = ticker });
    })
    .WithName("GetStockHistory")
    .WithOpenApi()
    .Produces<StockAnalyzer.Core.Models.HistoricalDataResult>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status404NotFound);

    // GET /api/stock/{ticker}/news - Get company news with sentiment and relevance scoring
    app.MapGet("/api/stock/{ticker}/news", async (
        string ticker,
        int? days,
        int? limit,
        NewsService newsService,
        HeadlineRelevanceService relevanceService) =>
    {
        if (!IsValidTicker(ticker))
            return InvalidTickerResult();

        var fromDate = DateTime.Now.AddDays(-(days ?? 30));
        var result = await newsService.GetCompanyNewsAsync(ticker, fromDate);

        // Look up company name for better relevance scoring
        var profile = await newsService.GetCompanyProfileAsync(ticker);
        var companyName = profile?.Name;

        // Enrich articles with sentiment and relevance scores
        var enriched = result.Articles
            .Select(article =>
            {
                var (sentiment, sentimentScore) = SentimentAnalyzer.Analyze(article.Headline);
                var relevance = relevanceService.ScoreRelevance(article, ticker, companyName);
                return article with
                {
                    Sentiment = sentiment.ToString().ToLower(),
                    SentimentScore = sentimentScore,
                    RelevanceScore = relevance,
                    SourceApi = "finnhub"
                };
            })
            .OrderByDescending(a => a.RelevanceScore)
            .ThenByDescending(a => a.PublishedAt)
            .Take(limit ?? 30)
            .ToList();

        return Results.Ok(new NewsResult
        {
            Symbol = result.Symbol,
            FromDate = result.FromDate,
            ToDate = result.ToDate,
            Articles = enriched
        });
    })
    .WithName("GetStockNews")
    .WithOpenApi()
    .Produces<StockAnalyzer.Core.Models.NewsResult>(StatusCodes.Status200OK);

    // GET /api/stock/{ticker}/significant - Get significant price moves (no news - use /news/move for lazy loading)
    app.MapGet("/api/stock/{ticker}/significant", async (
        string ticker,
        decimal? threshold,
        string? period,
        string? from,
        string? to,
        AggregatedStockDataService stockService,
        AnalysisService analysisService) =>
    {
        if (!IsValidTicker(ticker))
            return InvalidTickerResult();

        var thresholdValue = threshold ?? 3.0m;

        // Custom date range takes precedence over period
        HistoricalDataResult? history;
        if (!string.IsNullOrEmpty(from) && DateTime.TryParse(from, out var fromDate)
            && !string.IsNullOrEmpty(to) && DateTime.TryParse(to, out var toDate))
        {
            history = await stockService.GetHistoricalDataAsync(ticker, fromDate, toDate);
        }
        else
        {
            history = await stockService.GetHistoricalDataAsync(ticker, period ?? "1y");
        }
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
        if (cache.TryGetValue(cacheKey, out MoveNewsResult? cachedResult) && cachedResult != null)
        {
            return Results.Ok(new
            {
                articles = cachedResult.Articles,
                source = cachedResult.Source,
                directionMatch = cachedResult.DirectionMatch
            });
        }

        var result = await newsService.GetNewsForDateWithSentimentAsync(
            ticker,
            date,
            change,
            maxArticles: limit ?? 5);

        // Cache for 30 minutes (news doesn't change for historical dates)
        cache.Set(cacheKey, result, TimeSpan.FromMinutes(30));

        return Results.Ok(new
        {
            articles = result.Articles,
            source = result.Source,
            directionMatch = result.DirectionMatch
        });
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

    // GET /api/stock/{ticker}/chart-data - Combined history + analysis in one request
    // Eliminates duplicate GetHistoricalDataAsync calls and saves an HTTP round-trip
    app.MapGet("/api/stock/{ticker}/chart-data", async (
        string ticker,
        string? period,
        string? from,
        string? to,
        AggregatedStockDataService stockService,
        AnalysisService analysisService) =>
    {
        if (!IsValidTicker(ticker))
            return InvalidTickerResult();

        // Custom date range takes precedence over period
        HistoricalDataResult? history;
        if (!string.IsNullOrEmpty(from) && DateTime.TryParse(from, out var fromDate)
            && !string.IsNullOrEmpty(to) && DateTime.TryParse(to, out var toDate))
        {
            history = await stockService.GetHistoricalDataAsync(ticker, fromDate, toDate);
        }
        else
        {
            history = await stockService.GetHistoricalDataAsync(ticker, period ?? "1y");
        }
        if (history == null)
            return Results.NotFound(new { error = "Data not found", symbol = ticker });

        var movingAverages = analysisService.CalculateMovingAverages(history.Data);
        var performance = analysisService.CalculatePerformance(history.Data);
        var rsi = analysisService.CalculateRsi(history.Data);
        var macd = analysisService.CalculateMacd(history.Data);
        var bollingerBands = analysisService.CalculateBollingerBands(history.Data);
        var stochastic = analysisService.CalculateStochastic(history.Data);

        return Results.Ok(new
        {
            // History
            symbol = history.Symbol,
            period = history.Period,
            startDate = history.StartDate,
            endDate = history.EndDate,
            data = history.Data,
            minClose = history.MinClose,
            maxClose = history.MaxClose,
            averageClose = history.AverageClose,
            averageVolume = history.AverageVolume,
            // Analysis
            performance,
            movingAverages,
            rsi,
            macd,
            bollingerBands,
            stochastic
        });
    })
    .WithName("GetChartData")
    .WithOpenApi()
    .Produces(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status404NotFound);

    // GET /api/stock/{ticker}/returns - Period return table (1D, 5D, MTD, ..., Since Inception)
    app.MapGet("/api/stock/{ticker}/returns", async (
        string ticker,
        string? asOf,
        string? ipoDate,
        ReturnCalculationService returnService) =>
    {
        if (!IsValidTicker(ticker))
            return InvalidTickerResult();

        var endDate = DateTime.TryParse(asOf, out var d) ? d : DateTime.Today;
        var result = await returnService.CalculateReturnsAsync(ticker, endDate, ipoDate);
        if (result == null)
            return Results.NotFound(new { error = "No price data found", symbol = ticker });

        return Results.Ok(new
        {
            symbol = ticker,
            endDate = result.EndDate.ToString("yyyy-MM-dd"),
            earliestPriceDate = result.EarliestPriceDate?.ToString("yyyy-MM-dd"),
            returns = result.Returns.Select(r => new
            {
                label = r.Label,
                returnPct = r.ReturnPct,
                isAnnualized = r.IsAnnualized
            })
        });
    })
    .WithName("GetReturns")
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
                micCode = r.MicCode,
                exchangeName = r.ExchangeName,
                type = r.Type,
                displayName = r.DisplayName
            })
        });
    })
    .WithName("SearchTickers")
    .WithOpenApi()
    .Produces(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status400BadRequest);

    // GET /api/indices/search - Search index definitions for benchmark discovery
    app.MapGet("/api/indices/search", async (string? q, StockAnalyzerDbContext dbContext) =>
    {
        if (string.IsNullOrWhiteSpace(q))
            return Results.Ok(Array.Empty<object>());

        var normalizedQuery = q.Trim().ToUpperInvariant();

        var results = await dbContext.IndexDefinitions
            .AsNoTracking()
            .Where(idx => idx.ProxyEtfTicker != null &&
                (idx.IndexName.ToUpper().Contains(normalizedQuery) ||
                 idx.IndexCode.ToUpper().Contains(normalizedQuery) ||
                 (idx.IndexFamily != null && idx.IndexFamily.ToUpper().Contains(normalizedQuery))))
            .OrderBy(idx => idx.IndexName)
            .Take(10)
            .Select(idx => new
            {
                indexId = idx.IndexId,
                indexCode = idx.IndexCode,
                indexName = idx.IndexName,
                indexFamily = idx.IndexFamily,
                region = idx.Region,
                proxyEtfTicker = idx.ProxyEtfTicker
            })
            .ToListAsync();

        return Results.Ok(results);
    })
    .WithName("SearchIndices")
    .WithOpenApi()
    .Produces(StatusCodes.Status200OK);

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

        // Get latest date from a sample of securities (projected query, 2 columns only)
        DateTime? latestDate = null;
        if (totalPrices > 0)
        {
            var tickerAliasMap = await securityRepo.GetActiveTickerAliasMapAsync();
            var sampleAliases = tickerAliasMap.Values.Take(5);
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

        // Concurrency guard — only one bulk-load at a time
        if (!bulkLoadSemaphore.Wait(0))
            return Results.Conflict(new { error = "A bulk load is already in progress. Try again later." });

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
                finally
                {
                    bulkLoadSemaphore.Release();
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
            bulkLoadSemaphore.Release();
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
        IMemoryCache cache,
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

            // Invalidate dashboard stats cache so next fetch reflects new records
            if (result.TotalRecordsInserted > 0)
            {
                cache.Remove("dashboard:stats");
            }

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

    // POST /api/admin/prices/backfill-gaps - Gap-aware backfill for identified missing dates
    app.MapPost("/api/admin/prices/backfill-gaps", async (
        PriceRefreshService? refreshService,
        [FromQuery] int? maxConcurrency,
        CancellationToken ct) =>
    {
        if (refreshService == null)
            return Results.Problem("PriceRefreshService not available (EODHD not configured?)");

        try
        {
            Log.Information("Starting gap-aware backfill with concurrency {Concurrency}",
                maxConcurrency ?? 3);

            var result = await refreshService.BackfillGapsAsync(
                maxConcurrency ?? 3, ct: ct);

            return Results.Ok(result);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to run gap-aware backfill");
            return Results.Problem(ex.Message);
        }
    })
    .WithName("BackfillGaps")
    .WithOpenApi()
    .Produces(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status500InternalServerError);

    // POST /api/admin/prices/backfill-coverage - Backfill SecurityPriceCoverage from existing Prices data
    // One-time bootstrap operation to populate coverage tables from full Prices scan.
    // Safe to re-run (MERGE is idempotent). Extended timeout for full table scan.
    app.MapPost("/api/admin/prices/backfill-coverage", async (IServiceProvider serviceProvider) =>
    {
        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetService<StockAnalyzerDbContext>();

        if (context == null)
            return Results.BadRequest(new { error = "Database context not configured" });

        // Semaphore guard: prevent concurrent backfill runs
        if (!await backfillCoverageSemaphore.WaitAsync(0))
            return Results.Conflict(new { error = "Coverage backfill already in progress." });

        var connection = context.Database.GetDbConnection();
        var connectionWasOpen = connection.State == System.Data.ConnectionState.Open;

        try
        {
            if (!connectionWasOpen)
                await connection.OpenAsync();

            Log.Information("Starting SecurityPriceCoverage backfill from Prices table");

            int coverageRows = 0;
            int byYearRows = 0;

            // MERGE SecurityPriceCoverage: aggregate all Prices by SecurityAlias
            // Include ExpectedCount calculation from BusinessCalendar for the date range
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = @"
MERGE INTO data.SecurityPriceCoverage WITH (HOLDLOCK) AS target
USING (
    SELECT
        p.SecurityAlias,
        COUNT(*) AS PriceCount,
        MIN(p.EffectiveDate) AS FirstDate,
        MAX(p.EffectiveDate) AS LastDate
    FROM data.Prices p WITH (NOLOCK)
    GROUP BY p.SecurityAlias
) AS source
ON target.SecurityAlias = source.SecurityAlias
WHEN MATCHED THEN
    UPDATE SET
        PriceCount = source.PriceCount,
        FirstDate = source.FirstDate,
        LastDate = source.LastDate,
        ExpectedCount = (
            SELECT COUNT(*)
            FROM data.BusinessCalendar bc WITH (NOLOCK)
            WHERE bc.SourceId = 1
              AND bc.IsBusinessDay = 1
              AND bc.EffectiveDate >= source.FirstDate
              AND bc.EffectiveDate <= source.LastDate
        ),
        LastUpdatedAt = GETUTCDATE()
WHEN NOT MATCHED THEN
    INSERT (SecurityAlias, PriceCount, FirstDate, LastDate, ExpectedCount, LastUpdatedAt)
    VALUES (
        source.SecurityAlias,
        source.PriceCount,
        source.FirstDate,
        source.LastDate,
        (
            SELECT COUNT(*)
            FROM data.BusinessCalendar bc WITH (NOLOCK)
            WHERE bc.SourceId = 1
              AND bc.IsBusinessDay = 1
              AND bc.EffectiveDate >= source.FirstDate
              AND bc.EffectiveDate <= source.LastDate
        ),
        GETUTCDATE()
    );

SELECT @@ROWCOUNT";
                cmd.CommandTimeout = 600; // 10 minutes - intentionally expensive one-time scan
                coverageRows = (int)(await cmd.ExecuteScalarAsync() ?? 0);
            }

            Log.Information("SecurityPriceCoverage backfill complete: {Rows} rows affected", LogSanitizer.Sanitize(coverageRows.ToString()));

            // MERGE SecurityPriceCoverageByYear: aggregate all Prices by SecurityAlias + YEAR
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = @"
MERGE INTO data.SecurityPriceCoverageByYear WITH (HOLDLOCK) AS target
USING (
    SELECT
        p.SecurityAlias,
        YEAR(p.EffectiveDate) AS [Year],
        COUNT(*) AS PriceCount
    FROM data.Prices p WITH (NOLOCK)
    GROUP BY p.SecurityAlias, YEAR(p.EffectiveDate)
) AS source
ON target.SecurityAlias = source.SecurityAlias AND target.[Year] = source.[Year]
WHEN MATCHED THEN
    UPDATE SET
        PriceCount = source.PriceCount,
        LastUpdatedAt = GETUTCDATE()
WHEN NOT MATCHED THEN
    INSERT (SecurityAlias, [Year], PriceCount, LastUpdatedAt)
    VALUES (source.SecurityAlias, source.[Year], source.PriceCount, GETUTCDATE());

SELECT @@ROWCOUNT";
                cmd.CommandTimeout = 600;
                byYearRows = (int)(await cmd.ExecuteScalarAsync() ?? 0);
            }

            Log.Information("SecurityPriceCoverageByYear backfill complete: {Rows} rows affected", LogSanitizer.Sanitize(byYearRows.ToString()));

            return Results.Ok(new
            {
                success = true,
                message = $"Backfilled {coverageRows} coverage rows and {byYearRows} by-year rows",
                coverageRowsUpdated = coverageRows,
                coverageByYearRowsUpdated = byYearRows
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Backfill coverage failed");
            return Results.Problem($"Backfill coverage failed: {LogSanitizer.Sanitize(ex.Message)}");
        }
        finally
        {
            backfillCoverageSemaphore.Release();
            if (!connectionWasOpen && connection.State == System.Data.ConnectionState.Open)
                connection.Close();
        }
    })
    .WithName("BackfillPriceCoverage")
    .WithOpenApi()
    .Produces(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status400BadRequest)
    .Produces(StatusCodes.Status409Conflict)
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
                    micCode = s.MicCode,
                    exchangeName = s.MicExchange?.ExchangeName,
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

            // Fetch pageSize + 1 to detect hasMore without a separate COUNT scan
            var prices = await context.Prices
                .AsNoTracking()
                .Where(p => p.EffectiveDate >= start.Date && p.EffectiveDate <= end.Date)
                .OrderBy(p => p.EffectiveDate)
                .ThenBy(p => p.SecurityAlias)
                .Skip(skip)
                .Take(pageSize + 1)
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

            var hasMore = prices.Count > pageSize;
            if (hasMore)
                prices = prices.Take(pageSize).ToList();

            // Use CoverageSummary for approximate total (avoids expensive COUNT on Prices)
            var approxTotal = await context.CoverageSummary
                .AsNoTracking()
                .SumAsync(s => s.TrackedRecords + s.UntrackedRecords);

            return Results.Ok(new
            {
                success = true,
                startDate = start.ToString("yyyy-MM-dd"),
                endDate = end.ToString("yyyy-MM-dd"),
                page,
                pageSize,
                approximateTotal = approxTotal,
                count = prices.Count,
                hasMore,
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
            // Use CoverageSummary pre-aggregated table (~500 rows) instead of scanning 7M+ Prices
            var summaryRows = await context.CoverageSummary.AsNoTracking().ToListAsync();

            if (!summaryRows.Any())
            {
                return Results.Ok(new
                {
                    success = true,
                    hasData = false,
                    message = "No price data in database"
                });
            }

            // All derived from CoverageSummary in C# — zero Prices table queries
            var totalRecords = (int)summaryRows.Sum(r => r.TrackedRecords + r.UntrackedRecords);

            // Distinct securities: max per ImportanceScore across years (same as dashboard/stats)
            var distinctSecurities = summaryRows
                .GroupBy(r => r.ImportanceScore)
                .Sum(g => g.Max(r => r.TrackedSecurities + r.UntrackedSecurities));

            // Date range from CoverageSummary year boundaries
            var minYear = summaryRows.Min(r => r.Year);
            var maxYear = summaryRows.Max(r => r.Year);

            return Results.Ok(new
            {
                success = true,
                hasData = true,
                startDate = $"{minYear}-01-01",
                endDate = maxYear >= DateTime.UtcNow.Year
                    ? DateTime.UtcNow.ToString("yyyy-MM-dd")
                    : $"{maxYear}-12-31",
                totalRecords,
                distinctSecurities
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
                FROM data.Prices p WITH (NOLOCK)
                INNER JOIN data.SecurityMaster sm WITH (NOLOCK) ON sm.SecurityAlias = p.SecurityAlias
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
                FROM data.SecurityMaster sm WITH (ROWLOCK)
                WHERE EXISTS (SELECT 1 FROM data.TrackedSecurities ts WITH (NOLOCK) WHERE ts.SecurityAlias = sm.SecurityAlias)
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
            // All metrics derived from CoverageSummary (~500 rows) + lightweight index seeks
            // instead of 4 separate full-table scans on the 7M+ Prices table
            var summaryRows = await context.CoverageSummary.AsNoTracking().ToListAsync();

            if (!summaryRows.Any())
            {
                return Results.Ok(new
                {
                    success = true,
                    hasData = false,
                    message = "No price data in database"
                });
            }

            // All core metrics from CoverageSummary in C# — only 1 lightweight SQL query (recentActivity)
            var totalRecords = (int)summaryRows.Sum(r => r.TrackedRecords + r.UntrackedRecords);

            // Distinct dates from CoverageSummary (sum TradingDays per year)
            var distinctDates = summaryRows
                .GroupBy(r => r.Year)
                .Sum(g => g.Max(r => r.TradingDays));

            // Distinct securities: max per ImportanceScore across years (same as dashboard/stats)
            var distinctSecurities = summaryRows
                .GroupBy(r => r.ImportanceScore)
                .Sum(g => g.Max(r => r.TrackedSecurities + r.UntrackedSecurities));

            // Date range from CoverageSummary year boundaries
            var minYear = summaryRows.Min(r => r.Year);
            var maxYear = summaryRows.Max(r => r.Year);
            var startDate = $"{minYear}-01-01";
            var endDate = maxYear >= DateTime.UtcNow.Year
                ? DateTime.UtcNow.ToString("yyyy-MM-dd")
                : $"{maxYear}-12-31";

            // Coverage by decade: aggregate CoverageSummary in C# (no SQL needed)
            var coverageByDecade = summaryRows
                .GroupBy(r => (r.Year / 10) * 10)
                .OrderBy(g => g.Key)
                .Select(g => new
                {
                    decade = $"{g.Key}s",
                    records = (int)g.Sum(r => r.TrackedRecords + r.UntrackedRecords),
                    securities = (int)g.Sum(r => r.TrackedSecurities + r.UntrackedSecurities),
                    tradingDays = g.GroupBy(r => r.Year).Sum(yg => yg.Max(r => r.TradingDays))
                })
                .ToList<object>();

            // Recent activity: 10 most recent dates via index backward scan (fast, no GROUP BY)
            var recentActivity = new List<object>();
            var connection = context.Database.GetDbConnection();
            await connection.OpenAsync();
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = @"
                SELECT DISTINCT TOP 10 EffectiveDate
                FROM data.Prices WITH (NOLOCK)
                ORDER BY EffectiveDate DESC";
                cmd.CommandTimeout = 5;

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    recentActivity.Add(new
                    {
                        date = reader.GetDateTime(0).ToString("yyyy-MM-dd"),
                        loadedAt = reader.GetDateTime(0).ToString("yyyy-MM-dd")
                    });
                }
            }

            var yearsOfData = maxYear - minYear + 1;
            var avgRecordsPerDay = distinctDates > 0 ? totalRecords / distinctDates : 0;

            return Results.Ok(new
            {
                success = true,
                hasData = true,
                totalRecords,
                distinctSecurities,
                distinctDates,
                startDate,
                endDate,
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

    // GET /api/admin/prices/gaps - Find missing price data for tracked securities
    // Only checks tracked securities (IsTracked = 1), including those with zero price data.
    // To load untracked securities, use POST /api/admin/securities/promote-untracked first.
    app.MapGet("/api/admin/prices/gaps", async (IServiceProvider serviceProvider, string? market, int? limit) =>
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

            // SINGLE QUERY: Fetch ALL gaps (no TOP), count in C#.
            // Previous design ran 3 sequential queries (main + count + stats), each scanning
            // the 5M+ row Prices table. On Azure SQL Basic (5 DTU) this exhausted the DTU budget.
            using var cmd = connection.CreateCommand();

            cmd.CommandText = @"
                SELECT
                    sm.SecurityAlias,
                    sm.TickerSymbol,
                    sm.IsTracked,
                    COALESCE(pc.FirstDate, CAST(DATEADD(YEAR, -2, GETDATE()) AS DATE)) AS FirstDate,
                    COALESCE(pc.LastDate, CAST(GETDATE() AS DATE)) AS LastDate,
                    ISNULL(pc.PriceCount, 0) AS ActualPriceCount,
                    COALESCE(pc.ExpectedCount,
                        (SELECT COUNT(*) FROM data.BusinessCalendar bc WITH (NOLOCK)
                         WHERE bc.IsBusinessDay = 1 AND bc.SourceId = 1
                           AND bc.EffectiveDate >= DATEADD(YEAR, -2, GETDATE())
                           AND bc.EffectiveDate <= GETDATE())) AS ExpectedPriceCount,
                    COALESCE(pc.GapDays,
                        (SELECT COUNT(*) FROM data.BusinessCalendar bc WITH (NOLOCK)
                         WHERE bc.IsBusinessDay = 1 AND bc.SourceId = 1
                           AND bc.EffectiveDate >= DATEADD(YEAR, -2, GETDATE())
                           AND bc.EffectiveDate <= GETDATE())) AS MissingDays,
                    sm.ImportanceScore,
                    COALESCE(ts.Priority, 999) AS Priority
                FROM data.SecurityMaster sm WITH (NOLOCK)
                LEFT JOIN data.SecurityPriceCoverage pc WITH (NOLOCK) ON pc.SecurityAlias = sm.SecurityAlias
                LEFT JOIN data.TrackedSecurities ts WITH (NOLOCK) ON ts.SecurityAlias = sm.SecurityAlias
                WHERE sm.IsActive = 1 AND sm.IsEodhdUnavailable = 0 AND sm.IsEodhdComplete = 0 AND sm.IsTracked = 1
                  AND (pc.SecurityAlias IS NULL OR pc.GapDays > 0)
                ORDER BY Priority, MissingDays DESC, TickerSymbol";

            cmd.CommandTimeout = 30; // 30 sec — lightweight coverage table join, no Prices table scan

            // Read ALL gaps (typically <500 rows for tracked securities), count in C#
            var allGaps = new List<(int securityAlias, string ticker, bool isTracked, DateTime firstDate,
                DateTime lastDate, int actualDays, int expectedDays, int missingDays, int importanceScore)>();

            using (var reader = await cmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    allGaps.Add((
                        reader.GetInt32(0), reader.GetString(1), reader.GetBoolean(2),
                        reader.GetDateTime(3), reader.GetDateTime(4),
                        reader.GetInt32(5), reader.GetInt32(6), reader.GetInt32(7), reader.GetInt32(8)
                    ));
                }
            }

            // Compute counts in C# — eliminates the separate count query (was scanning Prices a 2nd time)
            var totalTrackedWithGaps = allGaps.Count;
            var totalAllMissingDays = allGaps.Sum(g => g.missingDays);

            // Cheap stats: SecurityMaster-only counts (no Prices table scan)
            // Previous design ran COUNT(DISTINCT) and COUNT(*) on Prices — a 3rd full table scan
            int totalTrackedSecurities = 0;
            int totalUntrackedSecurities = 0;

            int securitiesWithData = 0;
            int totalPriceRecords = 0;

            using (var statsCmd = connection.CreateCommand())
            {
                statsCmd.CommandText = @"
                    SELECT
                        (SELECT COUNT(*) FROM data.SecurityMaster WITH (NOLOCK)
                         WHERE IsActive = 1 AND IsTracked = 1) AS TotalTracked,
                        (SELECT COUNT(*) FROM data.SecurityMaster WITH (NOLOCK)
                         WHERE IsActive = 1 AND IsTracked = 0) AS TotalUntracked,
                        (SELECT COUNT(*) FROM data.SecurityPriceCoverage WITH (NOLOCK)) AS SecuritiesWithData,
                        (SELECT ISNULL(SUM(PriceCount), 0) FROM data.SecurityPriceCoverage WITH (NOLOCK)) AS TotalPriceRecords";

                statsCmd.CommandTimeout = 30;

                using var statsReader = await statsCmd.ExecuteReaderAsync();
                if (await statsReader.ReadAsync())
                {
                    totalTrackedSecurities = statsReader.GetInt32(0);
                    totalUntrackedSecurities = statsReader.GetInt32(1);
                    securitiesWithData = statsReader.GetInt32(2);
                    totalPriceRecords = statsReader.GetInt32(3);
                }
            }

            var totalSecurities = totalTrackedSecurities;
            // securitiesWithData is now populated from stats query (SecurityPriceCoverage count)
            var securitiesComplete = totalSecurities - totalTrackedWithGaps;

            // Return limited subset for the crawler, but with accurate total counts
            var securitiesWithGaps = allGaps.Take(maxResults).Select(g => new
            {
                securityAlias = g.securityAlias,
                ticker = g.ticker,
                isTracked = g.isTracked,
                firstDate = g.firstDate.ToString("yyyy-MM-dd"),
                lastDate = g.lastDate.ToString("yyyy-MM-dd"),
                actualDays = g.actualDays,
                expectedDays = g.expectedDays,
                missingDays = g.missingDays,
                importanceScore = g.importanceScore
            }).ToList();

            return Results.Ok(new
            {
                success = true,
                market = marketFilter,
                includeUntracked = false,
                summary = new
                {
                    totalSecurities,
                    totalTrackedSecurities,
                    totalUntrackedSecurities,
                    securitiesWithData,
                    securitiesWithGaps = totalTrackedWithGaps,
                    trackedWithGaps = totalTrackedWithGaps,
                    untrackedWithGaps = 0,
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
        catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number == -2) // SQL timeout
        {
            Log.Warning(ex, "Price gaps query timed out (SQL CommandTimeout exceeded)");
            return Results.Ok(new
            {
                success = false,
                error = "Query timed out. The gap analysis query is too expensive for the current database tier.",
                timedOut = true,
                market = market?.ToUpperInvariant() ?? "US",
                includeUntracked = false,
                gaps = Array.Empty<object>()
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

    // POST /api/admin/prices/mark-eodhd-complete - Mark a security as having all available EODHD data loaded
    // Called by the crawler when a full-history load returns 0 new records (EODHD data fully synced,
    // remaining business-calendar gaps are unfillable). Prevents wasting API calls on future gap queries.
    app.MapPost("/api/admin/prices/mark-eodhd-complete/{securityAlias}", async (IServiceProvider serviceProvider, int securityAlias) =>
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

            if (security.IsEodhdComplete)
            {
                return Results.Ok(new
                {
                    success = true,
                    message = $"Security {security.TickerSymbol} was already marked as EODHD complete",
                    ticker = security.TickerSymbol,
                    securityAlias
                });
            }

            security.IsEodhdComplete = true;
            security.UpdatedAt = DateTime.UtcNow;
            await context.SaveChangesAsync();

            Log.Information("Marked {Ticker} (alias {Alias}) as EODHD complete", security.TickerSymbol, securityAlias);

            return Results.Ok(new
            {
                success = true,
                message = $"Marked {security.TickerSymbol} as EODHD complete - will be skipped in future gap queries",
                ticker = security.TickerSymbol,
                securityAlias
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to mark security {SecurityAlias} as EODHD complete", securityAlias);
            return Results.Problem(ex.Message);
        }
    })
    .WithName("MarkEodhdComplete")
    .WithOpenApi()
    .Produces(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status404NotFound)
    .Produces(StatusCodes.Status500InternalServerError);

    // POST /api/admin/prices/bulk-mark-eodhd-complete - Bulk mark securities as EODHD complete
    // Marks all tracked securities that have at least N price records as IsEodhdComplete = true.
    // Logic: if we've loaded N+ records for a security, we've done a full-history load. Any remaining
    // gaps are unfillable (EODHD doesn't have data for those dates). This avoids burning API calls
    // one-by-one through the crawler. Use minPriceCount to control aggressiveness (default: 50).
    app.MapPost("/api/admin/prices/bulk-mark-eodhd-complete", async (IServiceProvider serviceProvider, HttpContext httpContext) =>
    {
        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetService<StockAnalyzerDbContext>();

        if (context == null)
            return Results.BadRequest(new { error = "Database context not configured" });

        try
        {
            // Parse optional minPriceCount parameter (default: 50)
            var minPriceCount = 50;
            if (httpContext.Request.Query.TryGetValue("minPriceCount", out var countStr) && int.TryParse(countStr, out var c))
                minPriceCount = c;

            // Preview mode: if dryRun=true, just return what would be marked
            var dryRun = httpContext.Request.Query.ContainsKey("dryRun");

            // CROSS APPLY uses IX_Prices_SecurityAlias_EffectiveDate index seek per security
            // instead of scanning the entire 7M+ row Prices table via GROUP BY JOIN
            var sql = @"
            SELECT sm.SecurityAlias, sm.TickerSymbol, ca.PriceCount
            FROM data.SecurityMaster sm
            CROSS APPLY (
                SELECT COUNT(*) AS PriceCount
                FROM data.Prices p WITH (NOLOCK)
                WHERE p.SecurityAlias = sm.SecurityAlias
            ) ca
            WHERE sm.IsTracked = 1
              AND sm.IsEodhdComplete = 0
              AND sm.IsEodhdUnavailable = 0
              AND ca.PriceCount >= @minPriceCount";

            var conn = context.Database.GetDbConnection();
            await context.Database.OpenConnectionAsync();

            // First, get the list of securities to mark
            // CA2100: sql is a hardcoded constant string with parameterized @minPriceCount — no injection risk
            using var cmd = conn.CreateCommand();
#pragma warning disable CA2100
            cmd.CommandText = sql;
#pragma warning restore CA2100
            var param = cmd.CreateParameter();
            param.ParameterName = "@minPriceCount";
            param.Value = minPriceCount;
            cmd.Parameters.Add(param);

            var candidates = new List<(int Alias, string Ticker, int Count)>();
            using (var reader = await cmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    candidates.Add((reader.GetInt32(0), reader.GetString(1), reader.GetInt32(2)));
                }
            }

            if (dryRun)
            {
                return Results.Ok(new
                {
                    success = true,
                    dryRun = true,
                    message = $"Would mark {candidates.Count} securities as EODHD complete (minPriceCount={minPriceCount})",
                    count = candidates.Count,
                    minPriceCount,
                    securities = candidates.Select(c => new { alias = c.Alias, ticker = c.Ticker, priceCount = c.Count })
                });
            }

            if (candidates.Count == 0)
            {
                return Results.Ok(new
                {
                    success = true,
                    message = "No securities matched criteria for bulk marking",
                    count = 0,
                    minPriceCount
                });
            }

            // Bulk update using parameterized IN clause
            var aliases = candidates.Select(c => c.Alias).ToList();
            using var updateCmd = conn.CreateCommand();
            var paramNames = new List<string>();
            for (int i = 0; i < aliases.Count; i++)
            {
                var pName = $"@a{i}";
                paramNames.Add(pName);
                var p = updateCmd.CreateParameter();
                p.ParameterName = pName;
                p.Value = aliases[i];
                updateCmd.Parameters.Add(p);
            }
#pragma warning disable CA2100 // Parameterized IN clause built from integer aliases — no injection risk
            updateCmd.CommandText = $@"
            UPDATE data.SecurityMaster
            SET IsEodhdComplete = 1, UpdatedAt = GETUTCDATE()
            WHERE SecurityAlias IN ({string.Join(",", paramNames)})";
#pragma warning restore CA2100

            var rowsAffected = await updateCmd.ExecuteNonQueryAsync();

            Log.Information("Bulk marked {Count} securities as EODHD complete (minPriceCount={MinPriceCount})",
                rowsAffected, minPriceCount);

            return Results.Ok(new
            {
                success = true,
                message = $"Marked {rowsAffected} securities as EODHD complete",
                count = rowsAffected,
                minPriceCount,
                securities = candidates.Select(c => new { alias = c.Alias, ticker = c.Ticker, priceCount = c.Count })
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to bulk mark securities as EODHD complete");
            return Results.Problem(ex.Message);
        }
    })
    .WithName("BulkMarkEodhdComplete")
    .WithOpenApi()
    .Produces(StatusCodes.Status200OK)
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
                    .Include(s => s.MicExchange)
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
                    micCode = s.MicCode,
                    exchangeName = s.MicExchange?.ExchangeName
                }).ToList();

                Log.Information("Reset IsEodhdUnavailable for ALL {Count} securities", unavailable.Count);
            }
            else
            {
                // Reset securities marked unavailable within the last N days (by UpdatedAt)
                var cutoff = DateTime.UtcNow.AddDays(-daysBack);
                var recentlyMarked = await context.SecurityMaster
                    .Include(s => s.MicExchange)
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
                    micCode = s.MicCode,
                    exchangeName = s.MicExchange?.ExchangeName
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

    // POST /api/admin/securities/calculate-importance - Calculate importance scores for all active securities
    // Primary signal: index membership (which indices a security belongs to, and how many)
    // Secondary signals: security type, MIC code quality
    // Penalties: OTC/Pink, warrants/rights, distressed securities
    app.MapPost("/api/admin/securities/calculate-importance", async (IServiceProvider serviceProvider) =>
    {
        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetService<StockAnalyzerDbContext>();

        if (context == null)
            return Results.BadRequest(new { error = "Database context not configured" });

        // Index tier classifications by IndexCode
        // Tier 1: Major broad-market indices (S&P 500, Russell 1000/2000/3000, MSCI flagship)
        var tier1Codes = new HashSet<string> { "SP500", "R1000", "R2000", "R3000", "MSCI_ACWI", "MSCI_EAFE", "MSCI_EM" };
        // Tier 2: Core style/size indices (S&P MidCap/SmallCap, Total Market, Core International)
        var tier2Codes = new HashSet<string> { "IJH", "IJR", "OEF", "ITOT", "IDEV", "IEMG", "IEFA", "IXUS" };

        // Local function to calculate importance score with index membership data
        int CalculateImportanceScore(SecurityMasterEntity sec,
            int tier1Count, int tier2Count, int totalIndexCount)
        {
            var score = 1; // Base score

            // Primary signal: Index membership (0-6 points)
            if (tier1Count >= 2)
                score += 6;       // In multiple flagship indices (e.g., SP500 + R1000 + R3000)
            else if (tier1Count == 1)
                score += 5;       // In one flagship index (e.g., R2000 only)
            else if (tier2Count >= 2)
                score += 4;       // In multiple core indices
            else if (tier2Count == 1)
                score += 3;       // In one core index
            else if (totalIndexCount >= 3)
                score += 2;       // In several thematic/sector indices
            else if (totalIndexCount >= 1)
                score += 1;       // In at least one index

            // Breadth bonus: held across many indices (0-1 points)
            if (totalIndexCount >= 8)
                score += 1;

            // Secondary signal: Security type (0-1 points)
            var secType = sec.SecurityType?.ToUpperInvariant() ?? "";
            if (secType.Contains("COMMON STOCK"))
                score += 1;

            // Secondary signal: MIC code quality (0-1 points)
            var mic = sec.MicCode;
            if (mic != null && bonusMics.Contains(mic))
                score += 1;
            if (mic != null && penaltyMics.Contains(mic))
                score -= 2;

            // Penalties
            if (secType.Contains("PREFERRED") || secType.Contains("WARRANT") || secType.Contains("RIGHT"))
                score -= 2;
            if (secType.Contains("OTC") || secType.Contains("PINK") || secType.Contains("GREY"))
                score -= 2;

            var name = sec.IssueName?.ToUpperInvariant() ?? "";
            if (name.Contains("WARRANT") || name.Contains("RIGHT") || name.Contains(" UNIT") || name.Contains("UNITS"))
                score -= 2;
            if (name.Contains("LIQUIDATING") || name.Contains("BANKRUPT") || name.Contains("LIQUIDATION"))
                score -= 3;

            return Math.Clamp(score, 1, 10);
        }

        // Concurrency guard — only one calculation at a time
        if (!await calculateImportanceSemaphore.WaitAsync(0))
            return Results.Conflict(new { error = "Importance score calculation already in progress." });

        try
        {
            Log.Information("Starting importance score calculation for all active securities");

            // Pre-load index tier mappings: IndexId → tier (1, 2, or 3)
            // Wrapped in try/catch: if index tables don't exist yet (pending migration),
            // falls back to heuristic-only scoring (no index membership data)
            var indexTiers = new Dictionary<int, int>();
            var indexMembership = new Dictionary<int, HashSet<int>>();
            var hasIndexData = false;
            var indexDefCount = 0;

            try
            {
                var indexDefs = await context.IndexDefinitions.AsNoTracking().ToListAsync();
                indexDefCount = indexDefs.Count;
                foreach (var idx in indexDefs)
                {
                    if (tier1Codes.Contains(idx.IndexCode))
                        indexTiers[idx.IndexId] = 1;
                    else if (tier2Codes.Contains(idx.IndexCode))
                        indexTiers[idx.IndexId] = 2;
                    else
                        indexTiers[idx.IndexId] = 3;
                }

                // Pre-load index membership: SecurityAlias → set of IndexIds (from latest snapshot per index)
                // Single efficient query using raw SQL to avoid N+1 and minimize DTU usage
                if (indexDefs.Count > 0)
                {
                    var conn = context.Database.GetDbConnection();
                    await context.Database.OpenConnectionAsync();
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = @"
                    ;WITH LatestPerIndex AS (
                        SELECT IndexId, MAX(EffectiveDate) AS MaxDate
                        FROM data.IndexConstituent WITH (NOLOCK)
                        GROUP BY IndexId
                    )
                    SELECT c.SecurityAlias, c.IndexId
                    FROM data.IndexConstituent c WITH (NOLOCK)
                    INNER JOIN LatestPerIndex l ON c.IndexId = l.IndexId AND c.EffectiveDate = l.MaxDate";
                    cmd.CommandTimeout = 60;

                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        var secAlias = reader.GetInt32(0);
                        var indexId = reader.GetInt32(1);
                        if (!indexMembership.ContainsKey(secAlias))
                            indexMembership[secAlias] = new HashSet<int>();
                        indexMembership[secAlias].Add(indexId);
                        hasIndexData = true;
                    }
                }
            }
            catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Message.Contains("Invalid object name"))
            {
                Log.Warning("Index tables not found in database — falling back to heuristic-only scoring");
                indexTiers.Clear();
                indexMembership.Clear();
                hasIndexData = false;
                indexDefCount = 0;
            }

            Log.Information("Loaded index membership for {Count} securities from {IndexCount} indices",
                indexMembership.Count, indexDefCount);

            var scoreDistribution = new Dictionary<int, int>();
            var updated = 0;
            var totalProcessed = 0;

            // Process in pages of 1000 to avoid loading all 55K+ entities at once
            var pageSize = 1000;
            var skip = 0;

            while (true)
            {
                var batch = await context.SecurityMaster
                    .Where(s => s.IsActive)
                    .OrderBy(s => s.SecurityAlias)
                    .Skip(skip)
                    .Take(pageSize)
                    .ToListAsync();

                if (batch.Count == 0) break;

                foreach (var security in batch)
                {
                    // Look up index membership for this security
                    var tier1Count = 0;
                    var tier2Count = 0;
                    var totalIndexCount = 0;

                    if (indexMembership.TryGetValue(security.SecurityAlias, out var indexIds))
                    {
                        totalIndexCount = indexIds.Count;
                        foreach (var indexId in indexIds)
                        {
                            if (indexTiers.TryGetValue(indexId, out var tier))
                            {
                                if (tier == 1) tier1Count++;
                                else if (tier == 2) tier2Count++;
                            }
                        }
                    }

                    var score = CalculateImportanceScore(security, tier1Count, tier2Count, totalIndexCount);

                    if (security.ImportanceScore != score)
                    {
                        security.ImportanceScore = score;
                        security.UpdatedAt = DateTime.UtcNow;
                        updated++;
                    }

                    if (!scoreDistribution.ContainsKey(score))
                        scoreDistribution[score] = 0;
                    scoreDistribution[score]++;
                }

                await context.SaveChangesAsync();
                context.ChangeTracker.Clear();
                totalProcessed += batch.Count;
                skip += pageSize;
            }

            Log.Information("Updated importance scores for {Count} securities (index data available: {HasIndex})",
                updated, hasIndexData);

            return Results.Ok(new
            {
                success = true,
                message = $"Calculated importance scores for {totalProcessed} active securities",
                totalProcessed,
                updated,
                indexDataAvailable = hasIndexData,
                securitiesWithIndexMembership = indexMembership.Count,
                distribution = scoreDistribution.OrderByDescending(x => x.Key).ToDictionary(x => x.Key, x => x.Value)
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to calculate importance scores");
            return Results.Problem(ex.Message);
        }
        finally
        {
            calculateImportanceSemaphore.Release();
        }
    })
    .WithName("CalculateImportanceScores")
    .WithOpenApi()
    .Produces(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status400BadRequest)
    .Produces(StatusCodes.Status500InternalServerError);

    // POST /api/admin/securities/backfill-mic-codes - Backfill MIC codes from EODHD exchange symbols
    // Fetches all US symbols from EODHD, maps exchange names to ISO 10383 MIC codes,
    // and bulk-updates SecurityMaster.MicCode for active securities.
    app.MapPost("/api/admin/securities/backfill-mic-codes", async (IServiceProvider serviceProvider) =>
    {
        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetService<StockAnalyzerDbContext>();
        var eodhdService = scope.ServiceProvider.GetService<EodhdService>();

        if (context == null)
            return Results.BadRequest(new { error = "Database context not configured" });

        if (eodhdService == null)
            return Results.BadRequest(new { error = "EODHD service not available" });

        // Concurrency guard — only one backfill at a time
        if (!await backfillMicCodesSemaphore.WaitAsync(0))
            return Results.Conflict(new { error = "MIC code backfill already in progress." });

        try
        {
            Log.Information("Starting MIC code backfill from EODHD exchange symbols");

            // EODHD exchange name → ISO 10383 MIC code mapping (case-insensitive)
            var exchangeMapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
            { "NYSE", "XNYS" },
            { "NASDAQ", "XNAS" },
            { "NYSE ARCA", "ARCX" },
            { "ARCA", "ARCX" },
            { "BATS", "BATS" },
            { "NYSE MKT", "XNYS" },
            { "OTC", "OTCM" },
            { "PINK", "PINX" },
            { "OTCQB", "OTCM" },
            { "OTCQX", "OTCM" },
            { "OTCMKTS", "OTCM" },
            { "OTCBB", "OTCM" },
            { "OTCGREY", "XOTC" },
            { "NMFQS", "XNAS" },
            };

            // Fetch all US symbols from EODHD (single API call)
            var symbols = await eodhdService.GetExchangeSymbolsAsync("US");
            Log.Information("Fetched {Count} US symbols from EODHD", symbols.Count);

            // Build ticker → MIC lookup and track unknown exchanges
            var tickerToMic = new Dictionary<string, string>();
            var unknownExchanges = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var symbol in symbols)
            {
                if (exchangeMapping.TryGetValue(symbol.Exchange, out var mic))
                {
                    tickerToMic[symbol.Code] = mic;
                }
                else
                {
                    // Unknown exchange names are logged but don't fail processing (AC3.5)
                    if (!unknownExchanges.ContainsKey(symbol.Exchange))
                        unknownExchanges[symbol.Exchange] = 0;
                    unknownExchanges[symbol.Exchange]++;
                }
            }

            // Log unknown exchanges as warnings
            foreach (var (exchange, count) in unknownExchanges.OrderByDescending(x => x.Value))
            {
                Log.Warning("Unknown EODHD exchange: {Exchange} ({Count} symbols)",
                    LogSanitizer.Sanitize(exchange), LogSanitizer.Sanitize(count.ToString()));
            }

            Log.Information("Built ticker → MIC lookup with {Count} mapped tickers, {UnknownCount} unknown exchanges",
                tickerToMic.Count, unknownExchanges.Count);

            // Batch update SecurityMaster.MicCode (1000 per batch)
            var pageSize = 1000;
            var skip = 0;
            var matched = 0;
            var unmatched = 0;

            while (true)
            {
                var batch = await context.SecurityMaster
                    .Where(s => s.IsActive)
                    .OrderBy(s => s.SecurityAlias)
                    .Skip(skip)
                    .Take(pageSize)
                    .ToListAsync();

                if (batch.Count == 0) break;

                foreach (var security in batch)
                {
                    if (tickerToMic.TryGetValue(security.TickerSymbol, out var mic))
                    {
                        security.MicCode = mic;
                        security.UpdatedAt = DateTime.UtcNow;
                        matched++;
                    }
                    else
                    {
                        unmatched++;
                    }
                }

                await context.SaveChangesAsync();
                context.ChangeTracker.Clear();
                skip += pageSize;

                Log.Information("Processed batch of {Count} securities (matched: {Matched}, unmatched: {Unmatched})",
                    batch.Count, matched, unmatched);
            }

            Log.Information("MIC code backfill complete: {Matched} matched, {Unmatched} unmatched",
                matched, unmatched);

            // Build response with unknown exchanges list
            var unknownExchangesList = unknownExchanges
                .Select(x => new { exchange = x.Key, count = x.Value })
                .OrderByDescending(x => x.count)
                .ToList();

            return Results.Ok(new
            {
                success = true,
                message = "MIC code backfill complete",
                totalEodhdSymbols = symbols.Count,
                matched,
                unmatched,
                unknownExchanges = unknownExchangesList
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to backfill MIC codes");
            return Results.Problem(ex.Message);
        }
        finally
        {
            backfillMicCodesSemaphore.Release();
        }
    })
    .WithName("BackfillMicCodes")
    .WithOpenApi()
    .Produces(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status400BadRequest)
    .Produces(StatusCodes.Status409Conflict)
    .Produces(StatusCodes.Status500InternalServerError);

    // POST /api/admin/securities/promote-untracked - Promote untracked securities to tracked
    // Selects top N untracked securities by ImportanceScore (highest first), marks them as tracked.
    // Used by the crawler to incrementally grow the tracked universe without expensive gap queries.
    app.MapPost("/api/admin/securities/promote-untracked", async (IServiceProvider serviceProvider, int? count) =>
    {
        var promoteCount = Math.Clamp(count ?? 100, 1, 500);

        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetService<StockAnalyzerDbContext>();

        if (context == null)
            return Results.BadRequest(new { error = "Database context not configured" });

        try
        {
            var connection = context.Database.GetDbConnection();
            await connection.OpenAsync();

            // Select top N untracked securities by ImportanceScore DESC
            using var selectCmd = connection.CreateCommand();
            selectCmd.CommandText = @"
            SELECT TOP (@count) SecurityAlias, TickerSymbol
            FROM data.SecurityMaster WITH (NOLOCK)
            WHERE IsActive = 1 AND IsEodhdUnavailable = 0 AND IsTracked = 0
            ORDER BY ImportanceScore DESC, TickerSymbol";

            var countParam = selectCmd.CreateParameter();
            countParam.ParameterName = "@count";
            countParam.Value = promoteCount;
            selectCmd.Parameters.Add(countParam);

            var toPromote = new List<(int SecurityAlias, string Ticker)>();
            using (var reader = await selectCmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    toPromote.Add((reader.GetInt32(0), reader.GetString(1)));
                }
            }

            if (toPromote.Count == 0)
            {
                return Results.Ok(new
                {
                    success = true,
                    promoted = 0,
                    tickers = Array.Empty<string>(),
                    message = "No untracked securities remaining to promote."
                });
            }

            // Insert into TrackedSecurities + update IsTracked flag using temp table
            using var tx = await connection.BeginTransactionAsync();

            // Create temp table and populate with parameterized inserts
            using var createTempCmd = connection.CreateCommand();
            createTempCmd.Transaction = (System.Data.Common.DbTransaction)tx;
            createTempCmd.CommandText = "CREATE TABLE #PromoteAliases (SecurityAlias INT NOT NULL PRIMARY KEY);";
            await createTempCmd.ExecuteNonQueryAsync();

            // Insert each alias individually with parameter
            foreach (var (alias, _) in toPromote)
            {
                using var insertAliasCmd = connection.CreateCommand();
                insertAliasCmd.Transaction = (System.Data.Common.DbTransaction)tx;
                insertAliasCmd.CommandText = "INSERT INTO #PromoteAliases (SecurityAlias) VALUES (@alias);";
                var aliasParam = insertAliasCmd.CreateParameter();
                aliasParam.ParameterName = "@alias";
                aliasParam.Value = alias;
                insertAliasCmd.Parameters.Add(aliasParam);
                await insertAliasCmd.ExecuteNonQueryAsync();
            }

            // Promote: insert into TrackedSecurities + update IsTracked
            using var promoteCmd = connection.CreateCommand();
            promoteCmd.Transaction = (System.Data.Common.DbTransaction)tx;
            promoteCmd.CommandText = @"
            INSERT INTO data.TrackedSecurities (SecurityAlias, Source, Priority, Notes, AddedBy)
            SELECT sm.SecurityAlias, 'auto-promote', 5, 'Promoted from untracked by crawler', 'crawler'
            FROM data.SecurityMaster sm
            INNER JOIN #PromoteAliases pa ON pa.SecurityAlias = sm.SecurityAlias
            WHERE NOT EXISTS (SELECT 1 FROM data.TrackedSecurities ts WHERE ts.SecurityAlias = sm.SecurityAlias);

            UPDATE sm SET sm.IsTracked = 1, sm.UpdatedAt = GETUTCDATE()
            FROM data.SecurityMaster sm
            INNER JOIN #PromoteAliases pa ON pa.SecurityAlias = sm.SecurityAlias
            WHERE sm.IsTracked = 0;";

            await promoteCmd.ExecuteNonQueryAsync();
            await tx.CommitAsync();

            var tickers = toPromote.Select(t => t.Ticker).ToArray();
            Log.Information("Promoted {Count} securities to tracked: {Tickers}", tickers.Length, string.Join(", ", tickers.Take(10)));

            return Results.Ok(new
            {
                success = true,
                promoted = tickers.Length,
                tickers,
                message = $"Promoted {tickers.Length} securities to tracked status."
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to promote untracked securities");
            return Results.Problem(ex.Message);
        }
    })
    .WithName("PromoteUntrackedSecurities")
    .WithOpenApi()
    .Produces(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status400BadRequest)
    .Produces(StatusCodes.Status500InternalServerError);

    // GET /api/admin/dashboard/stats - Consolidated dashboard stats for EODHD Loader
    // Universe counts from SecurityMaster (small table, instant).
    // Price stats + tiers + coverage from CoverageSummary (pre-aggregated, instant).
    // NO direct queries on the 7M+ row Prices table — avoids DTU exhaustion on Azure SQL Basic.
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

            // Query 1: Universe counts from SecurityMaster (small table, instant)
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
            SELECT
                COUNT(*) AS TotalSecurities,
                SUM(CASE WHEN IsTracked = 1 THEN 1 ELSE 0 END) AS Tracked,
                SUM(CASE WHEN IsTracked = 0 THEN 1 ELSE 0 END) AS Untracked,
                SUM(CASE WHEN IsEodhdUnavailable = 1 THEN 1 ELSE 0 END) AS Unavailable
            FROM data.SecurityMaster WITH (NOLOCK)
            WHERE IsActive = 1;
        ";
            cmd.CommandTimeout = 15;

            object? universeData = null;
            object? pricesData = null;
            var tiers = new List<object>();

            using var reader = await cmd.ExecuteReaderAsync();

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

            await reader.CloseAsync();

            // All remaining data from CoverageSummary (pre-aggregated, instant — no Prices table scan)
            var summaryRows = await context.CoverageSummary
                .AsNoTracking()
                .OrderByDescending(s => s.Year)
                .ToListAsync();

            // CoverageSummary freshness — when was it last refreshed?
            var summaryLastRefreshed = summaryRows.Any()
                ? summaryRows.Max(r => r.LastUpdatedAt).ToString("o")
                : (string?)null;

            // Real-time total record count from SQL Server metadata (instant, zero DTU)
            // sys.dm_db_partition_stats maintains row counts without scanning the table
            long totalRecordsLive = 0;
            using (var countCmd = connection.CreateCommand())
            {
                countCmd.CommandText = @"
                SELECT SUM(p.row_count)
                FROM sys.dm_db_partition_stats p
                INNER JOIN sys.tables t ON p.object_id = t.object_id
                INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
                WHERE s.name = 'data' AND t.name = 'Prices' AND p.index_id IN (0, 1)";
                countCmd.CommandTimeout = 5;
                var countObj = await countCmd.ExecuteScalarAsync();
                if (countObj is long l) totalRecordsLive = l;
                else if (countObj is decimal dec) totalRecordsLive = (long)dec;
                else if (countObj != null && countObj != DBNull.Value) totalRecordsLive = Convert.ToInt64(countObj);
            }

            // Use CoverageSummary for breakdown stats, but real-time count for the headline number
            if (summaryRows.Any())
            {
                var distinctSecurities = summaryRows
                    .GroupBy(r => r.ImportanceScore)
                    .Sum(g => g.Max(r => r.TrackedSecurities + r.UntrackedSecurities));
                var minYear = summaryRows.Min(r => r.Year);

                // Fast latest date lookup — TOP 1 with index seek, avoids full table scan
                string? latestDate = null;
                using var dateCmd = connection.CreateCommand();
                dateCmd.CommandText = "SELECT TOP 1 EffectiveDate FROM data.Prices WITH (NOLOCK) ORDER BY EffectiveDate DESC";
                dateCmd.CommandTimeout = 5;
                var latestObj = await dateCmd.ExecuteScalarAsync();
                if (latestObj is DateTime dt)
                    latestDate = dt.ToString("yyyy-MM-dd");

                pricesData = new
                {
                    totalRecords = (int)Math.Min(totalRecordsLive, int.MaxValue),
                    distinctSecurities,
                    oldestDate = $"{minYear}-01-01",
                    latestDate
                };
            }
            else
            {
                // No CoverageSummary data at all — still show the real-time count
                string? latestDate = null;
                using var dateCmd = connection.CreateCommand();
                dateCmd.CommandText = "SELECT TOP 1 EffectiveDate FROM data.Prices WITH (NOLOCK) ORDER BY EffectiveDate DESC";
                dateCmd.CommandTimeout = 5;
                var latestObj = await dateCmd.ExecuteScalarAsync();
                if (latestObj is DateTime dt)
                    latestDate = dt.ToString("yyyy-MM-dd");

                pricesData = new
                {
                    totalRecords = (int)Math.Min(totalRecordsLive, int.MaxValue),
                    distinctSecurities = 0,
                    oldestDate = (string?)null,
                    latestDate
                };
            }

            // Derive importance tier distribution from CoverageSummary
            // (replaces expensive LEFT JOIN with SELECT DISTINCT on Prices)
            var tierGroups = summaryRows
                .GroupBy(r => r.ImportanceScore)
                .OrderByDescending(g => g.Key);

            foreach (var g in tierGroups)
            {
                // Count securities with prices in this tier from CoverageSummary
                var withPrices = g.Max(r => r.TrackedSecurities + r.UntrackedSecurities);
                tiers.Add(new
                {
                    score = g.Key,
                    total = withPrices,
                    withPrices,
                    unavailable = 0
                });
            }

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
                summaryLastRefreshed,
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
                .AsNoTracking()
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
        // Concurrency guard — only one refresh at a time (this is the most expensive query)
        if (!await refreshSummarySemaphore.WaitAsync(0))
            return Results.Conflict(new { error = "Summary refresh already in progress. Try again later." });

        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetService<StockAnalyzerDbContext>();

        if (context == null)
        {
            refreshSummarySemaphore.Release();
            return Results.BadRequest(new { error = "Database context not configured" });
        }

        try
        {
            var connection = context.Database.GetDbConnection();
            await connection.OpenAsync();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
            SELECT
                cy.[Year],
                sm.ImportanceScore AS Score,
                SUM(CASE WHEN sm.IsTracked = 1 THEN cy.PriceCount ELSE 0 END) AS TrackedRecords,
                SUM(CASE WHEN sm.IsTracked = 0 THEN cy.PriceCount ELSE 0 END) AS UntrackedRecords,
                SUM(CASE WHEN sm.IsTracked = 1 THEN 1 ELSE 0 END) AS TrackedSecurities,
                SUM(CASE WHEN sm.IsTracked = 0 THEN 1 ELSE 0 END) AS UntrackedSecurities,
                (SELECT COUNT(*) FROM data.BusinessCalendar bc WITH (NOLOCK) WHERE bc.SourceId = 1 AND bc.IsBusinessDay = 1 AND YEAR(bc.EffectiveDate) = cy.[Year]) AS TradingDays
            FROM data.SecurityPriceCoverageByYear cy WITH (NOLOCK)
            INNER JOIN data.SecurityMaster sm WITH (NOLOCK)
                ON cy.SecurityAlias = sm.SecurityAlias
            WHERE sm.IsActive = 1
            GROUP BY cy.[Year], sm.ImportanceScore
            ORDER BY [Year], Score;
        ";
            cmd.CommandTimeout = 30; // 30 sec — reduced from 300 sec, now aggregates from pre-computed coverage metadata instead of 43M+ Prices rows

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
        finally
        {
            refreshSummarySemaphore.Release();
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

            // Batch insert in chunks of 2000 to avoid excessive memory/DTU usage
            foreach (var chunk in entries.Chunk(2000))
            {
                dbContext.BusinessCalendar.AddRange(chunk);
                await dbContext.SaveChangesAsync();
                dbContext.ChangeTracker.Clear();
            }

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

// Compute a short SHA256 hash of all JS files in wwwroot/js/ for cache-busting.
// Changes to any JS file produce a different hash → browsers fetch the new version.
static string ComputeJsContentHash(string webRootPath)
{
    var jsDir = Path.Combine(webRootPath, "js");
    if (!Directory.Exists(jsDir))
        return "000000";

    using var sha = SHA256.Create();
    foreach (var file in Directory.GetFiles(jsDir, "*.js").Order())
    {
        var bytes = File.ReadAllBytes(file);
        sha.TransformBlock(bytes, 0, bytes.Length, null, 0);
    }
    sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
    return Convert.ToHexString(sha.Hash!)[..8].ToLowerInvariant();
}

// Request models for admin endpoints
public class ResetTrackedRequest
{
    public List<string> Tickers { get; set; } = new();
    public string? Source { get; set; }
    public int? Priority { get; set; }
}

// Required for WebApplicationFactory<Program> in integration tests
public partial class Program { }
