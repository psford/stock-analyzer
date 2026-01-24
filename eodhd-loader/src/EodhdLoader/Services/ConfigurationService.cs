using System.IO;
using DotNetEnv;

namespace EodhdLoader.Services;

/// <summary>
/// Loads configuration from .env files and provides connection strings.
/// Searches multiple locations for the .env file.
/// </summary>
public class ConfigurationService
{
    private static readonly string[] EnvSearchPaths =
    [
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".env"),
        @"c:\Users\patri\Documents\claudeProjects\projects\eodhd-loader\.env",
        @"c:\Users\patri\Documents\claudeProjects\projects\stock-analyzer\.env",
        @"c:\Users\patri\Documents\claudeProjects\.env"
    ];

    public string? EodhdApiKey { get; private set; }
    public string LocalConnectionString { get; private set; } = string.Empty;
    public string LocalApiUrl { get; private set; } = string.Empty;
    public string? ProductionConnectionString { get; private set; }
    public string? ProductionApiUrl { get; private set; }
    public bool IsLoaded { get; private set; }
    public string? LoadedEnvPath { get; private set; }

    public ConfigurationService()
    {
        LoadConfiguration();
    }

    private void LoadConfiguration()
    {
        // Find and load .env file
        foreach (var path in EnvSearchPaths)
        {
            if (File.Exists(path))
            {
                Env.Load(path);
                LoadedEnvPath = path;
                break;
            }
        }

        // Load EODHD API key
        EodhdApiKey = Environment.GetEnvironmentVariable("EODHD_API_KEY");

        // Load Local environment settings
        LocalConnectionString = Environment.GetEnvironmentVariable("LOCAL_SQL_CONNECTION")
            ?? "Server=.\\SQLEXPRESS;Database=StockAnalyzer;Trusted_Connection=True;TrustServerCertificate=True";

        LocalApiUrl = Environment.GetEnvironmentVariable("LOCAL_API_URL")
            ?? "http://localhost:5000";

        // Load Production environment settings
        ProductionConnectionString = Environment.GetEnvironmentVariable("PROD_SQL_CONNECTION");
        ProductionApiUrl = Environment.GetEnvironmentVariable("PROD_API_URL")
            ?? "https://psfordtaurus.com";

        IsLoaded = !string.IsNullOrEmpty(EodhdApiKey);
    }

    public bool HasEodhdKey => !string.IsNullOrEmpty(EodhdApiKey);
    public bool HasProductionConfig => !string.IsNullOrEmpty(ProductionConnectionString)
                                        && !string.IsNullOrEmpty(ProductionApiUrl);

    public string GetConnectionString(TargetEnvironment env) =>
        env == TargetEnvironment.Local ? LocalConnectionString : ProductionConnectionString ?? string.Empty;

    public string GetApiUrl(TargetEnvironment env) =>
        env == TargetEnvironment.Local ? LocalApiUrl : ProductionApiUrl ?? string.Empty;
}

public enum TargetEnvironment
{
    Local,
    Production
}
