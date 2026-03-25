// Models/Requests/RejectDraftRequest.cs
using System.Text.Json.Serialization;

namespace KSeF.Backend.Models.Requests;

public class RejectDraftRequest
{
    [JsonPropertyName("reason")]
    public string Reason { get; set; } = string.Empty;
}