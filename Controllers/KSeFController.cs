using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FluentValidation;
using KSeF.Backend.Models.Requests;
using KSeF.Backend.Services;
using KSeF.Backend.Services.Interfaces;

namespace KSeF.Backend.Controllers;

[ApiController]
[Route("api/ksef")]
[Produces("application/json")]
public class KSeFController : ControllerBase
{
    private readonly IKSeFAuthService _authService;
    private readonly IKSeFInvoiceService _invoiceService;
    private readonly KSeFSessionManager _session;
    private readonly ILogger<KSeFController> _logger;

    public KSeFController(
        IKSeFAuthService authService,
        IKSeFInvoiceService invoiceService,
        KSeFSessionManager session,
        ILogger<KSeFController> logger)
    {
        _authService = authService;
        _invoiceService = invoiceService;
        _session = session;
        _logger = logger;
    }

    [HttpGet("status")]
    public IActionResult GetStatus()
    {
        return Ok(new
        {
            server = "OK",
            timestamp = DateTime.UtcNow,
            version = "2.0.0",
            session = _session.GetSessionInfo()
        });
    }

    [HttpPost("login")]
    [Authorize]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequest request,
        [FromServices] IValidator<LoginRequest> validator,
        CancellationToken ct)
    {
        var validationResult = await validator.ValidateAsync(request, ct);
        if (!validationResult.IsValid)
            return BadRequest(new
            {
                success = false,
                error = "Błędy walidacji",
                details = validationResult.Errors.Select(e => e.ErrorMessage)
            });

        _logger.LogInformation("KSeF login request for NIP: {Nip}", request.Nip);

        try
        {
            var result = await _authService.LoginAsync(request.Nip, request.KsefToken, "Test", ct);

            if (!result.Success)
            {
                _logger.LogWarning("KSeF login failed: {Error}", result.Error);
                return BadRequest(new { success = false, error = result.Error });
            }

            return Ok(new
            {
                success = true,
                message = "Zalogowano pomyślnie do KSeF",
                data = new
                {
                    nip = request.Nip,
                    referenceNumber = result.ReferenceNumber,
                    accessTokenValidUntil = result.AccessTokenValidUntil,
                    refreshTokenValidUntil = result.RefreshTokenValidUntil
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during KSeF login");
            return StatusCode(500, new { success = false, error = "Wystąpił nieoczekiwany błąd" });
        }
    }

    [HttpPost("logout")]
    [Authorize]
    public IActionResult Logout()
    {
        var nip = _session.Nip;
        _authService.Logout();
        _logger.LogInformation("KSeF logout for NIP: {Nip}", nip ?? "none");
        return Ok(new { success = true, message = "Wylogowano pomyślnie" });
    }

    [HttpPost("invoices")]
    [Authorize]
    public async Task<IActionResult> GetInvoices(
        [FromBody] InvoiceQueryRequest request,
        [FromServices] IValidator<InvoiceQueryRequest> validator,
        CancellationToken ct)
    {
        var validationResult = await validator.ValidateAsync(request, ct);
        if (!validationResult.IsValid)
            return BadRequest(new
            {
                success = false,
                error = "Błędy walidacji zapytania",
                details = validationResult.Errors.Select(e => e.ErrorMessage)
            });

        _logger.LogInformation("GetInvoices: SubjectType={Type}, DateType={DateType}",
            request.SubjectType, request.DateRange?.DateType);

        try
        {
            var result = await _invoiceService.GetInvoicesAsync(request, ct);

            _logger.LogInformation("GetInvoices: zwrócono {Count} faktur", result.TotalCount);

            return Ok(new { success = true, data = result });
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning("GetInvoices unauthorized: {Message}", ex.Message);
            return Unauthorized(new { success = false, error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetInvoices error");
            return BadRequest(new { success = false, error = ex.Message });
        }
    }

    [HttpGet("invoices/stats")]
    [Authorize]
    public async Task<IActionResult> GetInvoiceStats([FromQuery] int months = 3, CancellationToken ct = default)
    {
        _logger.LogInformation("GetInvoiceStats: months={Months}", months);

        if (months < 1) months = 1;
        if (months > 12) months = 12;

        try
        {
            var stats = await _invoiceService.GetInvoiceStatsAsync(months, ct);
            return Ok(new { success = true, data = stats });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { success = false, error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetInvoiceStats error");
            return BadRequest(new { success = false, error = ex.Message });
        }
    }

    [HttpPost("session/open")]
    [Authorize]
    public async Task<IActionResult> OpenSession(CancellationToken ct)
    {
        try
        {
            var result = await _invoiceService.OpenOnlineSessionAsync(ct);

            if (!result.Success)
                return BadRequest(new { success = false, error = result.Error });

            return Ok(new
            {
                success = true,
                message = "Sesja otwarta",
                data = new
                {
                    sessionReferenceNumber = result.SessionReferenceNumber,
                    validUntil = result.ValidUntil
                }
            });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { success = false, error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OpenSession error");
            return BadRequest(new { success = false, error = ex.Message });
        }
    }

    [HttpPost("session/close")]
    [Authorize]
    public IActionResult CloseSession()
    {
        _session.ClearOnlineSession();
        return Ok(new { success = true, message = "Sesja wysyłkowa zamknięta" });
    }

    [HttpPost("invoice/send")]
    [Authorize]
    public async Task<IActionResult> SendInvoice(
        [FromBody] CreateInvoiceRequest request,
        [FromServices] IValidator<CreateInvoiceRequest> validator,
        CancellationToken ct)
    {
        var validationResult = await validator.ValidateAsync(request, ct);
        if (!validationResult.IsValid)
            return BadRequest(new
            {
                success = false,
                error = "Błędy walidacji",
                details = validationResult.Errors.Select(e => e.ErrorMessage)
            });

        _logger.LogInformation("SendInvoice: {Number}", request.InvoiceNumber);

        try
        {
            var result = await _invoiceService.SendInvoiceAsync(request, ct);

            if (!result.Success)
                return BadRequest(new { success = false, error = result.Error });

            return Ok(new
            {
                success = true,
                message = "Faktura wysłana pomyślnie do KSeF",
                data = new
                {
                    elementReferenceNumber = result.ElementReferenceNumber,
                    processingCode = result.ProcessingCode,
                    processingDescription = result.ProcessingDescription,
                    invoiceHash = result.InvoiceHash
                }
            });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { success = false, error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SendInvoice error");
            return BadRequest(new { success = false, error = ex.Message });
        }
    }

    [HttpPost("invoice/pdf")]
    [Authorize]
    public IActionResult GeneratePdf(
        [FromBody] GeneratePdfRequest request,
        [FromServices] IPdfGeneratorService pdfService)
    {
        _logger.LogInformation("GeneratePdf: {Number}", request.InvoiceNumber);

        try
        {
            if (string.IsNullOrEmpty(request.InvoiceHash))
                return BadRequest(new { success = false, error = "Hash faktury jest wymagany" });

            var pdfBytes = pdfService.GeneratePdf(request);
            var fileName = SanitizeFileName(request.InvoiceNumber ?? "faktura") + ".pdf";
            return File(pdfBytes, "application/pdf", fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GeneratePdf error");
            return BadRequest(new { success = false, error = ex.Message });
        }
    }

    [HttpGet("invoice/{ksefNumber}")]
    [Authorize]
    public async Task<IActionResult> GetInvoiceDetails(string ksefNumber, CancellationToken ct)
    {
        _logger.LogInformation("GetInvoiceDetails: {KsefNumber}", ksefNumber);

        if (string.IsNullOrWhiteSpace(ksefNumber))
            return BadRequest(new { success = false, error = "Numer KSeF jest wymagany" });

        try
        {
            var result = await _invoiceService.GetInvoiceDetailsAsync(ksefNumber, ct);

            if (!result.Success)
                return BadRequest(new { success = false, error = result.Error });

            return Ok(new { success = true, data = result });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { success = false, error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetInvoiceDetails error");
            return BadRequest(new { success = false, error = ex.Message });
        }
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Join("-", name.Split(invalid, StringSplitOptions.RemoveEmptyEntries));
    }
}