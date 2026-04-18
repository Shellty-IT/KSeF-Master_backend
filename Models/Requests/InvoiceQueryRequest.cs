// Models/Requests/InvoiceQueryRequest.cs
using System.Text.Json.Serialization;

namespace KSeF.Backend.Models.Requests;

public class InvoiceQueryRequest
{
    [JsonPropertyName("subjectType")]
    public string SubjectType { get; set; } = "Subject1";

    [JsonPropertyName("dateRange")]
    public DateRangeFilter DateRange { get; set; } = new();

    [JsonPropertyName("amountFrom")]
    public decimal? AmountFrom { get; set; }

    [JsonPropertyName("amountTo")]
    public decimal? AmountTo { get; set; }

    [JsonPropertyName("contractorNip")]
    public string? ContractorNip { get; set; }

    [JsonPropertyName("contractorName")]
    public string? ContractorName { get; set; }

    [JsonPropertyName("invoiceNumber")]
    public string? InvoiceNumber { get; set; }

    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    [JsonPropertyName("maxResults")]
    public int? MaxResults { get; set; }

    [JsonPropertyName("sortDescending")]
    public bool SortDescending { get; set; } = true;
}

public class DateRangeFilter
{
    [JsonPropertyName("dateType")]
    public string DateType { get; set; } = "PermanentStorage";

    [JsonPropertyName("from")]
    public DateTime From { get; set; } = DateTime.UtcNow.AddMonths(-3);

    [JsonPropertyName("to")]
    public DateTime To { get; set; } = DateTime.UtcNow;
}