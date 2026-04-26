// Services/Auth/CertificateService.cs
using System.Security.Cryptography.X509Certificates;
using System.Text;
using KSeF.Backend.Models.Requests;
using KSeF.Backend.Models.Responses;
using KSeF.Backend.Repositories;
using KSeF.Backend.Services.Interfaces.Auth;

namespace KSeF.Backend.Services.Auth;

public class CertificateService : ICertificateService
{
    private readonly IUserRepository _userRepository;
    private readonly ICompanyRepository _companyRepository;
    private readonly ITokenEncryptionService _encryption;
    private readonly ILogger<CertificateService> _logger;

    public CertificateService(
        IUserRepository userRepository,
        ICompanyRepository companyRepository,
        ITokenEncryptionService encryption,
        ILogger<CertificateService> logger)
    {
        _userRepository = userRepository;
        _companyRepository = companyRepository;
        _encryption = encryption;
        _logger = logger;
    }

    public async Task<AppAuthResult> UploadCertificateAsync(int userId, UploadCertificateRequest request)
    {
        var company = await _companyRepository.GetByUserIdAsync(userId);
        if (company == null)
            return Fail("Najpierw skonfiguruj firmę");

        try
        {
            if (string.IsNullOrWhiteSpace(request.CertificateBase64))
                return Fail("Certyfikat jest wymagany");

            if (string.IsNullOrWhiteSpace(request.PrivateKeyBase64))
                return Fail("Klucz prywatny jest wymagany");

            byte[] certBytes;
            byte[] keyBytes;

            try
            {
                certBytes = Convert.FromBase64String(request.CertificateBase64);
                keyBytes = Convert.FromBase64String(request.PrivateKeyBase64);
            }
            catch (FormatException)
            {
                return Fail("Nieprawidłowy format Base64 certyfikatu lub klucza");
            }

            var certText = Encoding.UTF8.GetString(certBytes);
            var keyText = Encoding.UTF8.GetString(keyBytes);

            if (!certText.Contains("BEGIN CERTIFICATE"))
                return Fail("Certyfikat nie jest w formacie PEM (brak BEGIN CERTIFICATE)");

            if (!keyText.Contains("PRIVATE KEY"))
                return Fail("Klucz prywatny nie jest w formacie PEM (brak PRIVATE KEY)");

            try
            {
                var cert = X509Certificate2.CreateFromPem(certText);
                _logger.LogInformation("Certificate parsed: {Subject}", cert.Subject);
            }
            catch (Exception ex)
            {
                return Fail($"Nieprawidłowy certyfikat: {ex.Message}");
            }

            company.CertificateEncrypted = _encryption.Encrypt(request.CertificateBase64.Trim());
            company.PrivateKeyEncrypted = _encryption.Encrypt(request.PrivateKeyBase64.Trim());
            company.CertificatePasswordEncrypted = !string.IsNullOrEmpty(request.Password)
                ? _encryption.Encrypt(request.Password.Trim())
                : null;
            company.UpdatedAt = DateTime.UtcNow;

            await _companyRepository.UpdateAsync(company);

            _logger.LogInformation("Certificate uploaded for user {UserId}", userId);

            var user = await _userRepository.GetByIdWithCompanyAsync(userId);
            return new AppAuthResult { Success = true, User = UserAuthService.MapUserInfo(user!) };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload certificate for user {UserId}", userId);
            return Fail($"Błąd zapisu certyfikatu: {ex.Message}");
        }
    }

    public async Task<AppAuthResult> SwitchAuthMethodAsync(int userId, SwitchAuthMethodRequest request)
    {
        if (request.AuthMethod != "token" && request.AuthMethod != "certificate")
            return Fail("Dozwolone metody: 'token' lub 'certificate'");

        var company = await _companyRepository.GetByUserIdAsync(userId);
        if (company == null)
            return Fail("Najpierw skonfiguruj firmę");

        if (request.AuthMethod == "certificate" && string.IsNullOrEmpty(company.CertificateEncrypted))
            return Fail("Najpierw prześlij certyfikat");

        if (request.AuthMethod == "token" && string.IsNullOrEmpty(company.KsefTokenEncrypted))
            return Fail("Token KSeF nie jest skonfigurowany");

        company.AuthMethod = request.AuthMethod;
        company.UpdatedAt = DateTime.UtcNow;

        await _companyRepository.UpdateAsync(company);

        _logger.LogInformation("Auth method switched to {Method} for user {UserId}",
            request.AuthMethod, userId);

        var user = await _userRepository.GetByIdWithCompanyAsync(userId);
        return new AppAuthResult { Success = true, User = UserAuthService.MapUserInfo(user!) };
    }

