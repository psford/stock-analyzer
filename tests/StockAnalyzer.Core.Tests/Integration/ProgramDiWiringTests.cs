using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StockAnalyzer.Api;
using StockAnalyzer.Core.Data;
using StockAnalyzer.Core.Data.Entities;
using StockAnalyzer.Core.Services;
using Xunit;

namespace StockAnalyzer.Core.Tests.Integration;

// Serialize DI wiring tests — both factories mutate WSL_SQL_CONNECTION env var
[CollectionDefinition("DI Wiring", DisableParallelization = true)]
public class DiWiringCollection { }

/// <summary>
/// Validates that Program.cs DI wiring resolves all registered services
/// in both SQL and JSON (no connection string) branches.
/// </summary>
public class ProgramDiWiringTests
{
    /// <summary>
    /// JSON/fallback branch: no connection string set.
    /// Services that require SQL should NOT be registered.
    /// </summary>
    [Collection("DI Wiring")]
    public class JsonBranch : IClassFixture<JsonBranchFactory>
    {
        private readonly JsonBranchFactory _factory;

        public JsonBranch(JsonBranchFactory factory) => _factory = factory;

        [Fact]
        public void WatchlistRepository_Resolves_ToJson()
        {
            using var scope = _factory.Services.CreateScope();
            var repo = scope.ServiceProvider.GetService<IWatchlistRepository>();
            repo.Should().NotBeNull();
            repo.Should().BeOfType<JsonWatchlistRepository>();
        }

        [Fact]
        public void WatchlistService_Resolves()
        {
            using var scope = _factory.Services.CreateScope();
            var svc = scope.ServiceProvider.GetService<WatchlistService>();
            svc.Should().NotBeNull();
        }

        [Fact]
        public void AggregatedStockDataService_Resolves()
        {
            using var scope = _factory.Services.CreateScope();
            var svc = scope.ServiceProvider.GetService<AggregatedStockDataService>();
            svc.Should().NotBeNull();
        }

        [Fact]
        public void NewsService_Resolves()
        {
            using var scope = _factory.Services.CreateScope();
            var svc = scope.ServiceProvider.GetService<NewsService>();
            svc.Should().NotBeNull();
        }

        [Fact]
        public void AggregatedNewsService_Resolves()
        {
            using var scope = _factory.Services.CreateScope();
            var svc = scope.ServiceProvider.GetService<AggregatedNewsService>();
            svc.Should().NotBeNull();
        }

        [Fact]
        public void DbContext_NotRegistered_InJsonBranch()
        {
            using var scope = _factory.Services.CreateScope();
            var ctx = scope.ServiceProvider.GetService<StockAnalyzerDbContext>();
            ctx.Should().BeNull("JSON branch should not register DbContext");
        }

        [Fact]
        public void SqlRepositories_NotRegistered_InJsonBranch()
        {
            using var scope = _factory.Services.CreateScope();
            var symbolRepo = scope.ServiceProvider.GetService<ISymbolRepository>();
            symbolRepo.Should().BeNull("JSON branch should not register SQL symbol repository");

            var priceRepo = scope.ServiceProvider.GetService<IPriceRepository>();
            priceRepo.Should().BeNull("JSON branch should not register SQL price repository");
        }
    }

    /// <summary>
    /// SQL branch: WSL_SQL_CONNECTION is set (uses in-memory EF provider to avoid real DB).
    /// </summary>
    [Collection("DI Wiring")]
    public class SqlBranch : IClassFixture<SqlBranchFactory>
    {
        private readonly SqlBranchFactory _factory;

        public SqlBranch(SqlBranchFactory factory) => _factory = factory;

        [Fact]
        public void WatchlistRepository_Resolves_ToSql()
        {
            using var scope = _factory.Services.CreateScope();
            var repo = scope.ServiceProvider.GetService<IWatchlistRepository>();
            repo.Should().NotBeNull();
            repo.Should().BeOfType<SqlWatchlistRepository>();
        }

        [Fact]
        public void WatchlistService_Resolves()
        {
            using var scope = _factory.Services.CreateScope();
            var svc = scope.ServiceProvider.GetService<WatchlistService>();
            svc.Should().NotBeNull();
        }

