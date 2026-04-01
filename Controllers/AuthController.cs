// Controllers/AuthController.cs
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using KSeF.Backend.Models.Requests;
using KSeF.Backend.Models.Responses;
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

    [HttpPost("ksef/connect")]
    [Authorize]
    public async Task<IActionResult> ConnectToKsef(
        [FromServices] IKSeFAuthService ksefAuth,
        [FromServices] KSeFSessionManager session)
    {
        var userId = GetUserId();
        if (userId == null)
            return Unauthorized(new { success = false, error = "Nieprawidłowy token" });

        var user = await _authService.GetUserByIdAsync(userId.Value);
        if (user?.Company == null)
            return BadRequest(new { success = false, error = "Najpierw skonfiguruj firmę" });

        var ksefToken = await _authService.GetDecryptedKsefTokenAsync(userId.Value);
        if (string.IsNullOrEmpty(ksefToken))
            return BadRequest(new { success = false, error = "Token KSeF nie jest skonfigurowany lub nie można go odszyfrować" });

        try
        {
            var result = await ksefAuth.LoginAsync(user.Company.Nip, ksefToken, CancellationToken.None);

            if (!result.Success)
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

            _logger.LogInformation("KSeF connected for user {UserId}, NIP {Nip}", userId, user.Company.Nip);

            return Ok(new
            {
                success = true,
                message = "Połączono z KSeF",
                data = new
                {
                    nip = user.Company.Nip,
                    referenceNumber = result.ReferenceNumber,
                    accessTokenValidUntil = result.AccessTokenValidUntil,
                    refreshTokenValidUntil = result.RefreshTokenValidUntil
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "KSeF connection failed for user {UserId}", userId);
            return BadRequest(new { success = false, error = "Błąd połączenia z KSeF: " + ex.Message });
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