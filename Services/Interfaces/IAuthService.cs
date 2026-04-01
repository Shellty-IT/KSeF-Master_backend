// Services/Interfaces/IAuthService.cs
using KSeF.Backend.Models.Requests;

namespace KSeF.Backend.Services.Interfaces;

public class AppAuthResult
{
    public bool Success { get; set; }
    public string? Token { get; set; }
    public string? Error { get; set; }
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
}

public interface IAuthService
{
    Task<AppAuthResult> RegisterAsync(RegisterRequest request);
    Task<AppAuthResult> LoginAsync(LoginAppRequest request);
    Task<UserInfo?> GetUserByIdAsync(int userId);
    Task<AppAuthResult> SetupCompanyAsync(int userId, CompanySetupRequest request);
    Task<AppAuthResult> UpdateKsefTokenAsync(int userId, UpdateKsefTokenRequest request);
    Task<string?> GetDecryptedKsefTokenAsync(int userId);
}