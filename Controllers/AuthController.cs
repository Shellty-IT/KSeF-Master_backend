// Controllers/AuthController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using KSeF.Backend.Models.Requests;
using KSeF.Backend.Services;
using KSeF.Backend.Services.Interfaces;

namespace KSeF.Backend.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly IKSeFAuthService _ksefAuthService;
    private readonly IKSeFCertAuthService _ksefCertAuthService;
    private readonly KSeFSessionManager _sessionManager;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        IAuthService authService,
        IKSeFAuthService ksefAuthService,
        IKSeFCertAuthService ksefCertAuthService,
        KSeFSessionManager sessionManager,
        ILogger<AuthController> logger)
    {
        _authService = authService;
        _ksefAuthService = ksefAuthService;
        _ksefCertAuthService = ksefCertAuthService;
        _sessionManager = sessionManager;
        _logger = logger;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        var result = await _authService.RegisterAsync(request);

        if (!result.Success)
            return BadRequest(new { success = false, error = result.Error });

        return Ok(new
        {
            success = true,
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
        var result = await _authService.LoginAsync(request);

        if (!result.Success)
            return BadRequest(new { success = false, error = result.Error });

        return Ok(new
        {
            success = true,
            data = new
            {
                token = result.Token,
                user = result.User
            }
        });
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> GetMe()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (userIdClaim == null || !int.TryParse(userIdClaim, out var userId))
            return Unauthorized(new { success = false, error = "Nieprawidłowy token" });

        var user = await _authService.GetUserByIdAsync(userId);
        if (user == null)
            return NotFound(new { success = false, error = "Użytkownik nie istnieje" });

        var isKsefConnected = _sessionManager.IsAuthenticated;
        var needsCompanySetup = user.Company == null;

        return Ok(new
        {
            success = true,
            data = new
            {
                user = user,
                isKsefConnected = isKsefConnected,
                needsCompanySetup = needsCompanySetup
            }
        });
    }

    [Authorize]
    [HttpPost("company")]
    public async Task<IActionResult> SetupCompany([FromBody] CompanySetupRequest request)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (userIdClaim == null || !int.TryParse(userIdClaim, out var userId))
            return Unauthorized(new { success = false, error = "Nieprawidłowy token" });

        var result = await _authService.SetupCompanyAsync(userId, request);

        if (!result.Success)
            return BadRequest(new { success = false, error = result.Error });

        return Ok(new
        {
            success = true,
            message = "Firma została skonfigurowana",
            data = new
            {
                token = result.Token,
                user = result.User
            }
        });
    }

    [Authorize]
    [HttpPut("company/profile")]
    public async Task<IActionResult> UpdateCompanyProfile([FromBody] UpdateCompanyProfileRequest request)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (userIdClaim == null || !int.TryParse(userIdClaim, out var userId))
            return Unauthorized(new { success = false, error = "Nieprawidłowy token" });

        var result = await _authService.UpdateCompanyProfileAsync(userId, request);

        if (!result.Success)
            return BadRequest(new { success = false, error = result.Error });

        return Ok(new
        {
            success = true,
            message = "Profil firmy zaktualizowany",
            data = new
            {
                user = result.User
            }
        });
    }

    [Authorize]
    [HttpPut("company/token")]
    public async Task<IActionResult> UpdateKsefToken([FromBody] UpdateKsefTokenRequest request)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (userIdClaim == null || !int.TryParse(userIdClaim, out var userId))
            return Unauthorized(new { success = false, error = "Nieprawidłowy token" });

        var result = await _authService.UpdateKsefTokenAsync(userId, request);

        if (!result.Success)
            return BadRequest(new { success = false, error = result.Error });

        return Ok(new
        {
            success = true,
            message = "Token KSeF zaktualizowany",
            data = new
            {
                user = result.User
            }
        });
    }

    [Authorize]
    [HttpPut("company/environment")]
    public async Task<IActionResult> UpdateKsefEnvironment([FromBody] UpdateKsefEnvironmentRequest request)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (userIdClaim == null || !int.TryParse(userIdClaim, out var userId))
            return Unauthorized(new { success = false, error = "Nieprawidłowy token" });

        var result = await _authService.UpdateKsefEnvironmentAsync(userId, request);

        if (!result.Success)
            return BadRequest(new { success = false, error = result.Error });

        _logger.LogInformation("⚙️ User {UserId} switched KSeF environment to {Env}",
            userId, request.KsefEnvironment);

        return Ok(new
        {
            success = true,
            message = $"Środowisko KSeF zmienione na: {request.KsefEnvironment}",
            data = new
            {
                user = result.User
            }
        });
    }

    [Authorize]
    [HttpPost("company/certificate")]
    public async Task<IActionResult> UploadCertificate([FromBody] UploadCertificateRequest request)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (userIdClaim == null || !int.TryParse(userIdClaim, out var userId))
            return Unauthorized(new { success = false, error = "Nieprawidłowy token" });

        var result = await _authService.UploadCertificateAsync(userId, request);

        if (!result.Success)
            return BadRequest(new { success = false, error = result.Error });

        return Ok(new
        {
            success = true,
            message = "Certyfikat został przesłany",
            data = new
            {
                user = result.User
            }
        });
    }

    [Authorize]
    [HttpPut("company/auth-method")]
    public async Task<IActionResult> SwitchAuthMethod([FromBody] SwitchAuthMethodRequest request)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (userIdClaim == null || !int.TryParse(userIdClaim, out var userId))
            return Unauthorized(new { success = false, error = "Nieprawidłowy token" });

        var result = await _authService.SwitchAuthMethodAsync(userId, request);

        if (!result.Success)
            return BadRequest(new { success = false, error = result.Error });

        return Ok(new
        {
            success = true,
            message = $"Metoda uwierzytelniania zmieniona na: {request.AuthMethod}",
            data = new
            {
                user = result.User
            }
        });
    }

    [Authorize]
    [HttpDelete("company/certificate")]
    public async Task<IActionResult> DeleteCertificate()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (userIdClaim == null || !int.TryParse(userIdClaim, out var userId))
            return Unauthorized(new { success = false, error = "Nieprawidłowy token" });

        var result = await _authService.DeleteCertificateAsync(userId);

        if (!result.Success)
            return BadRequest(new { success = false, error = result.Error });

        return Ok(new
        {
            success = true,
            message = "Certyfikat został usunięty",
            data = new
            {
                user = result.User
            }
        });
    }

    [Authorize]
    [HttpGet("company/certificate/info")]
    public async Task<IActionResult> GetCertificateInfo()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (userIdClaim == null || !int.TryParse(userIdClaim, out var userId))
            return Unauthorized(new { success = false, error = "Nieprawidłowy token" });

        var info = await _authService.GetCertificateInfoAsync(userId);

        if (info == null)
            return NotFound(new { success = false, error = "Brak informacji o certyfikacie" });

        return Ok(new { success = true, data = info });
    }
    
    [Authorize]
    [HttpPost("ksef/connect")]
    public async Task<IActionResult> ConnectToKsef()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (userIdClaim == null || !int.TryParse(userIdClaim, out var userId))
            return Unauthorized(new { success = false, error = "Nieprawidłowy token" });

        var user = await _authService.GetUserByIdAsync(userId);
        if (user?.Company == null)
            return BadRequest(new { success = false, error = "Najpierw skonfiguruj firmę" });

        var authMethod = user.Company.AuthMethod;
        var ksefEnvironment = user.Company.KsefEnvironment;

        _logger.LogInformation(
            "🔗 Connecting to KSeF for user {UserId}, method: {Method}, environment: {Env}",
            userId, authMethod, ksefEnvironment);

        if (authMethod == "certificate")
        {
            var certData = await _authService.GetDecryptedCertificateAsync(userId);
            if (certData == null)
                return BadRequest(new
                {
                    success = false,
                    error = "Certyfikat nie jest skonfigurowany lub jest nieprawidłowy"
                });

            var password = certData.Value.password?.Trim();

            var authResult = await _ksefCertAuthService.AuthenticateWithCertificateAsync(
                user.Company.Nip,
                certData.Value.cert,
                certData.Value.key,
                password,
                ksefEnvironment
            );

            if (!authResult.Success)
            {
                _logger.LogError("❌ KSeF cert auth failed for user {UserId}: {Error}",
                    userId, authResult.Error);
                return BadRequest(new
                {
                    success = false,
                    error = authResult.Error ?? "Nie udało się połączyć z KSeF"
                });
            }

            _logger.LogInformation(
                "✅ KSeF connected (certificate) for user {UserId}, environment: {Env}",
                userId, ksefEnvironment);

            return Ok(new
            {
                success = true,
                message = $"Połączono z KSeF (certyfikat, środowisko: {ksefEnvironment})",
                data = new
                {
                    referenceNumber = authResult.ReferenceNumber,
                    environment = ksefEnvironment
                }
            });
        }
        else
        {
            var ksefToken = await _authService.GetDecryptedKsefTokenAsync(userId);
            if (string.IsNullOrEmpty(ksefToken))
                return BadRequest(new
                {
                    success = false,
                    error = "Token KSeF nie jest skonfigurowany"
                });

            var authResult = await _ksefAuthService.LoginAsync(
                user.Company.Nip,
                ksefToken,
                ksefEnvironment
            );

            if (!authResult.Success)
            {
                _logger.LogError("❌ KSeF token auth failed for user {UserId}: {Error}",
                    userId, authResult.Error);
                return BadRequest(new
                {
                    success = false,
                    error = authResult.Error ?? "Nie udało się połączyć z KSeF"
                });
            }

            _logger.LogInformation(
                "✅ KSeF connected (token) for user {UserId}, environment: {Env}",
                userId, ksefEnvironment);

            return Ok(new
            {
                success = true,
                message = $"Połączono z KSeF (token, środowisko: {ksefEnvironment})",
                data = new
                {
                    referenceNumber = authResult.ReferenceNumber,
                    environment = ksefEnvironment
                }
            });
        }
    }
    
    [Authorize]
    [HttpPost("ksef/disconnect")]
    public IActionResult DisconnectFromKsef()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (userIdClaim == null || !int.TryParse(userIdClaim, out var userId))
            return Unauthorized(new { success = false, error = "Nieprawidłowy token" });

        _sessionManager.ClearAuthSession();

        _logger.LogInformation("🔌 User {UserId} disconnected from KSeF", userId);

        return Ok(new { success = true, message = "Odłączono od KSeF" });
    }
}