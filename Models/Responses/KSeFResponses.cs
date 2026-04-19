// Models/Responses/KSeFResponses.cs
using System.Text.Json.Serialization;

namespace KSeF.Backend.Models.Responses;

// ═══════════════════════════════════════════════════════
// CERTYFIKATY — GET /security/public-key-certificates
// ═══════════════════════════════════════════════════════

public class CertificateInfo
{
    [JsonPropertyName("certificate")]
    public string Certificate { get; set; } = string.Empty;

    [JsonPropertyName("validFrom")]
    public DateTime ValidFrom { get; set; }

    [JsonPropertyName("validTo")]
    public DateTime ValidTo { get; set; }

    [JsonPropertyName("usage")]
    public List<string>? Usage { get; set; }
}

// ═══════════════════════════════════════════════════════
// CHALLENGE — POST /auth/challenge
// ═══════════════════════════════════════════════════════

public class ChallengeResponse
{
    [JsonPropertyName("challenge")]
    public string Challenge { get; set; } = string.Empty;

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }
}

// ═══════════════════════════════════════════════════════
// AUTH TOKEN — POST /auth/ksef-token
// ═══════════════════════════════════════════════════════

public class AuthTokenResponse
{
    [JsonPropertyName("referenceNumber")]
    public string ReferenceNumber { get; set; } = string.Empty;

    [JsonPropertyName("authenticationToken")]
    public TokenInfo? AuthenticationToken { get; set; }

    // Nowe API v2 może zwracać status bezpośrednio
    [JsonPropertyName("processingCode")]
    public int? ProcessingCode { get; set; }

    [JsonPropertyName("processingDescription")]
    public string? ProcessingDescription { get; set; }
}

public class TokenInfo
{
    [JsonPropertyName("token")]
    public string Token { get; set; } = string.Empty;

    [JsonPropertyName("validUntil")]
    public DateTime ValidUntil { get; set; }
}

// ═══════════════════════════════════════════════════════
// AUTH STATUS — GET /auth/{referenceNumber}
// ═══════════════════════════════════════════════════════

public class AuthStatusResponse
{
    [JsonPropertyName("startDate")]
    public DateTime StartDate { get; set; }

    [JsonPropertyName("authenticationMethod")]
    public string AuthenticationMethod { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public StatusInfo? Status { get; set; }

    [JsonPropertyName("isTokenRedeemed")]
    public bool IsTokenRedeemed { get; set; }

    // v2: może zawierać token bezpośrednio po autoryzacji
    [JsonPropertyName("accessToken")]
    public TokenInfo? AccessToken { get; set; }

    [JsonPropertyName("refreshToken")]
    public TokenInfo? RefreshToken { get; set; }
}

public class StatusInfo
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;
}

// ═══════════════════════════════════════════════════════
// TOKEN REDEEM — POST /auth/token/redeem
// ═══════════════════════════════════════════════════════

public class TokenRedeemResponse
{
    [JsonPropertyName("accessToken")]
    public TokenInfo? AccessToken { get; set; }

    [JsonPropertyName("refreshToken")]
    public TokenInfo? RefreshToken { get; set; }
}

// ═══════════════════════════════════════════════════════
// TOKEN REFRESH — POST /auth/token/refresh
// ═══════════════════════════════════════════════════════

public class TokenRefreshResponse
{
    [JsonPropertyName("accessToken")]
    public TokenInfo? AccessToken { get; set; }
}

// ═══════════════════════════════════════════════════════
// SESSION — POST /invoices/sending/interactive
// ═══════════════════════════════════════════════════════

public class OpenSessionResponse
{
    [JsonPropertyName("referenceNumber")]
    public string ReferenceNumber { get; set; } = string.Empty;

    [JsonPropertyName("validUntil")]
    public DateTime ValidUntil { get; set; }
}

// ═══════════════════════════════════════════════════════
// INVOICE QUERY — POST /invoices/query
// ═══════════════════════════════════════════════════════

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

// ═══════════════════════════════════════════════════════
// SEND INVOICE
// ═══════════════════════════════════════════════════════

public class SendInvoiceApiResponse
{
    [JsonPropertyName("elementReferenceNumber")]
    public string? ElementReferenceNumber { get; set; }

    [JsonPropertyName("processingCode")]
    public int? ProcessingCode { get; set; }

    [JsonPropertyName("processingDescription")]
    public string? ProcessingDescription { get; set; }

    [JsonPropertyName("referenceNumber")]
    public string? ReferenceNumber { get; set; }

    [JsonPropertyName("timestamp")]
    public DateTime? Timestamp { get; set; }
}

// ═══════════════════════════════════════════════════════
// INVOICE DETAILS
// ═══════════════════════════════════════════════════════

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

// ═══════════════════════════════════════════════════════
// RESULT TYPES (wewnętrzne — nie mylić z KSeF API)
// ═══════════════════════════════════════════════════════

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

// ═══════════════════════════════════════════════════════
// STATYSTYKI
// ═══════════════════════════════════════════════════════

public class InvoiceStatsResponse
{
    public int IssuedCount { get; set; }
    public int ReceivedCount { get; set; }
    public decimal IssuedNetTotal { get; set; }
    public decimal IssuedGrossTotal { get; set; }
    public decimal ReceivedNetTotal { get; set; }
    public decimal ReceivedGrossTotal { get; set; }
    public DateTime PeriodFrom { get; set; }
    public DateTime PeriodTo { get; set; }
    public DateTime FetchedAt { get; set; } = DateTime.UtcNow;
    public List<MonthlyStats> Monthly { get; set; } = new();
    public Dictionary<string, int> TopContractors { get; set; } = new();
    public Dictionary<string, CurrencyStats> ByCurrency { get; set; } = new();
}

public class MonthlyStats
{
    public string Month { get; set; } = string.Empty;
    public int IssuedCount { get; set; }
    public int ReceivedCount { get; set; }
    public decimal IssuedGross { get; set; }
    public decimal ReceivedGross { get; set; }
}

public class CurrencyStats
{
    public int Count { get; set; }
    public decimal NetTotal { get; set; }
    public decimal GrossTotal { get; set; }
}