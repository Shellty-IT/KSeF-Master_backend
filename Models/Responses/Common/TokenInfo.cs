using System.Text.Json.Serialization;

namespace KSeF.Backend.Models.Responses.Common;

public class TokenInfo
{
    [JsonPropertyName("token")]
    public string Token { get; set; } = string.Empty;

    [JsonPropertyName("validUntil")]
    public DateTime ValidUntil { get; set; }
}