using System.Net.Http;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EodhdLoader.Services;

/// <summary>
/// Service for fetching index constituent data from Wikipedia.
/// </summary>
public class IndexService
{
    private readonly HttpClient _httpClient;
    private readonly ConfigurationService _config;

    private static readonly Dictionary<string, string> WikipediaUrls = new()
    {
        { "GSPC.INDX", "https://en.wikipedia.org/wiki/List_of_S%26P_500_companies" },
        { "DJI.INDX", "https://en.wikipedia.org/wiki/Dow_Jones_Industrial_Average" },
        { "RUA.INDX", "https://en.wikipedia.org/wiki/Russell_3000_Index" },
        { "RUT.INDX", "https://en.wikipedia.org/wiki/Russell_2000_Index" }
    };

    public IndexService(HttpClient httpClient, ConfigurationService config)
    {
        _httpClient = httpClient;
        _config = config;
    }

    /// <summary>
    /// Fetches index constituents from Wikipedia.
    /// </summary>
    public async Task<IndexConstituentsResponse> GetConstituentsAsync(
        string indexSymbol,
        CancellationToken ct = default)
    {
        if (!WikipediaUrls.TryGetValue(indexSymbol, out var wikipediaUrl))
        {
            throw new NotSupportedException($"Index {indexSymbol} not supported for Wikipedia scraping");
        }

        var html = await _httpClient.GetStringAsync(wikipediaUrl, ct);

        var constituents = indexSymbol switch
        {
            "GSPC.INDX" => ParseSP500Table(html),
            "DJI.INDX" => ParseDowJonesTable(html),
            "RUA.INDX" or "RUT.INDX" => ParseRussellTable(html, indexSymbol),
            _ => throw new NotSupportedException($"Parser not implemented for {indexSymbol}")
        };

        return new IndexConstituentsResponse
        {
            IndexSymbol = indexSymbol,
            IndexName = GetIndexNameFromSymbol(indexSymbol),
            Constituents = constituents
        };
    }

