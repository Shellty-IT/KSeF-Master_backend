// Controllers/AuthController.cs
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using KSeF.Backend.Models.Requests;
using KSeF.Backend.Services;
using KSeF.Backend.Services.Interfaces;

namespace KSeF.Backend.Controllers;

[ApiController]
[Route("api/auth")]
[Produces("application/json")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly ILogger<AuthController> _logger;

    public AuthController(IAuthService authService, ILogger<AuthController> logger)
    {
        _authService = authService;
        _logger = logger;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        _logger.LogInformation("=== REGISTER REQUEST: {Email} ===", request.Email);

        var result = await _authService.RegisterAsync(request);

        if (!result.Success)
            return BadRequest(new { success = false, error = result.Error });

        return Ok(new
        {
            success = true,
            message = "Konto utworzone pomyślnie",
            data = new
            {
                token = result.Token,
                user = result.User
            }
        });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginAppRequest request)
    {
        _logger.LogInformation("=== APP LOGIN REQUEST: {Email} ===", request.Email);

        var result = await _authService.LoginAsync(request);

        if (!result.Success)
            return Unauthorized(new { success = false, error = result.Error });

        return Ok(new
        {
            success = true,
            message = "Zalogowano pomyślnie",
            data = new
            {
                token = result.Token,
                user = result.User
            }
        });
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> GetMe()
    {
        var userId = GetUserId();
        if (userId == null)
            return Unauthorized(new { success = false, error = "Nieprawidłowy token" });

        var user = await _authService.GetUserByIdAsync(userId.Value);

        if (user == null)
            return NotFound(new { success = false, error = "Użytkownik nie istnieje" });

        return Ok(new { success = true, data = user });
    }

    [HttpPost("company")]
    [Authorize]
    public async Task<IActionResult> SetupCompany([FromBody] CompanySetupRequest request)
    {
        var userId = GetUserId();
        if (userId == null)
            return Unauthorized(new { success = false, error = "Nieprawidłowy token" });

        _logger.LogInformation("=== COMPANY SETUP: User {UserId}, NIP {Nip} ===", userId, request.Nip);

        var result = await _authService.SetupCompanyAsync(userId.Value, request);

        if (!result.Success)
            return BadRequest(new { success = false, error = result.Error });

        return Ok(new
        {
            success = true,
            message = "Firma skonfigurowana pomyślnie",
            data = new
            {
                token = result.Token,
                user = result.User
            }
        });
    }

    [HttpPut("company/token")]
    [Authorize]
    public async Task<IActionResult> UpdateKsefToken([FromBody] UpdateKsefTokenRequest request)
    {
        var userId = GetUserId();
        if (userId == null)
            return Unauthorized(new { success = false, error = "Nieprawidłowy token" });

        _logger.LogInformation("=== UPDATE KSEF TOKEN: User {UserId} ===", userId);

        var result = await _authService.UpdateKsefTokenAsync(userId.Value, request);

        if (!result.Success)
            return BadRequest(new { success = false, error = result.Error });

        return Ok(new
        {
            success = true,
            message = "Token KSeF zaktualizowany pomyślnie",
            data = result.User
        });
    }

    [HttpPost("company/certificate")]
    [Authorize]
    public async Task<IActionResult> UploadCertificate([FromBody] UploadCertificateRequest request)
    {
        var userId = GetUserId();
        if (userId == null)
            return Unauthorized(new { success = false, error = "Nieprawidłowy token" });

        _logger.LogInformation("=== UPLOAD CERTIFICATE: User {UserId} ===", userId);

        var result = await _authService.UploadCertificateAsync(userId.Value, request);

        if (!result.Success)
            return BadRequest(new { success = false, error = result.Error });

        return Ok(new
        {
            success = true,
            message = "Certyfikat przesłany pomyślnie",
            data = result.User
        });
    }

    [HttpPut("company/auth-method")]
    [Authorize]
    public async Task<IActionResult> SwitchAuthMethod([FromBody] SwitchAuthMethodRequest request)
    {
        var userId = GetUserId();
        if (userId == null)
            return Unauthorized(new { success = false, error = "Nieprawidłowy token" });

        _logger.LogInformation("=== SWITCH AUTH METHOD: User {UserId}, Method {Method} ===", userId, request.AuthMethod);

        var result = await _authService.SwitchAuthMethodAsync(userId.Value, request);

        if (!result.Success)
            return BadRequest(new { success = false, error = result.Error });

        return Ok(new
        {
            success = true,
            message = $"Metoda uwierzytelniania zmieniona na '{request.AuthMethod}'",
            data = result.User
        });
    }

    [HttpDelete("company/certificate")]
    [Authorize]
    public async Task<IActionResult> DeleteCertificate()
    {
        var userId = GetUserId();
        if (userId == null)
            return Unauthorized(new { success = false, error = "Nieprawidłowy token" });

        _logger.LogInformation("=== DELETE CERTIFICATE: User {UserId} ===", userId);

        var result = await _authService.DeleteCertificateAsync(userId.Value);

        if (!result.Success)
            return BadRequest(new { success = false, error = result.Error });

        return Ok(new
        {
            success = true,
            message = "Certyfikat usunięty. Metoda zmieniona na 'token'.",
            data = result.User
        });
    }

    [HttpGet("company/certificate/info")]
    [Authorize]
    public async Task<IActionResult> GetCertificateInfo()
    {
        var userId = GetUserId();
        if (userId == null)
            return Unauthorized(new { success = false, error = "Nieprawidłowy token" });

        var info = await _authService.GetCertificateInfoAsync(userId.Value);

        if (info == null)
            return NotFound(new { success = false, error = "Profil firmy nie istnieje" });

        return Ok(new { success = true, data = info });
    }

[HttpPost("ksef/connect")]
[Authorize]
public async Task<IActionResult> ConnectToKsef(
    [FromServices] IKSeFAuthService ksefAuthToken,
    [FromServices] IKSeFCertAuthService ksefAuthCert,
    [FromServices] KSeFSessionManager session)
{
    var userId = GetUserId();
    if (userId == null)
        return Unauthorized(new { success = false, error = "Nieprawidłowy token" });

    var user = await _authService.GetUserByIdAsync(userId.Value);
    if (user?.Company == null)
        return BadRequest(new { success = false, error = "Najpierw skonfiguruj firmę" });

    try
    {
        _logger.LogInformation("═══════════════════════════════════════════════════════════════");
        _logger.LogInformation("  🔗 KSEF CONNECT: User {UserId}, NIP {Nip}, Method {Method}", 
            userId, user.Company.Nip, user.Company.AuthMethod);
        _logger.LogInformation("═══════════════════════════════════════════════════════════════");

        Models.Responses.AuthResult result;

        if (user.Company.AuthMethod == "certificate")
        {
            _logger.LogInformation("🔐 --- Pobieranie certyfikatu z bazy ---");
            
            var certData = await _authService.GetDecryptedCertificateAsync(userId.Value);
            
            if (certData == null)
            {
                _logger.LogError("  ❌ Certyfikat NULL po deszyfrowaniu");
                return BadRequest(new { 
                    success = false, 
                    error = "Certyfikat nie jest dostępny. Prześlij certyfikat ponownie.",
                    tokenExpired = false
                });
            }

            var (cert, key, passwordRaw) = certData.Value;

            if (cert == null || key == null)
            {
                _logger.LogError("  ❌ Cert lub Key jest NULL");
                return BadRequest(new { 
                    success = false, 
                    error = "Dane certyfikatu są niepełne. Prześlij certyfikat ponownie.",
                    tokenExpired = false
                });
            }

            // ✅ TRIM HASŁA (usunięcie spacji przed/po)
            var password = passwordRaw?.Trim();
            if (passwordRaw != password)
            {
                _logger.LogWarning("  ⚠️ Password trimmed: '{Before}' → '{After}'", 
                    passwordRaw ?? "NULL", password ?? "NULL");
            }

            _logger.LogInformation("  ✅ Certyfikat pobrany z bazy");
            _logger.LogDebug("    📄 Cert length: {CertLen}", cert.Length);
            _logger.LogDebug("    🔑 Key length: {KeyLen}", key.Length);
            _logger.LogDebug("    🔒 Has password: {HasPwd} (length: {PwdLen})", 
                !string.IsNullOrEmpty(password), password?.Length ?? 0);

            // ✅ DEBUG: Sprawdź pierwsze znaki hasła (bez ujawnienia pełnej wartości)
            if (!string.IsNullOrEmpty(password))
            {
                var firstChar = (int)password[0];
                var lastChar = (int)password[password.Length - 1];
                _logger.LogDebug("    🔒 Password first char ASCII: {First}, last char ASCII: {Last}", 
                    firstChar, lastChar);
                
                // Sprawdź czy hasło zawiera nietypowe znaki
                var hasNonPrintable = password.Any(c => c < 32 || c > 126);
                if (hasNonPrintable)
                {
                    _logger.LogWarning("    ⚠️ Password contains non-printable characters!");
                }
            }

            _logger.LogInformation("🚀 --- Wywołanie KSeFCertAuthService ---");
            
            result = await ksefAuthCert.AuthenticateWithCertificateAsync(
                user.Company.Nip,
                cert,
                key,
                password,  // ← Trimmed password
                HttpContext.RequestAborted);
        }
        else
        {
            _logger.LogInformation("🔑 --- Pobieranie tokenu KSeF z bazy ---");
            
            var ksefToken = await _authService.GetDecryptedKsefTokenAsync(userId.Value);
            
            if (string.IsNullOrEmpty(ksefToken))
            {
                _logger.LogError("  ❌ Token KSeF NULL po deszyfrowaniu");
                return BadRequest(new { 
                    success = false, 
                    error = "Token KSeF nie jest skonfigurowany",
                    tokenExpired = false
                });
            }

            _logger.LogInformation("  ✅ Token pobrany z bazy (len={Len})", ksefToken.Length);
            _logger.LogInformation("🚀 --- Wywołanie KSeFAuthService ---");

            result = await ksefAuthToken.LoginAsync(
                user.Company.Nip, 
                ksefToken, 
                HttpContext.RequestAborted);
        }

        if (!result.Success)
        {
            _logger.LogError("  ❌ KSeF auth failed: {Error}", result.Error);
            
            return BadRequest(new
            {
                success = false,
                error = result.Error,
                tokenExpired = result.Error != null && (
                    result.Error.Contains("401") ||
                    result.Error.Contains("token") ||
                    result.Error.Contains("wygasł") ||
                    result.Error.Contains("unauthorized", StringComparison.OrdinalIgnoreCase))
            });
        }

        _logger.LogInformation("═══════════════════════════════════════════════════════════════");
        _logger.LogInformation("  ✅ KSEF CONNECTED: User {UserId}, NIP {Nip}, Method {Method}",
            userId, user.Company.Nip, user.Company.AuthMethod);
        _logger.LogInformation("  📋 RefNumber: {Ref}", result.ReferenceNumber);
        _logger.LogInformation("  ⏰ AccessToken valid until: {Until}", result.AccessTokenValidUntil);
        _logger.LogInformation("═══════════════════════════════════════════════════════════════");

        return Ok(new
        {
            success = true,
            message = $"Połączono z KSeF ({user.Company.AuthMethod})",
            data = new
            {
                nip = user.Company.Nip,
                authMethod = user.Company.AuthMethod,
                referenceNumber = result.ReferenceNumber,
                accessTokenValidUntil = result.AccessTokenValidUntil,
                refreshTokenValidUntil = result.RefreshTokenValidUntil
            }
        });
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "❌ KSeF connection failed for user {UserId}", userId);
        return BadRequest(new { 
            success = false, 
            error = "Błąd połączenia z KSeF: " + ex.Message,
            tokenExpired = false
        });
    }
}

    private int? GetUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier);
        if (claim == null) return null;
        if (int.TryParse(claim.Value, out var id)) return id;
        return null;
    }
}