using KSeF.Backend.Models.Responses;

namespace KSeF.Backend.Services.Interfaces;

public interface IKSeFAuthService
{
    /// <summary>
    /// Pełny proces logowania: certyfikaty → challenge → szyfrowanie → auth → redeem
    /// Po zakończeniu accessToken jest zapisany w SessionManager
    /// </summary>
    Task<AuthResult> LoginAsync(string nip, string ksefToken, CancellationToken ct = default);

    /// <summary>
    /// Odświeża accessToken używając refreshToken
    /// </summary>
    Task<bool> RefreshTokenIfNeededAsync(CancellationToken ct = default);

    /// <summary>
    /// Wylogowanie - czyści sesję
    /// </summary>
    void Logout();
}