using System.Text.Json.Serialization;

namespace KSeF.Backend.Models.Requests;

public class InvoiceQueryRequest
{
    [JsonPropertyName("subjectType")]
    public string SubjectType { get; set; } = "Subject1";

    [JsonPropertyName("dateRange")]
    public DateRangeFilter DateRange { get; set; } = new();
}

public class DateRangeFilter
{
    [JsonPropertyName("dateType")]
    public string DateType { get; set; } = "PermanentStorage";

    [JsonPropertyName("from")]
    public DateTime From { get; set; } = DateTime.UtcNow.AddDays(-30);

    [JsonPropertyName("to")]
    public DateTime To { get; set; } = DateTime.UtcNow;
}