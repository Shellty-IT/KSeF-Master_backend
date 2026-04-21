using System.Text.Json.Serialization;
using KSeF.Backend.Models.Responses.Common;

namespace KSeF.Backend.Models.Responses.Auth;

public class TokenRedeemResponse
{
    [JsonPropertyName("accessToken")]
    public TokenInfo? AccessToken { get; set; }

    [JsonPropertyName("refreshToken")]
    public TokenInfo? RefreshToken { get; set; }
}