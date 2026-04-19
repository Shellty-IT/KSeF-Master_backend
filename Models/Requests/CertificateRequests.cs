// Models/Requests/CertificateRequests.cs
namespace KSeF.Backend.Models.Requests;

public class UploadCertificateRequest
{
    public string CertificateBase64 { get; set; } = string.Empty;
    public string PrivateKeyBase64 { get; set; } = string.Empty;
    public string? Password { get; set; }
}

public class SwitchAuthMethodRequest
{
    public string AuthMethod { get; set; } = string.Empty;
}