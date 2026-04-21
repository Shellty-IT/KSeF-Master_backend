namespace KSeF.Backend.Models.Responses.Common;

public class AuthResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? SessionToken { get; set; }
    public string? ReferenceNumber { get; set; }
    public DateTime? AccessTokenValidUntil { get; set; }
    public DateTime? RefreshTokenValidUntil { get; set; }
}

public class SessionResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? SessionReferenceNumber { get; set; }
    public DateTime? ValidUntil { get; set; }
}

public class SendInvoiceResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? ElementReferenceNumber { get; set; }
    public string? ProcessingDescription { get; set; }
    public int? ProcessingCode { get; set; }
    public string? InvoiceHash { get; set; }
}

public class InvoiceDetailsResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? InvoiceHash { get; set; }
    public string? InvoiceXml { get; set; }
    public string? KsefNumber { get; set; }
    public string? InvoiceNumber { get; set; }
    public string? IssueDate { get; set; }
    public string? SellerNip { get; set; }
    public string? SellerName { get; set; }
    public string? SellerAddress { get; set; }
    public string? BuyerNip { get; set; }
    public string? BuyerName { get; set; }
    public string? BuyerAddress { get; set; }
    public decimal NetTotal { get; set; }
    public decimal VatTotal { get; set; }
    public decimal GrossTotal { get; set; }
    public List<InvoiceItemResult>? Items { get; set; }
}

public class InvoiceItemResult
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