    public async Task<AppAuthResult> DeleteCertificateAsync(int userId)
    {
        var company = await _companyRepository.GetByUserIdAsync(userId);
        if (company == null)
            return Fail("Profil firmy nie istnieje");

        company.CertificateEncrypted = null;
        company.PrivateKeyEncrypted = null;
        company.CertificatePasswordEncrypted = null;
        company.AuthMethod = "token";
        company.UpdatedAt = DateTime.UtcNow;

        await _companyRepository.UpdateAsync(company);

        _logger.LogInformation("Certificate deleted for user {UserId}", userId);

        var user = await _userRepository.GetByIdWithCompanyAsync(userId);
        return new AppAuthResult { Success = true, User = UserAuthService.MapUserInfo(user!) };
    }

    public async Task<(byte[]? cert, byte[]? key, string? password)?> GetDecryptedCertificateAsync(int userId)
    {
        var company = await _companyRepository.GetByUserIdAsync(userId);
        if (company == null) return null;

        if (string.IsNullOrEmpty(company.CertificateEncrypted) ||
            string.IsNullOrEmpty(company.PrivateKeyEncrypted))
            return null;

        try
        {
            var certBase64 = _encryption.Decrypt(company.CertificateEncrypted);
            var keyBase64 = _encryption.Decrypt(company.PrivateKeyEncrypted);

            if (string.IsNullOrEmpty(certBase64) || string.IsNullOrEmpty(keyBase64))
                return null;

            string? password = null;
            if (!string.IsNullOrEmpty(company.CertificatePasswordEncrypted))
                password = _encryption.Decrypt(company.CertificatePasswordEncrypted)?.Trim();

            var certBytes = Convert.FromBase64String(certBase64);
            var keyBytes = Convert.FromBase64String(keyBase64);

            var certText = Encoding.UTF8.GetString(certBytes);
            var keyText = Encoding.UTF8.GetString(keyBytes);

            if (!certText.Contains("-----BEGIN CERTIFICATE-----"))
            {
                _logger.LogError("Certificate missing PEM header for user {UserId}", userId);
                return null;
            }

            if (!keyText.Contains("-----BEGIN") || !keyText.Contains("PRIVATE KEY"))
            {
                _logger.LogError("Private key missing PEM header for user {UserId}", userId);
                return null;
            }

            return (certBytes, keyBytes, password);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to decrypt certificate for user {UserId}", userId);
            return null;
        }
    }

    public async Task<UserCertificateInfo?> GetCertificateInfoAsync(int userId)
    {
        var company = await _companyRepository.GetByUserIdAsync(userId);
        if (company == null) return null;

        var hasCert = !string.IsNullOrEmpty(company.CertificateEncrypted);

        if (!hasCert)
        {
            return new UserCertificateInfo
            {
                HasCertificate = false,
                HasPrivateKey = false,
                IsPasswordProtected = false
            };
        }

        try
        {
            var certBase64 = _encryption.Decrypt(company.CertificateEncrypted!);
            var certBytes = Convert.FromBase64String(certBase64);
            var cert = new X509Certificate2(certBytes);

            return new UserCertificateInfo
            {
                HasCertificate = true,
                HasPrivateKey = !string.IsNullOrEmpty(company.PrivateKeyEncrypted),
                IsPasswordProtected = !string.IsNullOrEmpty(company.CertificatePasswordEncrypted),
                UploadedAt = company.UpdatedAt ?? company.CreatedAt,
                SubjectName = cert.Subject,
                NotBefore = cert.NotBefore,
                NotAfter = cert.NotAfter
            };
        }
        catch
        {
            return new UserCertificateInfo
            {
                HasCertificate = true,
                HasPrivateKey = !string.IsNullOrEmpty(company.PrivateKeyEncrypted),
                IsPasswordProtected = !string.IsNullOrEmpty(company.CertificatePasswordEncrypted),
                UploadedAt = company.UpdatedAt ?? company.CreatedAt
            };
        }
    }

    private static AppAuthResult Fail(string error) =>
        new() { Success = false, Error = error };
}