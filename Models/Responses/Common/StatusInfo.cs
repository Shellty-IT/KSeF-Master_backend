using System.Text.Json.Serialization;

namespace KSeF.Backend.Models.Responses.Common;

public class StatusInfo
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;
}