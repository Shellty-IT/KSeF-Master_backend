using System.Text.Json.Serialization;

namespace KSeF.Backend.Models.Requests;

public class CreateInvoiceRequest
{
    [JsonPropertyName("invoiceNumber")]
    public string InvoiceNumber { get; set; } = string.Empty;

    [JsonPropertyName("issueDate")]
    public string IssueDate { get; set; } = string.Empty;

    [JsonPropertyName("saleDate")]
    public string SaleDate { get; set; } = string.Empty;

    [JsonPropertyName("seller")]
    public PartyData Seller { get; set; } = new();

    [JsonPropertyName("buyer")]
    public PartyData Buyer { get; set; } = new();

    [JsonPropertyName("items")]
    public List<InvoiceItem> Items { get; set; } = new();

    [JsonPropertyName("currency")]
    public string Currency { get; set; } = "PLN";

    [JsonPropertyName("issuePlace")]
    public string? IssuePlace { get; set; }

    [JsonPropertyName("payment")]
    public PaymentData? Payment { get; set; }

    [JsonPropertyName("correctionData")]
    public CorrectionData? CorrectionData { get; set; }
}

public class PartyData
{
    [JsonPropertyName("nip")]
    public string Nip { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("countryCode")]
    public string CountryCode { get; set; } = "PL";

    [JsonPropertyName("addressLine1")]
    public string AddressLine1 { get; set; } = string.Empty;

    [JsonPropertyName("addressLine2")]
    public string? AddressLine2 { get; set; }
}

public class InvoiceItem
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("unit")]
    public string Unit { get; set; } = "szt.";

    [JsonPropertyName("quantity")]
    public decimal Quantity { get; set; }

    [JsonPropertyName("unitPriceNet")]
    public decimal UnitPriceNet { get; set; }

    [JsonPropertyName("vatRate")]
    public string VatRate { get; set; } = "23";
}

public class PaymentData
{
    [JsonPropertyName("method")]
    public string Method { get; set; } = "przelew";

    [JsonPropertyName("dueDate")]
    public string? DueDate { get; set; }

    [JsonPropertyName("bankAccount")]
    public string? BankAccount { get; set; }

    [JsonPropertyName("paid")]
    public bool? Paid { get; set; }

    [JsonPropertyName("paidDate")]
    public string? PaidDate { get; set; }
}

public class CorrectionData
{
    [JsonPropertyName("originalInvoiceNumber")]
    public string OriginalInvoiceNumber { get; set; } = string.Empty;

    [JsonPropertyName("originalIssueDate")]
    public string OriginalIssueDate { get; set; } = string.Empty;

    [JsonPropertyName("originalKsefNumber")]
    public string? OriginalKsefNumber { get; set; }

    [JsonPropertyName("reason")]
    public string? Reason { get; set; }

    [JsonPropertyName("correctionType")]
    public int? CorrectionType { get; set; }
}