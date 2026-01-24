using System.Text.Json.Serialization;

namespace KSeF.Backend.Models.Requests;

/// <summary>
/// Dane faktury z frontendu - backend wygeneruje XML
/// </summary>
public class CreateInvoiceRequest
{
    /// <summary>
    /// Numer faktury (np. "FV/2025/001")
    /// </summary>
    [JsonPropertyName("invoiceNumber")]
    public string InvoiceNumber { get; set; } = string.Empty;

    /// <summary>
    /// Data wystawienia (YYYY-MM-DD)
    /// </summary>
    [JsonPropertyName("issueDate")]
    public string IssueDate { get; set; } = string.Empty;

    /// <summary>
    /// Data sprzedaży/dostawy (YYYY-MM-DD)
    /// </summary>
    [JsonPropertyName("saleDate")]
    public string SaleDate { get; set; } = string.Empty;

    /// <summary>
    /// Dane sprzedawcy
    /// </summary>
    [JsonPropertyName("seller")]
    public PartyData Seller { get; set; } = new();

    /// <summary>
    /// Dane nabywcy
    /// </summary>
    [JsonPropertyName("buyer")]
    public PartyData Buyer { get; set; } = new();

    /// <summary>
    /// Pozycje faktury
    /// </summary>
    [JsonPropertyName("items")]
    public List<InvoiceItem> Items { get; set; } = new();

    /// <summary>
    /// Waluta (domyślnie PLN)
    /// </summary>
    [JsonPropertyName("currency")]
    public string Currency { get; set; } = "PLN";

    /// <summary>
    /// Miejsce wystawienia
    /// </summary>
    [JsonPropertyName("issuePlace")]
    public string? IssuePlace { get; set; }

    /// <summary>
    /// Dane płatności
    /// </summary>
    [JsonPropertyName("payment")]
    public PaymentData? Payment { get; set; }
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
    /// <summary>
    /// Nazwa towaru/usługi
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Jednostka miary (szt., kg, godz., usł. itp.)
    /// </summary>
    [JsonPropertyName("unit")]
    public string Unit { get; set; } = "szt.";

    /// <summary>
    /// Ilość
    /// </summary>
    [JsonPropertyName("quantity")]
    public decimal Quantity { get; set; }

    /// <summary>
    /// Cena jednostkowa netto
    /// </summary>
    [JsonPropertyName("unitPriceNet")]
    public decimal UnitPriceNet { get; set; }

    /// <summary>
    /// Stawka VAT (23, 8, 5, 0, "zw", "np")
    /// </summary>
    [JsonPropertyName("vatRate")]
    public string VatRate { get; set; } = "23";
}

/// <summary>
/// Dane płatności
/// </summary>
public class PaymentData
{
    /// <summary>
    /// Metoda płatności: "przelew", "gotówka"
    /// </summary>
    [JsonPropertyName("method")]
    public string Method { get; set; } = "przelew";

    /// <summary>
    /// Termin płatności (YYYY-MM-DD)
    /// </summary>
    [JsonPropertyName("dueDate")]
    public string? DueDate { get; set; }

    /// <summary>
    /// Numer rachunku bankowego (26 cyfr)
    /// </summary>
    [JsonPropertyName("bankAccount")]
    public string? BankAccount { get; set; }
}