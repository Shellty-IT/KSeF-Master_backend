using KSeF.Backend.Models.Responses;

namespace KSeF.Backend.Services.Interfaces;

public interface IKSeFCertAuthService
{
    Task<AuthResult> AuthenticateWithCertificateAsync(
        string nip,
        byte[] certificateBytes,
        byte[] privateKeyBytes,
        string? password,
        string environment = "Test",
        CancellationToken cancellationToken = default
    );
}