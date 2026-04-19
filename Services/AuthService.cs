// Services/AuthService.cs
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using KSeF.Backend.Models.Data;
using KSeF.Backend.Models.Requests;
using KSeF.Backend.Models.Responses;
using KSeF.Backend.Services.Interfaces;

namespace KSeF.Backend.Services;

public class AuthService : IAuthService
{
    private readonly AppDbContext _db;
    private readonly ITokenEncryptionService _encryption;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        AppDbContext db,
        ITokenEncryptionService encryption,
        IConfiguration configuration,
        ILogger<AuthService> logger)
    {
        _db = db;
        _encryption = encryption;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<AppAuthResult> RegisterAsync(RegisterRequest request)
    {
        var errors = ValidateRegister(request);
        if (errors.Count > 0)
            return new AppAuthResult { Success = false, Error = string.Join("; ", errors) };

        var emailLower = request.Email.Trim().ToLowerInvariant();

        var exists = await _db.Users.AnyAsync(u => u.Email == emailLower);
        if (exists)
            return new AppAuthResult { Success = false, Error = "Konto z tym adresem email już istnieje" };

        var user = new User
        {
            Email = emailLower,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            Name = request.Name.Trim(),
            CreatedAt = DateTime.UtcNow
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        _logger.LogInformation("User registered: {Email}", emailLower);

        var token = GenerateJwt(user);
        var userInfo = MapUserInfo(user);

        return new AppAuthResult { Success = true, Token = token, User = userInfo };
    }

    public async Task<AppAuthResult> LoginAsync(LoginAppRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            return new AppAuthResult { Success = false, Error = "Email i hasło są wymagane" };

        var emailLower = request.Email.Trim().ToLowerInvariant();

        var user = await _db.Users
            .Include(u => u.CompanyProfile)
            .FirstOrDefaultAsync(u => u.Email == emailLower);

        if (user == null)
            return new AppAuthResult { Success = false, Error = "Nieprawidłowy email lub hasło" };

        if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            return new AppAuthResult { Success = false, Error = "Nieprawidłowy email lub hasło" };

        _logger.LogInformation("User logged in: {Email}", emailLower);

        var token = GenerateJwt(user);
        var userInfo = MapUserInfo(user);

        return new AppAuthResult { Success = true, Token = token, User = userInfo };
    }

    public async Task<UserInfo?> GetUserByIdAsync(int userId)
    {
        var user = await _db.Users
            .Include(u => u.CompanyProfile)
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user == null) return null;

        return MapUserInfo(user);
    }

    public async Task<AppAuthResult> SetupCompanyAsync(int userId, CompanySetupRequest request)
    {
        var errors = ValidateCompanySetup(request);
        if (errors.Count > 0)
            return new AppAuthResult { Success = false, Error = string.Join("; ", errors) };

        var user = await _db.Users
            .Include(u => u.CompanyProfile)
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user == null)
            return new AppAuthResult { Success = false, Error = "Użytkownik nie istnieje" };

        if (user.CompanyProfile != null)
            return new AppAuthResult { Success = false, Error = "Firma jest już skonfigurowana. Użyj aktualizacji tokenu." };

        var encryptedToken = _encryption.Encrypt(request.KsefToken.Trim());

        var company = new CompanyProfile
        {
            UserId = userId,
            CompanyName = request.CompanyName.Trim(),
            Nip = request.Nip.Trim(),
            KsefTokenEncrypted = encryptedToken,
            AuthMethod = "token",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _db.CompanyProfiles.Add(company);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Company configured for user {UserId}: NIP {Nip}", userId, request.Nip);

        var token = GenerateJwt(user);
        user.CompanyProfile = company;
        var userInfo = MapUserInfo(user);

        return new AppAuthResult { Success = true, Token = token, User = userInfo };
    }

    public async Task<AppAuthResult> UpdateKsefTokenAsync(int userId, UpdateKsefTokenRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.KsefToken))
            return new AppAuthResult { Success = false, Error = "Token KSeF jest wymagany" };

        if (!request.KsefToken.Contains('|'))
            return new AppAuthResult { Success = false, Error = "Nieprawidłowy format tokenu KSeF" };

        var user = await _db.Users
            .Include(u => u.CompanyProfile)
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user == null)
            return new AppAuthResult { Success = false, Error = "Użytkownik nie istnieje" };

        if (user.CompanyProfile == null)
            return new AppAuthResult { Success = false, Error = "Najpierw skonfiguruj firmę" };

        user.CompanyProfile.KsefTokenEncrypted = _encryption.Encrypt(request.KsefToken.Trim());
        user.CompanyProfile.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        _logger.LogInformation("KSeF token updated for user {UserId}", userId);

        var userInfo = MapUserInfo(user);

        return new AppAuthResult { Success = true, User = userInfo };
    }

    public async Task<string?> GetDecryptedKsefTokenAsync(int userId)
    {
        var company = await _db.CompanyProfiles
            .FirstOrDefaultAsync(c => c.UserId == userId && c.IsActive);

        if (company == null || string.IsNullOrEmpty(company.KsefTokenEncrypted)) return null;

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
    var user = await _db.Users
        .Include(u => u.CompanyProfile)
        .FirstOrDefaultAsync(u => u.Id == userId);

    if (user == null)
        return new AppAuthResult { Success = false, Error = "Użytkownik nie istnieje" };

    if (user.CompanyProfile == null)
        return new AppAuthResult { Success = false, Error = "Najpierw skonfiguruj firmę" };

    try
    {
        _logger.LogInformation("Uploading certificate for user {UserId}", userId);
        _logger.LogDebug("  Cert Base64 length: {Len}", request.CertificateBase64?.Length ?? 0);
        _logger.LogDebug("  Key Base64 length: {Len}", request.PrivateKeyBase64?.Length ?? 0);
        _logger.LogDebug("  Password provided: {HasPwd}", !string.IsNullOrEmpty(request.Password));

        // ✅ Walidacja Base64
        if (string.IsNullOrWhiteSpace(request.CertificateBase64))
            return new AppAuthResult { Success = false, Error = "Certyfikat jest wymagany" };

        if (string.IsNullOrWhiteSpace(request.PrivateKeyBase64))
            return new AppAuthResult { Success = false, Error = "Klucz prywatny jest wymagany" };

        // ✅ Konwersja i walidacja
        byte[] certBytes;
        byte[] keyBytes;

        try
        {
            certBytes = Convert.FromBase64String(request.CertificateBase64);
            keyBytes = Convert.FromBase64String(request.PrivateKeyBase64);
        }
        catch (FormatException)
        {
            return new AppAuthResult { Success = false, Error = "Nieprawidłowy format Base64 certyfikatu lub klucza" };
        }

        _logger.LogDebug("  Cert bytes length: {Len}", certBytes.Length);
        _logger.LogDebug("  Key bytes length: {Len}", keyBytes.Length);

        // ✅ Sprawdzenie czy to PEM
        var certText = Encoding.UTF8.GetString(certBytes);
        var keyText = Encoding.UTF8.GetString(keyBytes);

        if (!certText.Contains("BEGIN CERTIFICATE"))
        {
            return new AppAuthResult { Success = false, Error = "Certyfikat nie jest w formacie PEM (brak BEGIN CERTIFICATE)" };
        }

        if (!keyText.Contains("PRIVATE KEY"))
        {
            return new AppAuthResult { Success = false, Error = "Klucz prywatny nie jest w formacie PEM (brak PRIVATE KEY)" };
        }

        _logger.LogDebug("  ✓ PEM format detected");

        // ✅ Walidacja certyfikatu (tylko publiczny dla sprawdzenia struktury)
        X509Certificate2 publicCert;
        try
        {
            publicCert = X509Certificate2.CreateFromPem(certText);
            _logger.LogInformation("  ✓ Certificate parsed: {Subject}", publicCert.Subject);
            _logger.LogInformation("  Valid: {From} → {To}", publicCert.NotBefore, publicCert.NotAfter);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse certificate");
            return new AppAuthResult { Success = false, Error = $"Nieprawidłowy certyfikat: {ex.Message}" };
        }

        // ✅ Zapisanie (szyfrowanie ORYGINALNYCH Base64 stringów)
        user.CompanyProfile.CertificateEncrypted = _encryption.Encrypt(request.CertificateBase64.Trim());
        user.CompanyProfile.PrivateKeyEncrypted = _encryption.Encrypt(request.PrivateKeyBase64.Trim());

        // ✅ FIX: Trim hasła + debug log
        if (!string.IsNullOrEmpty(request.Password))
        {
            var passwordTrimmed = request.Password.Trim();
            _logger.LogDebug("  Password length (trimmed): {Len}", passwordTrimmed.Length);
            user.CompanyProfile.CertificatePasswordEncrypted = _encryption.Encrypt(passwordTrimmed);
        }
        else
        {
            _logger.LogWarning("  No password provided for private key");
            user.CompanyProfile.CertificatePasswordEncrypted = null;
        }

        user.CompanyProfile.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        _logger.LogInformation("✅ Certificate uploaded successfully for user {UserId}", userId);

        var userInfo = MapUserInfo(user);
        return new AppAuthResult { Success = true, User = userInfo };
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to upload certificate for user {UserId}", userId);
        return new AppAuthResult { Success = false, Error = $"Błąd zapisu certyfikatu: {ex.Message}" };
    }
}

    public async Task<AppAuthResult> SwitchAuthMethodAsync(int userId, SwitchAuthMethodRequest request)
    {
        var user = await _db.Users
            .Include(u => u.CompanyProfile)
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user == null)
            return new AppAuthResult { Success = false, Error = "Użytkownik nie istnieje" };

        if (user.CompanyProfile == null)
            return new AppAuthResult { Success = false, Error = "Najpierw skonfiguruj firmę" };

        if (request.AuthMethod != "token" && request.AuthMethod != "certificate")
            return new AppAuthResult { Success = false, Error = "Dozwolone metody: 'token' lub 'certificate'" };

        if (request.AuthMethod == "certificate")
        {
            if (string.IsNullOrEmpty(user.CompanyProfile.CertificateEncrypted))
                return new AppAuthResult { Success = false, Error = "Najpierw prześlij certyfikat" };
        }

        if (request.AuthMethod == "token")
        {
            if (string.IsNullOrEmpty(user.CompanyProfile.KsefTokenEncrypted))
                return new AppAuthResult { Success = false, Error = "Token KSeF nie jest skonfigurowany" };
        }

        user.CompanyProfile.AuthMethod = request.AuthMethod;
        user.CompanyProfile.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        _logger.LogInformation("Auth method switched to {Method} for user {UserId}", request.AuthMethod, userId);

        var userInfo = MapUserInfo(user);
        return new AppAuthResult { Success = true, User = userInfo };
    }

    public async Task<AppAuthResult> DeleteCertificateAsync(int userId)
    {
        var user = await _db.Users
            .Include(u => u.CompanyProfile)
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user == null)
            return new AppAuthResult { Success = false, Error = "Użytkownik nie istnieje" };

        if (user.CompanyProfile == null)
            return new AppAuthResult { Success = false, Error = "Profil firmy nie istnieje" };

        user.CompanyProfile.CertificateEncrypted = null;
        user.CompanyProfile.PrivateKeyEncrypted = null;
        user.CompanyProfile.CertificatePasswordEncrypted = null;
        user.CompanyProfile.AuthMethod = "token";
        user.CompanyProfile.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        _logger.LogInformation("Certificate deleted for user {UserId}", userId);

        var userInfo = MapUserInfo(user);
        return new AppAuthResult { Success = true, User = userInfo };
    }

