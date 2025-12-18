namespace KSeF.Backend.Models.Requests;

public class GeneratePdfRequest
{
    /// <summary>
    /// Źródło danych: "local" (wysłane z aplikacji) lub "ksef" (pobrane z KSeF)
    /// </summary>
    public string Source { get; set; } = "local";

    /// <summary>
    /// Numer KSeF faktury (wymagany dla source="ksef", opcjonalny dla "local")
    /// </summary>
    public string? KsefNumber { get; set; }

    /// <summary>
    /// Hash faktury (wymagany dla source="local")
    /// </summary>
    public string? InvoiceHash { get; set; }

    // === Dane faktury (wymagane dla source="local") ===

    public string? InvoiceNumber { get; set; }
    public string? IssueDate { get; set; }
    public string? SaleDate { get; set; }
    public string? IssuePlace { get; set; }

    public PdfPartyData? Seller { get; set; }
    public PdfPartyData? Buyer { get; set; }

    public List<PdfInvoiceItem>? Items { get; set; }

    public PdfTotals? Totals { get; set; }

    public PdfPaymentData? Payment { get; set; }
}

public class PdfPartyData
{
    public string Nip { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string? BankAccount { get; set; }
}

public class PdfInvoiceItem
{
    public string Name { get; set; } = string.Empty;
    public string Unit { get; set; } = "szt.";
    public decimal Quantity { get; set; }
    public decimal UnitPriceNet { get; set; }
    public string VatRate { get; set; } = "23";
    public decimal NetValue { get; set; }
    public decimal VatValue { get; set; }
    public decimal GrossValue { get; set; }
}

public class PdfTotals
{
    public decimal Net { get; set; }
    public decimal Vat { get; set; }
    public decimal Gross { get; set; }
    public Dictionary<string, PdfVatRateSummary>? PerRate { get; set; }
}

public class PdfVatRateSummary
{
    public decimal Net { get; set; }
    public decimal Vat { get; set; }
    public decimal Gross { get; set; }
}

public class PdfPaymentData
{
    public string Method { get; set; } = "przelew";
    public string? DueDate { get; set; }
    public string? BankAccount { get; set; }
}