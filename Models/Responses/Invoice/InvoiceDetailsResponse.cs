using System.Text.Json.Serialization;

namespace KSeF.Backend.Models.Responses.Invoice;

public class InvoiceDetailsResponse
{
    [JsonPropertyName("timestamp")]
    public DateTime? Timestamp { get; set; }

    [JsonPropertyName("invoiceHash")]
    public InvoiceHashInfo? InvoiceHash { get; set; }

    [JsonPropertyName("invoicePayload")]
    public InvoicePayloadInfo? InvoicePayload { get; set; }

    [JsonPropertyName("ksefReferenceNumber")]
    public string? KsefReferenceNumber { get; set; }
}

public class InvoiceHashInfo
{
    [JsonPropertyName("hashSHA")]
    public HashShaInfo? HashSHA { get; set; }

    [JsonPropertyName("fileSize")]
    public int? FileSize { get; set; }
}

public class HashShaInfo
{
    [JsonPropertyName("algorithm")]
    public string? Algorithm { get; set; }

    [JsonPropertyName("encoding")]
    public string? Encoding { get; set; }

    [JsonPropertyName("value")]
    public string? Value { get; set; }
}

public class InvoicePayloadInfo
{
    [JsonPropertyName("payloadType")]
    public string? PayloadType { get; set; }

    [JsonPropertyName("invoiceBody")]
    public string? InvoiceBody { get; set; }
}