public async Task<(byte[]? cert, byte[]? key, string? password)?> GetDecryptedCertificateAsync(int userId)
{
    var company = await _db.CompanyProfiles
        .FirstOrDefaultAsync(c => c.UserId == userId && c.IsActive);

    if (company == null) return null;

    if (string.IsNullOrEmpty(company.CertificateEncrypted) || string.IsNullOrEmpty(company.PrivateKeyEncrypted))
        return null;

    try
    {
        _logger.LogInformation("🔐 Decrypting certificate for user {UserId}", userId);

        // ✅ Deszyfrowanie Base64 stringów z bazy
        var certBase64 = _encryption.Decrypt(company.CertificateEncrypted);
        var keyBase64 = _encryption.Decrypt(company.PrivateKeyEncrypted);

        _logger.LogDebug("  📄 Decrypted cert Base64 length: {Len}", certBase64?.Length ?? 0);
        _logger.LogDebug("  🔑 Decrypted key Base64 length: {Len}", keyBase64?.Length ?? 0);

        string? password = null;
        if (!string.IsNullOrEmpty(company.CertificatePasswordEncrypted))
        {
            password = _encryption.Decrypt(company.CertificatePasswordEncrypted);
            _logger.LogDebug("  🔒 Decrypted password length: {Len}", password?.Length ?? 0);
            
            // ✅ DEBUG: Sprawdź białe znaki
            if (password != null && password != password.Trim())
            {
                _logger.LogWarning("  ⚠️ Password contains leading/trailing whitespace!");
                _logger.LogWarning("  Original: '{Orig}' (len={OrigLen})", password, password.Length);
                password = password.Trim();
                _logger.LogWarning("  Trimmed: '{Trim}' (len={TrimLen})", password, password.Length);
            }
        }
        else
        {
            _logger.LogDebug("  🔓 No password in database (unencrypted key expected)");
        }

        // ✅ Konwersja Base64 → bytes (to są bytes PEM text)
        byte[] certBytes;
        byte[] keyBytes;
        
        try
        {
            certBytes = Convert.FromBase64String(certBase64);
            keyBytes = Convert.FromBase64String(keyBase64);
        }
        catch (FormatException ex)
        {
            _logger.LogError(ex, "❌ Invalid Base64 format in encrypted certificate/key");
            return null;
        }

        _logger.LogDebug("  📄 Cert bytes length: {Len}", certBytes.Length);
        _logger.LogDebug("  🔑 Key bytes length: {Len}", keyBytes.Length);

        // ✅ DEBUG: Sprawdź czy to faktycznie PEM
        var certPreview = Encoding.UTF8.GetString(certBytes.Take(50).ToArray());
        var keyPreview = Encoding.UTF8.GetString(keyBytes.Take(50).ToArray());
        _logger.LogDebug("  📄 Cert preview (50 chars): {Preview}", certPreview);
        _logger.LogDebug("  🔑 Key preview (50 chars): {Preview}", keyPreview);

        // ✅ Sprawdź czy PEM ma poprawne headery
        var certText = Encoding.UTF8.GetString(certBytes);
        var keyText = Encoding.UTF8.GetString(keyBytes);
        
        var hasCertHeader = certText.Contains("-----BEGIN CERTIFICATE-----");
        var hasKeyHeader = keyText.Contains("-----BEGIN") && keyText.Contains("PRIVATE KEY");
        
        _logger.LogDebug("  📄 Has cert header: {HasHeader}", hasCertHeader);
        _logger.LogDebug("  🔑 Has key header: {HasHeader}", hasKeyHeader);
        
        if (!hasCertHeader)
        {
            _logger.LogError("  ❌ Certificate doesn't contain PEM header!");
            return null;
        }
        
        if (!hasKeyHeader)
        {
            _logger.LogError("  ❌ Private key doesn't contain PEM header!");
            return null;
        }

        _logger.LogInformation("  ✅ Certificate and key decrypted successfully");
        return (certBytes, keyBytes, password);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "❌ Failed to decrypt certificate for user {UserId}", userId);
        return null;
    }
}

    public async Task<UserCertificateInfo?> GetCertificateInfoAsync(int userId)
    {
        var company = await _db.CompanyProfiles
            .FirstOrDefaultAsync(c => c.UserId == userId && c.IsActive);

        if (company == null) return null;

        var hasCert = !string.IsNullOrEmpty(company.CertificateEncrypted);
        var hasKey = !string.IsNullOrEmpty(company.PrivateKeyEncrypted);
        var hasPassword = !string.IsNullOrEmpty(company.CertificatePasswordEncrypted);

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
            var certBase64 = _encryption.Decrypt(company.CertificateEncrypted);
            var certBytes = Convert.FromBase64String(certBase64);
            var cert = new X509Certificate2(certBytes);

            return new UserCertificateInfo
            {
                HasCertificate = hasCert,
                HasPrivateKey = hasKey,
                IsPasswordProtected = hasPassword,
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
                HasCertificate = hasCert,
                HasPrivateKey = hasKey,
                IsPasswordProtected = hasPassword,
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

    private List<string> ValidateRegister(RegisterRequest request)
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

    private List<string> ValidateCompanySetup(CompanySetupRequest request)
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

        return errors;
    }

    private UserInfo MapUserInfo(User user)
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
                    HasCertificate = !string.IsNullOrEmpty(user.CompanyProfile.CertificateEncrypted)
                }
                : null
        };
    }
}