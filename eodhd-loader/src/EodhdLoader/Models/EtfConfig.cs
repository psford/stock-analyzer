namespace EodhdLoader.Models;

using System.Text.Json.Serialization;

/// <summary>
/// Configuration for an iShares ETF, loaded from ishares_etf_configs.json.
/// JSON uses snake_case, properties use PascalCase with JsonPropertyName attributes for mapping.
/// </summary>
public class EtfConfig
{
    /// <summary>
    /// iShares product ID used to construct the download URL.
    /// </summary>
    [JsonPropertyName("product_id")]
    public int ProductId { get; set; }

    /// <summary>
    /// iShares product slug used to construct the download URL.
    /// </summary>
    [JsonPropertyName("slug")]
    public string Slug { get; set; } = string.Empty;

    /// <summary>
    /// Index code for matching against IndexDefinition table.
    /// </summary>
    [JsonPropertyName("index_code")]
    public string IndexCode { get; set; } = string.Empty;
}
