// Services/Interfaces/IKSeFAuthService.cs
using KSeF.Backend.Models.Responses.Common;

namespace KSeF.Backend.Services.Interfaces.KSeF;

public interface IKSeFAuthService
{
    Task<AuthResult> LoginAsync(string nip, string ksefToken, string environment = "Test", CancellationToken ct = default);
    Task<bool> RefreshTokenIfNeededAsync(CancellationToken ct = default);
    void Logout();
}