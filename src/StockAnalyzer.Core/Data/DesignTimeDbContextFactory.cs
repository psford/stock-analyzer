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
        // SA_DESIGN_CONNECTION: Used in WSL2 with TCP connection to Windows SQL Express.
        // Uses the admin login (wsl_claude_admin) because migrations need DDL permissions.
        // Fallback: Windows localdb for existing Windows development workflow.
        var connectionString = Environment.GetEnvironmentVariable("SA_DESIGN_CONNECTION")
            ?? "Server=(localdb)\\mssqllocaldb;Database=StockAnalyzer_Design;Trusted_Connection=True;";

        var optionsBuilder = new DbContextOptionsBuilder<StockAnalyzerDbContext>();
        optionsBuilder.UseSqlServer(connectionString);

        return new StockAnalyzerDbContext(optionsBuilder.Options);
    }
}
