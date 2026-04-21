using System.Text.Json.Serialization;

namespace KSeF.Backend.Models.Responses.Invoice;

public class InvoiceQueryResponse
{
    [JsonPropertyName("hasMore")]
    public bool HasMore { get; set; }

    [JsonPropertyName("isTruncated")]
    public bool IsTruncated { get; set; }

    [JsonPropertyName("permanentStorageHwmDate")]
    public DateTime? PermanentStorageHwmDate { get; set; }

    [JsonPropertyName("invoices")]
    public List<InvoiceMetadata> Invoices { get; set; } = new();

    [JsonPropertyName("totalCount")]
    public int TotalCount { get; set; }

    [JsonPropertyName("fetchedAt")]
    public DateTime FetchedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("pagesProcessed")]
    public int PagesProcessed { get; set; }
}