    private List<IndexConstituent> ParseSP500Table(string html)
    {
        var constituents = new List<IndexConstituent>();

        // S&P 500 Wikipedia table has format: Symbol | Security | GICS Sector | GICS Sub-Industry
        // Extract table rows between <table...> and </table>
        var tableMatch = Regex.Match(html, @"<table[^>]*?id=""constituents"".*?>(.*?)</table>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);

        if (!tableMatch.Success)
        {
            throw new InvalidOperationException("Could not find S&P 500 constituents table on Wikipedia");
        }

        var tableContent = tableMatch.Groups[1].Value;
        var rowMatches = Regex.Matches(tableContent, @"<tr[^>]*?>(.*?)</tr>", RegexOptions.Singleline);

        foreach (Match rowMatch in rowMatches.Skip(1)) // Skip header row
        {
            var cells = Regex.Matches(rowMatch.Groups[1].Value, @"<td[^>]*?>(.*?)</td>", RegexOptions.Singleline);

            if (cells.Count < 3) continue;

            var ticker = StripHtml(cells[0].Groups[1].Value).Trim();
            var name = StripHtml(cells[1].Groups[1].Value).Trim();
            var sector = cells.Count > 2 ? StripHtml(cells[2].Groups[1].Value).Trim() : null;
            var industry = cells.Count > 3 ? StripHtml(cells[3].Groups[1].Value).Trim() : null;

            if (!string.IsNullOrWhiteSpace(ticker) && ticker.Length <= 5)
            {
                constituents.Add(new IndexConstituent
                {
                    Ticker = ticker,
                    Name = name,
                    Exchange = "US",
                    Sector = sector,
                    Industry = industry
                });
            }
        }

        return constituents;
    }

    private List<IndexConstituent> ParseDowJonesTable(string html)
    {
        var constituents = new List<IndexConstituent>();

        // Dow Jones has a simpler table
        var tableMatch = Regex.Match(html, @"<table[^>]*?class=""[^""]*?wikitable[^""]*?"".*?>(.*?)</table>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);

        if (!tableMatch.Success) return constituents;

        var tableContent = tableMatch.Groups[1].Value;
        var rowMatches = Regex.Matches(tableContent, @"<tr[^>]*?>(.*?)</tr>", RegexOptions.Singleline);

        foreach (Match rowMatch in rowMatches.Skip(1))
        {
            var cells = Regex.Matches(rowMatch.Groups[1].Value, @"<td[^>]*?>(.*?)</td>", RegexOptions.Singleline);

            if (cells.Count < 2) continue;

            var name = StripHtml(cells[0].Groups[1].Value).Trim();
            var ticker = StripHtml(cells[1].Groups[1].Value).Trim();

            if (!string.IsNullOrWhiteSpace(ticker) && ticker.Length <= 5)
            {
                constituents.Add(new IndexConstituent
                {
                    Ticker = ticker,
                    Name = name,
                    Exchange = "US"
                });
            }
        }

        return constituents;
    }

    private List<IndexConstituent> ParseRussellTable(string html, string indexSymbol)
    {
        // For Russell indices, we'll extract ticker symbols from the page
        // This is a basic implementation - can be enhanced
        var constituents = new List<IndexConstituent>();

        var tickerMatches = Regex.Matches(html, @"\b[A-Z]{1,5}\b");
        var uniqueTickers = tickerMatches.Select(m => m.Value).Distinct().ToList();

        foreach (var ticker in uniqueTickers.Where(t => t.Length >= 1 && t.Length <= 5))
        {
            constituents.Add(new IndexConstituent
            {
                Ticker = ticker,
                Name = string.Empty,
                Exchange = "US"
            });
        }

        return constituents;
    }

    private string StripHtml(string html)
    {
        // Remove HTML tags and decode entities
        var stripped = Regex.Replace(html, @"<[^>]+>", string.Empty);
        stripped = stripped.Replace("&amp;", "&")
                           .Replace("&lt;", "<")
                           .Replace("&gt;", ">")
                           .Replace("&quot;", "\"")
                           .Replace("&#39;", "'");
        return stripped.Trim();
    }

    private string GetIndexNameFromSymbol(string symbol)
    {
        return GetMajorIndices().FirstOrDefault(i => i.Symbol == symbol)?.Name ?? symbol;
    }

    public List<IndexDefinition> GetMajorIndices()
    {
        return
        [
            new IndexDefinition { Symbol = "GSPC.INDX", Name = "S&P 500", Region = "US" },
            new IndexDefinition { Symbol = "DJI.INDX", Name = "Dow Jones Industrial Average", Region = "US" },
            new IndexDefinition { Symbol = "RUA.INDX", Name = "Russell 3000", Region = "US" },
            new IndexDefinition { Symbol = "RUT.INDX", Name = "Russell 2000", Region = "US" }
        ];
    }
}

public class IndexDefinition
{
    public string Symbol { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
}

public class IndexConstituentsResponse
{
    public string IndexSymbol { get; set; } = string.Empty;
    public string? IndexName { get; set; }
    public List<IndexConstituent> Constituents { get; set; } = [];
}

public class IndexConstituent
{
    public string Ticker { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Exchange { get; set; } = string.Empty;
    public string? Sector { get; set; }
    public string? Industry { get; set; }
}

// EODHD API response models (kept for future use)
internal class FundamentalsResponse
{
    [JsonPropertyName("General")]
    public GeneralInfo? General { get; set; }

    [JsonPropertyName("Components")]
    public Dictionary<string, ComponentInfo>? Components { get; set; }
}

internal class GeneralInfo
{
    [JsonPropertyName("Name")]
    public string? Name { get; set; }
}

internal class ComponentInfo
{
    [JsonPropertyName("Code")]
    public string? Code { get; set; }

    [JsonPropertyName("Exchange")]
    public string? Exchange { get; set; }

    [JsonPropertyName("Name")]
    public string? Name { get; set; }

    [JsonPropertyName("Sector")]
    public string? Sector { get; set; }

    [JsonPropertyName("Industry")]
    public string? Industry { get; set; }
}
