// Models/ExternalDraft.cs
namespace KSeF.Backend.Models;

public enum ExternalDraftStatus
{
    PENDING,
    APPROVED,
    REJECTED
}

public class ExternalDraft
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string SmartQuoteId { get; set; } = string.Empty;
    public string OfferNumber { get; set; } = string.Empty;
    public ExternalDraftStatus Status { get; set; } = ExternalDraftStatus.PENDING;
    public string IssueDate { get; set; } = string.Empty;
    public string DueDate { get; set; } = string.Empty;

    public string SellerName { get; set; } = string.Empty;
    public string SellerNip { get; set; } = string.Empty;
    public string SellerAddress { get; set; } = string.Empty;
    public string SellerCity { get; set; } = string.Empty;
    public string SellerPostalCode { get; set; } = string.Empty;

    public string BuyerName { get; set; } = string.Empty;
    public string BuyerNip { get; set; } = string.Empty;
    public string BuyerAddress { get; set; } = string.Empty;
    public string BuyerCity { get; set; } = string.Empty;
    public string BuyerPostalCode { get; set; } = string.Empty;

    public List<ExternalDraftItem> Items { get; set; } = new();

    public decimal TotalNet { get; set; }
    public decimal TotalVat { get; set; }
    public decimal TotalGross { get; set; }
    public string Currency { get; set; } = "PLN";
    public int PaymentDays { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessedAt { get; set; }
    public string? ProcessedBy { get; set; }
    public string? RejectionReason { get; set; }
}

public class ExternalDraftItem
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Quantity { get; set; }
    public string Unit { get; set; } = "szt.";
    public decimal UnitPrice { get; set; }
    public int VatRate { get; set; }
    public decimal Discount { get; set; }
    public decimal TotalNet { get; set; }
    public decimal TotalVat { get; set; }
    public decimal TotalGross { get; set; }
}