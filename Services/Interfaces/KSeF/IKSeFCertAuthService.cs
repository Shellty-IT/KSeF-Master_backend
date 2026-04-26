using KSeF.Backend.Models.Responses.Common;

namespace KSeF.Backend.Services.Interfaces.KSeF;

public interface IKSeFCertAuthService
{
    Task<AuthResult> AuthenticateWithCertificateAsync(
        string nip,
        byte[] certificateBytes,
        byte[] privateKeyBytes,
        string? password,
        string environment = "Test",
        CancellationToken cancellationToken = default);
}