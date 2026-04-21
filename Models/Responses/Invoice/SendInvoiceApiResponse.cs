using System.Text.Json.Serialization;

namespace KSeF.Backend.Models.Responses.Invoice;

public class SendInvoiceApiResponse
{
    [JsonPropertyName("elementReferenceNumber")]
    public string? ElementReferenceNumber { get; set; }

    [JsonPropertyName("processingCode")]
    public int? ProcessingCode { get; set; }

    [JsonPropertyName("processingDescription")]
    public string? ProcessingDescription { get; set; }

    [JsonPropertyName("referenceNumber")]
    public string? ReferenceNumber { get; set; }

    [JsonPropertyName("timestamp")]
    public DateTime? Timestamp { get; set; }
}