// Services/AuthService.cs
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using KSeF.Backend.Models.Data;
using KSeF.Backend.Models.Requests;
using KSeF.Backend.Models.Responses;
using KSeF.Backend.Repositories;
using KSeF.Backend.Services.Interfaces;

namespace KSeF.Backend.Services;

public class AuthService : IAuthService
{
    private readonly IUserRepository _userRepository;
    private readonly ICompanyRepository _companyRepository;
    private readonly ITokenEncryptionService _encryption;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        IUserRepository userRepository,
        ICompanyRepository companyRepository,
        ITokenEncryptionService encryption,
        IConfiguration configuration,
        ILogger<AuthService> logger)
    {
        _userRepository = userRepository;
        _companyRepository = companyRepository;
        _encryption = encryption;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<AppAuthResult> RegisterAsync(RegisterRequest request)
    {
        var errors = ValidateRegister(request);
        if (errors.Count > 0)
            return Fail(string.Join("; ", errors));

        var emailLower = request.Email.Trim().ToLowerInvariant();

        if (await _userRepository.EmailExistsAsync(emailLower))
            return Fail("Konto z tym adresem email już istnieje");

        var user = new User
        {
            Email = emailLower,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            Name = request.Name.Trim(),
            CreatedAt = DateTime.UtcNow
        };

        await _userRepository.CreateAsync(user);

        _logger.LogInformation("User registered: {Email}", emailLower);

        return new AppAuthResult
        {
            Success = true,
            Token = GenerateJwt(user),
            User = MapUserInfo(user)
        };
    }

    public async Task<AppAuthResult> LoginAsync(LoginAppRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            return Fail("Email i hasło są wymagane");

        var user = await _userRepository.GetByEmailAsync(request.Email);

        if (user == null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            return Fail("Nieprawidłowy email lub hasło");

        _logger.LogInformation("User logged in: {Email}", user.Email);

        return new AppAuthResult
        {
            Success = true,
            Token = GenerateJwt(user),
            User = MapUserInfo(user)
        };
    }

    public async Task<UserInfo?> GetUserByIdAsync(int userId)
    {
        var user = await _userRepository.GetByIdWithCompanyAsync(userId);
        return user == null ? null : MapUserInfo(user);
    }

    public async Task<AppAuthResult> SetupCompanyAsync(int userId, CompanySetupRequest request)
    {
        var errors = ValidateCompanySetup(request);
        if (errors.Count > 0)
            return Fail(string.Join("; ", errors));

        var user = await _userRepository.GetByIdWithCompanyAsync(userId);
        if (user == null)
            return Fail("Użytkownik nie istnieje");

        if (user.CompanyProfile != null)
            return Fail("Firma jest już skonfigurowana. Użyj aktualizacji tokenu.");

        var company = new CompanyProfile
        {
            UserId = userId,
            CompanyName = request.CompanyName.Trim(),
            Nip = request.Nip.Trim(),
            KsefTokenEncrypted = _encryption.Encrypt(request.KsefToken.Trim()),
            AuthMethod = "token",
            KsefEnvironment = request.KsefEnvironment,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        await _companyRepository.CreateAsync(company);

        _logger.LogInformation("Company configured for user {UserId}: NIP {Nip}, Environment {Env}",
            userId, request.Nip, request.KsefEnvironment);

        user.CompanyProfile = company;

        return new AppAuthResult
        {
            Success = true,
            Token = GenerateJwt(user),
            User = MapUserInfo(user)
        };
    }

    public async Task<AppAuthResult> UpdateKsefTokenAsync(int userId, UpdateKsefTokenRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.KsefToken))
            return Fail("Token KSeF jest wymagany");

        if (!request.KsefToken.Contains('|'))
            return Fail("Nieprawidłowy format tokenu KSeF");

        var company = await _companyRepository.GetByUserIdAsync(userId);
        if (company == null)
            return Fail("Najpierw skonfiguruj firmę");

        company.KsefTokenEncrypted = _encryption.Encrypt(request.KsefToken.Trim());
        company.UpdatedAt = DateTime.UtcNow;

        await _companyRepository.UpdateAsync(company);

        _logger.LogInformation("KSeF token updated for user {UserId}", userId);

        var user = await _userRepository.GetByIdWithCompanyAsync(userId);
        return new AppAuthResult { Success = true, User = MapUserInfo(user!) };
    }

    public async Task<AppAuthResult> UpdateCompanyProfileAsync(int userId, UpdateCompanyProfileRequest request)
    {
        var errors = ValidateCompanyProfile(request);
        if (errors.Count > 0)
            return Fail(string.Join("; ", errors));

        var company = await _companyRepository.GetByUserIdAsync(userId);
        if (company == null)
            return Fail("Profil firmy nie istnieje. Najpierw skonfiguruj firmę.");

        company.CompanyName = request.CompanyName.Trim();
        company.Nip = request.Nip.Trim();
        company.UpdatedAt = DateTime.UtcNow;

        await _companyRepository.UpdateAsync(company);

        _logger.LogInformation("Company profile updated for user {UserId}: {CompanyName}, NIP {Nip}",
            userId, request.CompanyName, request.Nip);

        var user = await _userRepository.GetByIdWithCompanyAsync(userId);
        return new AppAuthResult { Success = true, User = MapUserInfo(user!) };
    }

    public async Task<AppAuthResult> UpdateKsefEnvironmentAsync(int userId, UpdateKsefEnvironmentRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.KsefEnvironment))
            return Fail("Środowisko KSeF jest wymagane");

        if (request.KsefEnvironment != "Test" && request.KsefEnvironment != "Production")
            return Fail("Dozwolone środowiska: 'Test' lub 'Production'");

        var company = await _companyRepository.GetByUserIdAsync(userId);
        if (company == null)
            return Fail("Najpierw skonfiguruj firmę");

        company.KsefEnvironment = request.KsefEnvironment;
        company.UpdatedAt = DateTime.UtcNow;

        await _companyRepository.UpdateAsync(company);

        _logger.LogInformation("KSeF environment switched to {Environment} for user {UserId}",
            request.KsefEnvironment, userId);

        var user = await _userRepository.GetByIdWithCompanyAsync(userId);
        return new AppAuthResult { Success = true, User = MapUserInfo(user!) };
    }

    public async Task<string?> GetDecryptedKsefTokenAsync(int userId)
    {
        var company = await _companyRepository.GetByUserIdAsync(userId);
        if (company == null || string.IsNullOrEmpty(company.KsefTokenEncrypted))
            return null;

        try
        {
            return _encryption.Decrypt(company.KsefTokenEncrypted);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to decrypt KSeF token for user {UserId}", userId);
            return null;
        }
    }

    public async Task<AppAuthResult> UploadCertificateAsync(int userId, UploadCertificateRequest request)
    {
        var company = await _companyRepository.GetByUserIdAsync(userId);
        if (company == null)
            return Fail("Najpierw skonfiguruj firmę");

        try
        {
            _logger.LogInformation("Uploading certificate for user {UserId}", userId);

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
                var publicCert = X509Certificate2.CreateFromPem(certText);
                _logger.LogInformation("Certificate parsed: {Subject}", publicCert.Subject);
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
            return new AppAuthResult { Success = true, User = MapUserInfo(user!) };
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
        return new AppAuthResult { Success = true, User = MapUserInfo(user!) };
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
        return new AppAuthResult { Success = true, User = MapUserInfo(user!) };
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
            {
                password = _encryption.Decrypt(company.CertificatePasswordEncrypted)?.Trim();
            }

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

            _logger.LogInformation("Certificate decrypted for user {UserId}", userId);
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

    private string GenerateJwt(User user)
    {
        var jwtKey = _configuration.GetValue<string>("Jwt:Key")
            ?? throw new InvalidOperationException("Jwt:Key is not configured");
        var jwtIssuer = _configuration.GetValue<string>("Jwt:Issuer") ?? "KSeFMaster";
        var jwtAudience = _configuration.GetValue<string>("Jwt:Audience") ?? "KSeFMasterApp";
        var expirationHours = _configuration.GetValue<int>("Jwt:ExpirationHours", 24);

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Name, user.Name),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: jwtIssuer,
            audience: jwtAudience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(expirationHours),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static AppAuthResult Fail(string error) =>
        new() { Success = false, Error = error };

    private static List<string> ValidateRegister(RegisterRequest request)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(request.Email))
            errors.Add("Email jest wymagany");
        else if (!request.Email.Contains('@') || !request.Email.Contains('.'))
            errors.Add("Nieprawidłowy format email");

        if (string.IsNullOrWhiteSpace(request.Password))
            errors.Add("Hasło jest wymagane");
        else if (request.Password.Length < 8)
            errors.Add("Hasło musi mieć co najmniej 8 znaków");

        if (string.IsNullOrWhiteSpace(request.Name))
            errors.Add("Imię i nazwisko jest wymagane");

        return errors;
    }

    private static List<string> ValidateCompanySetup(CompanySetupRequest request)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(request.CompanyName))
            errors.Add("Nazwa firmy jest wymagana");

        if (string.IsNullOrWhiteSpace(request.Nip))
            errors.Add("NIP jest wymagany");
        else if (request.Nip.Trim().Length != 10 || !request.Nip.Trim().All(char.IsDigit))
            errors.Add("NIP musi mieć dokładnie 10 cyfr");

        if (string.IsNullOrWhiteSpace(request.KsefToken))
            errors.Add("Token KSeF jest wymagany");
        else if (!request.KsefToken.Contains('|'))
            errors.Add("Nieprawidłowy format tokenu KSeF");

        if (request.KsefEnvironment != "Test" && request.KsefEnvironment != "Production")
            errors.Add("Dozwolone środowiska: 'Test' lub 'Production'");

        return errors;
    }

    private static List<string> ValidateCompanyProfile(UpdateCompanyProfileRequest request)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(request.CompanyName))
            errors.Add("Nazwa firmy jest wymagana");

        if (string.IsNullOrWhiteSpace(request.Nip))
            errors.Add("NIP jest wymagany");
        else if (request.Nip.Trim().Length != 10 || !request.Nip.Trim().All(char.IsDigit))
            errors.Add("NIP musi mieć dokładnie 10 cyfr");

        return errors;
    }

    private static UserInfo MapUserInfo(User user)
    {
        return new UserInfo
        {
            Id = user.Id,
            Email = user.Email,
            Name = user.Name,
            Company = user.CompanyProfile != null
                ? new CompanyInfo
                {
                    Id = user.CompanyProfile.Id,
                    CompanyName = user.CompanyProfile.CompanyName,
                    Nip = user.CompanyProfile.Nip,
                    IsActive = user.CompanyProfile.IsActive,
                    HasKsefToken = !string.IsNullOrEmpty(user.CompanyProfile.KsefTokenEncrypted),
                    AuthMethod = user.CompanyProfile.AuthMethod,
                    KsefEnvironment = user.CompanyProfile.KsefEnvironment,
                    HasCertificate = !string.IsNullOrEmpty(user.CompanyProfile.CertificateEncrypted)
                }
                : null
        };
    }
}