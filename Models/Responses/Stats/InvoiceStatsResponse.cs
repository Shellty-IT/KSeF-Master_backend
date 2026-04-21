namespace KSeF.Backend.Models.Responses.Stats;

public class InvoiceStatsResponse
{
    public int IssuedCount { get; set; }
    public int ReceivedCount { get; set; }
    public decimal IssuedNetTotal { get; set; }
    public decimal IssuedGrossTotal { get; set; }
    public decimal ReceivedNetTotal { get; set; }
    public decimal ReceivedGrossTotal { get; set; }
    public DateTime PeriodFrom { get; set; }
    public DateTime PeriodTo { get; set; }
    public DateTime FetchedAt { get; set; } = DateTime.UtcNow;
    public List<MonthlyStats> Monthly { get; set; } = new();
    public Dictionary<string, int> TopContractors { get; set; } = new();
    public Dictionary<string, CurrencyStats> ByCurrency { get; set; } = new();
}

public class MonthlyStats
{
    public string Month { get; set; } = string.Empty;
    public int IssuedCount { get; set; }
    public int ReceivedCount { get; set; }
    public decimal IssuedGross { get; set; }
    public decimal ReceivedGross { get; set; }
}

public class CurrencyStats
{
    public int Count { get; set; }
    public decimal NetTotal { get; set; }
    public decimal GrossTotal { get; set; }
}