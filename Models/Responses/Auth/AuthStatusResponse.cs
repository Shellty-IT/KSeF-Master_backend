using System.Text.Json.Serialization;
using KSeF.Backend.Models.Responses.Common;

namespace KSeF.Backend.Models.Responses.Auth;

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

    [JsonPropertyName("accessToken")]
    public TokenInfo? AccessToken { get; set; }

    [JsonPropertyName("refreshToken")]
    public TokenInfo? RefreshToken { get; set; }
}