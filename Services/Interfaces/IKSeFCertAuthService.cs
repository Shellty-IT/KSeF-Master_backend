// Services/Interfaces/IKSeFCertAuthService.cs
using KSeF.Backend.Models.Responses;

namespace KSeF.Backend.Services.Interfaces;

public interface IKSeFCertAuthService
{
    Task<AuthResult> AuthenticateWithCertificateAsync(
        string nip,
        byte[] certificateBytes,
        byte[] privateKeyBytes,
        string? password,
        CancellationToken cancellationToken = default
    );
}