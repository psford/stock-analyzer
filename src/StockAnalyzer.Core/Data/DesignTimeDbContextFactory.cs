using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace StockAnalyzer.Core.Data;

/// <summary>
/// Design-time factory for EF Core migrations.
/// Used by 'dotnet ef migrations' commands when the DbContext
/// isn't registered in the DI container (e.g., when using JSON storage mode).
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<StockAnalyzerDbContext>
{
    public StockAnalyzerDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<StockAnalyzerDbContext>();

        // Use a dummy connection string for design-time migrations
        // The actual connection string comes from Azure configuration at runtime
        optionsBuilder.UseSqlServer(
            "Server=(localdb)\\mssqllocaldb;Database=StockAnalyzer_Design;Trusted_Connection=True;");

        return new StockAnalyzerDbContext(optionsBuilder.Options);
    }
}
