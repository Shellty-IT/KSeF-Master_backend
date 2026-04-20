using KSeF.Backend.Models.Responses;

namespace KSeF.Backend.Services.Interfaces;

public interface IKSeFAuthService
{
    Task<AuthResult> LoginAsync(string nip, string ksefToken, string environment = "Test", CancellationToken ct = default);

    Task<bool> RefreshTokenIfNeededAsync(CancellationToken ct = default);

    void Logout();
}