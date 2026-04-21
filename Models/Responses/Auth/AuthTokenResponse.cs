using System.Text.Json.Serialization;
using KSeF.Backend.Models.Responses.Common;

namespace KSeF.Backend.Models.Responses.Auth;

public class AuthTokenResponse
{
    [JsonPropertyName("referenceNumber")]
    public string ReferenceNumber { get; set; } = string.Empty;

    [JsonPropertyName("authenticationToken")]
    public TokenInfo? AuthenticationToken { get; set; }

    [JsonPropertyName("processingCode")]
    public int? ProcessingCode { get; set; }

    [JsonPropertyName("processingDescription")]
    public string? ProcessingDescription { get; set; }
}