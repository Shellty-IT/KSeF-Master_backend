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

public class AppAuthResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? Token { get; set; }
    public UserInfo? User { get; set; }
}

public class UserInfo
{
    public int Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public CompanyInfo? Company { get; set; }
}

public class CompanyInfo
{
    public int Id { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string Nip { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool HasKsefToken { get; set; }
    public string AuthMethod { get; set; } = "token";
    public string KsefEnvironment { get; set; } = "Test";
    public bool HasCertificate { get; set; }
}