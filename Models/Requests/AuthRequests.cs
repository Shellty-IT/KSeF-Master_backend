// Models/Requests/AuthRequests.cs
namespace KSeF.Backend.Models.Requests;

public class RegisterRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public class LoginAppRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class CompanySetupRequest
{
    public string CompanyName { get; set; } = string.Empty;
    public string Nip { get; set; } = string.Empty;
    public string KsefToken { get; set; } = string.Empty;
}

public class UpdateCompanyProfileRequest
{
    public string CompanyName { get; set; } = string.Empty;
    public string Nip { get; set; } = string.Empty;
}

public class UpdateKsefTokenRequest
{
    public string KsefToken { get; set; } = string.Empty;
}