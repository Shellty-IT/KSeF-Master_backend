using System.Text.Json.Serialization;

namespace KSeF.Backend.Models.Responses;

// ============ Certyfikaty ============
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

// ============ Challenge ============
public class ChallengeResponse
{
    [JsonPropertyName("challenge")]
    public string Challenge { get; set; } = string.Empty;

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }
}

// ============ Auth Token ============
public class AuthTokenResponse
{
    [JsonPropertyName("referenceNumber")]
    public string ReferenceNumber { get; set; } = string.Empty;

    [JsonPropertyName("authenticationToken")]
    public TokenInfo? AuthenticationToken { get; set; }
}

public class TokenInfo
{
    [JsonPropertyName("token")]
    public string Token { get; set; } = string.Empty;

    [JsonPropertyName("validUntil")]
    public DateTime ValidUntil { get; set; }
}

// ============ Auth Status ============
public class AuthStatusResponse
{
    [JsonPropertyName("startDate")]
    public DateTime StartDate { get; set; }

    [JsonPropertyName("authenticationMethod")]
    public string AuthenticationMethod { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public StatusInfo Status { get; set; } = new();

    [JsonPropertyName("isTokenRedeemed")]
    public bool IsTokenRedeemed { get; set; }
}

public class StatusInfo
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;
}

// ============ Token Redeem ============
public class TokenRedeemResponse
{
    [JsonPropertyName("accessToken")]
    public TokenInfo? AccessToken { get; set; }

    [JsonPropertyName("refreshToken")]
    public TokenInfo? RefreshToken { get; set; }
}

// ============ Token Refresh ============
public class TokenRefreshResponse
{
    [JsonPropertyName("accessToken")]
    public TokenInfo? AccessToken { get; set; }
}

// ============ Session Online ============
public class OpenSessionResponse
{
    [JsonPropertyName("referenceNumber")]
    public string ReferenceNumber { get; set; } = string.Empty;

    [JsonPropertyName("validUntil")]
    public DateTime ValidUntil { get; set; }
}

// ============ Invoice Query ============
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

// ============ Send Invoice Response ============
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

// ============ Backend Result Wrappers ============
public class AuthResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
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
}