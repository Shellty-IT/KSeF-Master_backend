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
    private readonly IUserAuthService _userAuthService;
    private readonly ICompanyService _companyService;
    private readonly ICertificateService _certificateService;
    private readonly IKSeFAuthService _ksefAuthService;
    private readonly IKSeFCertAuthService _ksefCertAuthService;
    private readonly KSeFSessionManager _sessionManager;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        IUserAuthService userAuthService,
        ICompanyService companyService,
        ICertificateService certificateService,
        IKSeFAuthService ksefAuthService,
        IKSeFCertAuthService ksefCertAuthService,
        KSeFSessionManager sessionManager,
        ILogger<AuthController> logger)
    {
        _userAuthService = userAuthService;
        _companyService = companyService;
        _certificateService = certificateService;
        _ksefAuthService = ksefAuthService;
        _ksefCertAuthService = ksefCertAuthService;
        _sessionManager = sessionManager;
        _logger = logger;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        var result = await _userAuthService.RegisterAsync(request);

        if (!result.Success)
            return BadRequest(new { success = false, error = result.Error });

        return Ok(new
        {
            success = true,
            data = new { token = result.Token, user = result.User }
        });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginAppRequest request)
    {
        var result = await _userAuthService.LoginAsync(request);

        if (!result.Success)
            return BadRequest(new { success = false, error = result.Error });

        return Ok(new
        {
            success = true,
            data = new { token = result.Token, user = result.User }
        });
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> GetMe()
    {
        var userId = GetUserId();
        if (userId == null)
            return Unauthorized(new { success = false, error = "Nieprawidłowy token" });

        var user = await _userAuthService.GetUserByIdAsync(userId.Value);
        if (user == null)
            return NotFound(new { success = false, error = "Użytkownik nie istnieje" });

        return Ok(new
        {
            success = true,
            data = new
            {
                user,
                isKsefConnected = _sessionManager.IsAuthenticated,
                needsCompanySetup = user.Company == null
            }
        });
    }

    [Authorize]
    [HttpPost("company")]
    public async Task<IActionResult> SetupCompany([FromBody] CompanySetupRequest request)
    {
        var userId = GetUserId();
        if (userId == null)
            return Unauthorized(new { success = false, error = "Nieprawidłowy token" });

        var result = await _companyService.SetupCompanyAsync(userId.Value, request);

        if (!result.Success)
            return BadRequest(new { success = false, error = result.Error });

        return Ok(new
        {
            success = true,
            message = "Firma została skonfigurowana",
            data = new { token = result.Token, user = result.User }
        });
    }

    [Authorize]
    [HttpPut("company/profile")]
    public async Task<IActionResult> UpdateCompanyProfile([FromBody] UpdateCompanyProfileRequest request)
    {
        var userId = GetUserId();
        if (userId == null)
            return Unauthorized(new { success = false, error = "Nieprawidłowy token" });

        var result = await _companyService.UpdateCompanyProfileAsync(userId.Value, request);

        if (!result.Success)
            return BadRequest(new { success = false, error = result.Error });

        return Ok(new
        {
            success = true,
            message = "Profil firmy zaktualizowany",
            data = new { user = result.User }
        });
    }

    [Authorize]
    [HttpPut("company/token")]
    public async Task<IActionResult> UpdateKsefToken([FromBody] UpdateKsefTokenRequest request)
    {
        var userId = GetUserId();
        if (userId == null)
            return Unauthorized(new { success = false, error = "Nieprawidłowy token" });

        var result = await _companyService.UpdateKsefTokenAsync(userId.Value, request);

        if (!result.Success)
            return BadRequest(new { success = false, error = result.Error });

        return Ok(new
        {
            success = true,
            message = "Token KSeF zaktualizowany",
            data = new { user = result.User }
        });
    }

    [Authorize]
    [HttpPut("company/environment")]
    public async Task<IActionResult> UpdateKsefEnvironment([FromBody] UpdateKsefEnvironmentRequest request)
    {
        var userId = GetUserId();
        if (userId == null)
            return Unauthorized(new { success = false, error = "Nieprawidłowy token" });

        var result = await _companyService.UpdateKsefEnvironmentAsync(userId.Value, request);

        if (!result.Success)
            return BadRequest(new { success = false, error = result.Error });

        return Ok(new
        {
            success = true,
            message = $"Środowisko KSeF zmienione na: {request.KsefEnvironment}",
            data = new { user = result.User }
        });
    }

    [Authorize]
    [HttpPost("company/certificate")]
    public async Task<IActionResult> UploadCertificate([FromBody] UploadCertificateRequest request)
    {
        var userId = GetUserId();
        if (userId == null)
            return Unauthorized(new { success = false, error = "Nieprawidłowy token" });

        var result = await _certificateService.UploadCertificateAsync(userId.Value, request);

        if (!result.Success)
            return BadRequest(new { success = false, error = result.Error });

        return Ok(new
        {
            success = true,
            message = "Certyfikat został przesłany",
            data = new { user = result.User }
        });
    }

    [Authorize]
    [HttpPut("company/auth-method")]
    public async Task<IActionResult> SwitchAuthMethod([FromBody] SwitchAuthMethodRequest request)
    {
        var userId = GetUserId();
        if (userId == null)
            return Unauthorized(new { success = false, error = "Nieprawidłowy token" });

        var result = await _certificateService.SwitchAuthMethodAsync(userId.Value, request);

        if (!result.Success)
            return BadRequest(new { success = false, error = result.Error });

        return Ok(new
        {
            success = true,
            message = $"Metoda uwierzytelniania zmieniona na: {request.AuthMethod}",
            data = new { user = result.User }
        });
    }

    [Authorize]
    [HttpDelete("company/certificate")]
    public async Task<IActionResult> DeleteCertificate()
    {
        var userId = GetUserId();
        if (userId == null)
            return Unauthorized(new { success = false, error = "Nieprawidłowy token" });

        var result = await _certificateService.DeleteCertificateAsync(userId.Value);

        if (!result.Success)
            return BadRequest(new { success = false, error = result.Error });

        return Ok(new
        {
            success = true,
            message = "Certyfikat został usunięty",
            data = new { user = result.User }
        });
    }

    [Authorize]
    [HttpGet("company/certificate/info")]
    public async Task<IActionResult> GetCertificateInfo()
    {
        var userId = GetUserId();
        if (userId == null)
            return Unauthorized(new { success = false, error = "Nieprawidłowy token" });

        var info = await _certificateService.GetCertificateInfoAsync(userId.Value);

        if (info == null)
            return NotFound(new { success = false, error = "Brak informacji o certyfikacie" });

        return Ok(new { success = true, data = info });
    }

    [Authorize]
    [HttpPost("ksef/connect")]
    public async Task<IActionResult> ConnectToKsef()
    {
        var userId = GetUserId();
        if (userId == null)
            return Unauthorized(new { success = false, error = "Nieprawidłowy token" });

        var user = await _userAuthService.GetUserByIdAsync(userId.Value);
        if (user?.Company == null)
            return BadRequest(new { success = false, error = "Najpierw skonfiguruj firmę" });

        var authMethod = user.Company.AuthMethod;
        var environment = user.Company.KsefEnvironment;

        _logger.LogInformation("Connecting to KSeF for user {UserId}, method: {Method}, environment: {Env}",
            userId, authMethod, environment);

        if (authMethod == "certificate")
        {
            var certData = await _certificateService.GetDecryptedCertificateAsync(userId.Value);
            if (certData == null)
                return BadRequest(new { success = false, error = "Certyfikat nie jest skonfigurowany lub jest nieprawidłowy" });

            var authResult = await _ksefCertAuthService.AuthenticateWithCertificateAsync(
                user.Company.Nip,
                certData.Value.cert!,
                certData.Value.key!,
                certData.Value.password,
                environment);

            if (!authResult.Success)
            {
                _logger.LogError("KSeF cert auth failed for user {UserId}: {Error}", userId, authResult.Error);
                return BadRequest(new { success = false, error = authResult.Error ?? "Nie udało się połączyć z KSeF" });
            }

            return Ok(new
            {
                success = true,
                message = $"Połączono z KSeF (certyfikat, środowisko: {environment})",
                data = new { referenceNumber = authResult.ReferenceNumber, environment }
            });
        }
        else
        {
            var ksefToken = await _companyService.GetDecryptedKsefTokenAsync(userId.Value);
            if (string.IsNullOrEmpty(ksefToken))
                return BadRequest(new { success = false, error = "Token KSeF nie jest skonfigurowany" });

            var authResult = await _ksefAuthService.LoginAsync(user.Company.Nip, ksefToken, environment);

            if (!authResult.Success)
            {
                _logger.LogError("KSeF token auth failed for user {UserId}: {Error}", userId, authResult.Error);
                return BadRequest(new { success = false, error = authResult.Error ?? "Nie udało się połączyć z KSeF" });
            }

            return Ok(new
            {
                success = true,
                message = $"Połączono z KSeF (token, środowisko: {environment})",
                data = new { referenceNumber = authResult.ReferenceNumber, environment }
            });
        }
    }

    [Authorize]
    [HttpPost("ksef/disconnect")]
    public IActionResult DisconnectFromKsef()
    {
        var userId = GetUserId();
        if (userId == null)
            return Unauthorized(new { success = false, error = "Nieprawidłowy token" });

        _sessionManager.ClearAuthSession();

        _logger.LogInformation("User {UserId} disconnected from KSeF", userId);

        return Ok(new { success = true, message = "Odłączono od KSeF" });
    }

    private int? GetUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(claim, out var id) ? id : null;
    }
}