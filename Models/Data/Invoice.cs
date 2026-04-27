// BACKEND: Models/Data/Invoice.cs
namespace KSeF.Backend.Models.Data;

public class Invoice
{
    public int Id { get; set; }
    public int CompanyProfileId { get; set; }

    public string KsefReferenceNumber { get; set; } = string.Empty;
    public string Nip { get; set; } = string.Empty;

    public string InvoiceType { get; set; } = string.Empty;
    public string Direction { get; set; } = string.Empty;

    public string? InvoiceNumber { get; set; }
    public string? SellerNip { get; set; }
    public string? SellerName { get; set; }
    public string? BuyerNip { get; set; }
    public string? BuyerName { get; set; }

    public decimal? NetAmount { get; set; }
    public decimal? VatAmount { get; set; }
    public decimal? GrossAmount { get; set; }
    public string? Currency { get; set; }

    public DateTime? InvoiceDate { get; set; }
    public DateTime AcquisitionTimestamp { get; set; }
    public DateTime SyncedAt { get; set; } = DateTime.UtcNow;

    public string? XmlContent { get; set; }
    public string KsefEnvironment { get; set; } = "Test";

    public string? InvoiceHash { get; set; }

    public CompanyProfile CompanyProfile { get; set; } = null!;
}