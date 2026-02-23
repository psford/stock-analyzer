namespace EodhdLoader.Services;

using EodhdLoader.Models;

/// <summary>
/// Service for downloading and ingesting iShares ETF constituent data.
/// Manages the full pipeline: download JSON from iShares, parse holdings,
/// match to SecurityMaster, upsert identifiers with SCD Type 2 history,
/// and insert IndexConstituent records idempotently.
/// </summary>
public interface IISharesConstituentService
{
    /// <summary>
    /// Downloads and parses holdings for a single ETF, persists to database.
    /// </summary>
    /// <param name="etfTicker">ETF ticker symbol (e.g., "IVV")</param>
    /// <param name="asOfDate">Optional effective date; if null, uses current date. Weekend dates are adjusted to last business day.</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Statistics for the ingestion operation</returns>
    Task<IngestStats> IngestEtfAsync(string etfTicker, DateTime? asOfDate = null, CancellationToken ct = default);

    /// <summary>
    /// Loads all configured ETFs with rate limiting (2s minimum between requests).
    /// </summary>
    /// <param name="asOfDate">Optional effective date for all ETFs</param>
    /// <param name="ct">Cancellation token</param>
    Task IngestAllEtfsAsync(DateTime? asOfDate = null, CancellationToken ct = default);

    /// <summary>
    /// Returns ETFs with stale constituent data (missing latest month-end).
    /// Used by UI to identify which ETFs need a refresh.
    /// </summary>
    /// <param name="ct">Cancellation token</param>
    /// <returns>List of (EtfTicker, IndexCode) pairs for stale ETFs</returns>
    Task<IReadOnlyList<(string EtfTicker, string IndexCode)>> GetStaleEtfsAsync(CancellationToken ct = default);

    /// <summary>
    /// All configured ETF tickers and their metadata from ishares_etf_configs.json.
    /// </summary>
    IReadOnlyDictionary<string, EtfConfig> EtfConfigs { get; }

    /// <summary>
    /// Raised for each log-worthy event during ingestion.
    /// </summary>
    event Action<string>? LogMessage;

    /// <summary>
    /// Raised for progress tracking during bulk operations.
    /// </summary>
    event Action<IngestProgress>? ProgressUpdated;
}
