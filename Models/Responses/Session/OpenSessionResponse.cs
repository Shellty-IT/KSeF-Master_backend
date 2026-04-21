using System.Text.Json.Serialization;

namespace KSeF.Backend.Models.Responses.Session;

public class OpenSessionResponse
{
    [JsonPropertyName("referenceNumber")]
    public string ReferenceNumber { get; set; } = string.Empty;

    [JsonPropertyName("validUntil")]
    public DateTime ValidUntil { get; set; }
}