// Services/AuthService.cs
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using KSeF.Backend.Models.Data;
using KSeF.Backend.Models.Requests;
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

        if (company == null) return null;

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
                    HasKsefToken = !string.IsNullOrEmpty(user.CompanyProfile.KsefTokenEncrypted)
                }
                : null
        };
    }
}