        [Fact]
        public void DbContext_Resolves_InSqlBranch()
        {
            using var scope = _factory.Services.CreateScope();
            var ctx = scope.ServiceProvider.GetService<StockAnalyzerDbContext>();
            ctx.Should().NotBeNull("SQL branch must register DbContext");
        }

        [Fact]
        public void SymbolRepository_Resolves_InSqlBranch()
        {
            using var scope = _factory.Services.CreateScope();
            var repo = scope.ServiceProvider.GetService<ISymbolRepository>();
            repo.Should().NotBeNull();
            repo.Should().BeOfType<SqlSymbolRepository>();
        }

        [Fact]
        public void SecurityMasterRepository_Resolves_InSqlBranch()
        {
            using var scope = _factory.Services.CreateScope();
            var repo = scope.ServiceProvider.GetService<ISecurityMasterRepository>();
            repo.Should().NotBeNull();
        }

        [Fact]
        public void PriceRepository_Resolves_InSqlBranch()
        {
            using var scope = _factory.Services.CreateScope();
            var repo = scope.ServiceProvider.GetService<IPriceRepository>();
            repo.Should().NotBeNull();
        }

        [Fact]
        public void AggregatedStockDataService_Resolves()
        {
            using var scope = _factory.Services.CreateScope();
            var svc = scope.ServiceProvider.GetService<AggregatedStockDataService>();
            svc.Should().NotBeNull();
        }
    }
}

/// <summary>
/// Factory for JSON/fallback DI branch — no SQL connection string.
/// Env var must be cleared before host builds (Program.cs reads it at startup).
/// </summary>
public class JsonBranchFactory : WebApplicationFactory<Program>
{
    public JsonBranchFactory()
    {
        // Explicitly clear WSL_SQL_CONNECTION to test JSON mode
        // Setting to null doesn't work - use empty string
        Environment.SetEnvironmentVariable("WSL_SQL_CONNECTION", "");

        // Set dummy values for all required API keys so EndpointRegistry validation passes
        Environment.SetEnvironmentVariable("TWELVEDATA_API_KEY", "test-dummy-key");
        Environment.SetEnvironmentVariable("FMP_API_KEY", "test-dummy-key");
        Environment.SetEnvironmentVariable("FINNHUB_API_KEY", "test-dummy-key");
        Environment.SetEnvironmentVariable("EODHD_API_KEY", "test-dummy-key");
        Environment.SetEnvironmentVariable("MARKETAUX_API_TOKEN", "test-dummy-key");

        // Reset EndpointRegistry so it reloads with updated env vars
        EndpointRegistry.Reset();
    }

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:DefaultConnection", null);
    }
}

/// <summary>
/// Factory for SQL DI branch — sets WSL_SQL_CONNECTION, replaces DbContext with InMemory.
/// Env var must be set before host builds (Program.cs reads it at startup).
/// </summary>
public class SqlBranchFactory : WebApplicationFactory<Program>
{
    public SqlBranchFactory()
    {
        Environment.SetEnvironmentVariable("WSL_SQL_CONNECTION",
            "Server=localhost;Database=Test;Trusted_Connection=True;TrustServerCertificate=True;");
        // Set dummy values for all required API keys so EndpointRegistry validation passes
        Environment.SetEnvironmentVariable("TWELVEDATA_API_KEY", "test-dummy-key");
        Environment.SetEnvironmentVariable("FMP_API_KEY", "test-dummy-key");
        Environment.SetEnvironmentVariable("FINNHUB_API_KEY", "test-dummy-key");
        Environment.SetEnvironmentVariable("EODHD_API_KEY", "test-dummy-key");
        Environment.SetEnvironmentVariable("MARKETAUX_API_TOKEN", "test-dummy-key");

        // Reset EndpointRegistry so it reloads with updated env vars
        EndpointRegistry.Reset();
    }

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Remove the real SQL Server DbContext and replace with InMemory
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(Microsoft.EntityFrameworkCore.DbContextOptions<StockAnalyzerDbContext>));
            if (descriptor != null)
                services.Remove(descriptor);

            services.AddDbContext<StockAnalyzerDbContext>(options =>
                options.UseInMemoryDatabase("DiWiringTest"));
        });
    }
}
