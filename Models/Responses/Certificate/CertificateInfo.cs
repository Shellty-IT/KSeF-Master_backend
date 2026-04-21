using System.Text.Json.Serialization;

namespace KSeF.Backend.Models.Responses.Certificate;

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