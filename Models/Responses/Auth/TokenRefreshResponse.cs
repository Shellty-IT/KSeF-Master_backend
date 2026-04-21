using System.Text.Json.Serialization;
using KSeF.Backend.Models.Responses.Common;

namespace KSeF.Backend.Models.Responses.Auth;

public class TokenRefreshResponse
{
    [JsonPropertyName("accessToken")]
    public TokenInfo? AccessToken { get; set; }
}