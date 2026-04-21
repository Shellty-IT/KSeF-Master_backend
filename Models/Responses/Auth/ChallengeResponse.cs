using System.Text.Json.Serialization;

namespace KSeF.Backend.Models.Responses.Auth;

public class ChallengeResponse
{
    [JsonPropertyName("challenge")]
    public string Challenge { get; set; } = string.Empty;

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }
}