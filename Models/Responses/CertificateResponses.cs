// Models/Responses/CertificateResponses.cs
namespace KSeF.Backend.Models.Responses;

public class UserCertificateInfo
{
    public bool HasCertificate { get; set; }
    public bool HasPrivateKey { get; set; }
    public bool IsPasswordProtected { get; set; }
    public DateTime? UploadedAt { get; set; }
    public string? SubjectName { get; set; }
    public DateTime? NotBefore { get; set; }
    public DateTime? NotAfter { get; set; }
}

public class CertAuthChallengeResponse
{
    public string Challenge { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}

public class CertAuthResult
{
    public bool Success { get; set; }
    public string? SessionToken { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? Error { get; set; }
    public DateTime? AccessTokenValidUntil { get; set; }
    public DateTime? RefreshTokenValidUntil { get; set; }
}