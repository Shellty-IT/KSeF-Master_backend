using System.Text.Json.Serialization;

namespace KSeF.Backend.Models.Responses.Invoice;

public class InvoiceMetadata
{
    [JsonPropertyName("ksefNumber")]
    public string KsefNumber { get; set; } = string.Empty;

    [JsonPropertyName("invoiceNumber")]
    public string? InvoiceNumber { get; set; }

    [JsonPropertyName("issueDate")]
    public string? IssueDate { get; set; }

    [JsonPropertyName("invoicingDate")]
    public DateTime? InvoicingDate { get; set; }

    [JsonPropertyName("acquisitionDate")]
    public DateTime? AcquisitionDate { get; set; }

    [JsonPropertyName("permanentStorageDate")]
    public DateTime? PermanentStorageDate { get; set; }

    [JsonPropertyName("seller")]
    public PartyInfo? Seller { get; set; }

    [JsonPropertyName("buyer")]
    public BuyerInfo? Buyer { get; set; }

    [JsonPropertyName("netAmount")]
    public decimal? NetAmount { get; set; }

    [JsonPropertyName("grossAmount")]
    public decimal? GrossAmount { get; set; }

    [JsonPropertyName("vatAmount")]
    public decimal? VatAmount { get; set; }

    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    [JsonPropertyName("invoicingMode")]
    public string? InvoicingMode { get; set; }

    [JsonPropertyName("invoiceType")]
    public string? InvoiceType { get; set; }

    [JsonPropertyName("formCode")]
    public FormCodeInfo? FormCode { get; set; }

    [JsonPropertyName("isSelfInvoicing")]
    public bool IsSelfInvoicing { get; set; }

    [JsonPropertyName("hasAttachment")]
    public bool HasAttachment { get; set; }

    [JsonPropertyName("invoiceHash")]
    public string? InvoiceHash { get; set; }
}

public class PartyInfo
{
    [JsonPropertyName("nip")]
    public string? Nip { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

public class BuyerInfo
{
    [JsonPropertyName("identifier")]
    public IdentifierInfo? Identifier { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

public class IdentifierInfo
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;
}

public class FormCodeInfo
{
    [JsonPropertyName("systemCode")]
    public string SystemCode { get; set; } = string.Empty;

    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; set; } = string.Empty;

    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;
}