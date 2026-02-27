namespace StockAnalyzer.Core.Data.Entities;

public class MicExchangeEntity
{
    public string MicCode { get; set; } = string.Empty;
    public string ExchangeName { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    public ICollection<SecurityMasterEntity> Securities { get; set; } = new List<SecurityMasterEntity>();
}
