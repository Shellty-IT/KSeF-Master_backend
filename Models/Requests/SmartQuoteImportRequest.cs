// Models/Requests/SmartQuoteImportRequest.cs
using System.Text.Json.Serialization;

namespace KSeF.Backend.Models.Requests;

public class SmartQuoteImportRequest
{
    [JsonPropertyName("smartQuoteId")]
    public string SmartQuoteId { get; set; } = string.Empty;

    [JsonPropertyName("offerNumber")]
    public string OfferNumber { get; set; } = string.Empty;

    [JsonPropertyName("issueDate")]
    public string IssueDate { get; set; } = string.Empty;

    [JsonPropertyName("dueDate")]
    public string DueDate { get; set; } = string.Empty;

    [JsonPropertyName("seller")]
    public SmartQuoteParty Seller { get; set; } = new();

    [JsonPropertyName("buyer")]
    public SmartQuoteParty Buyer { get; set; } = new();

    [JsonPropertyName("items")]
    public List<SmartQuoteItem> Items { get; set; } = new();

    [JsonPropertyName("totalNet")]
    public decimal TotalNet { get; set; }

    [JsonPropertyName("totalVat")]
    public decimal TotalVat { get; set; }

    [JsonPropertyName("totalGross")]
    public decimal TotalGross { get; set; }

    [JsonPropertyName("currency")]
    public string Currency { get; set; } = "PLN";

    [JsonPropertyName("paymentDays")]
    public int PaymentDays { get; set; }
}

public class SmartQuoteParty
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("nip")]
    public string Nip { get; set; } = string.Empty;

    [JsonPropertyName("address")]
    public string Address { get; set; } = string.Empty;

    [JsonPropertyName("city")]
    public string City { get; set; } = string.Empty;

    [JsonPropertyName("postalCode")]
    public string PostalCode { get; set; } = string.Empty;
}

public class SmartQuoteItem
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("quantity")]
    public decimal Quantity { get; set; }

    [JsonPropertyName("unit")]
    public string Unit { get; set; } = "szt.";

    [JsonPropertyName("unitPrice")]
    public decimal UnitPrice { get; set; }

    [JsonPropertyName("vatRate")]
    public int VatRate { get; set; }

    [JsonPropertyName("discount")]
    public decimal Discount { get; set; }

    [JsonPropertyName("totalNet")]
    public decimal TotalNet { get; set; }

    [JsonPropertyName("totalVat")]
    public decimal TotalVat { get; set; }

    [JsonPropertyName("totalGross")]
    public decimal TotalGross { get; set; }
}