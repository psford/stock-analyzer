namespace EodhdLoader.Models;

/// <summary>
/// Configuration for an iShares ETF, loaded from ishares_etf_configs.json.
/// </summary>
public class EtfConfig
{
    /// <summary>
    /// iShares product ID used to construct the download URL.
    /// </summary>
    public int ProductId { get; set; }

    /// <summary>
    /// iShares product slug used to construct the download URL.
    /// </summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>
    /// Index code for matching against IndexDefinition table.
    /// </summary>
    public string IndexCode { get; set; } = string.Empty;
}
