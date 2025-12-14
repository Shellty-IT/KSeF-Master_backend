using Microsoft.AspNetCore.Mvc;
using KSeF.Backend.Models.Requests;
using KSeF.Backend.Models.Responses;
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

    // ═══════════════════════════════════════════════════════════════
    // STATUS
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Status serwera i sesji
    /// </summary>
    [HttpGet("status")]
    public IActionResult GetStatus()
    {
        return Ok(new
        {
            server = "OK",
            timestamp = DateTime.UtcNow,
            environment = "KSeF Test (ksef-test.mf.gov.pl)",
            version = "1.0.0",
            session = _session.GetSessionInfo()
        });
    }

    // ═══════════════════════════════════════════════════════════════
    // LOGOWANIE / WYLOGOWANIE
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Logowanie do KSeF
    /// </summary>
    /// <remarks>
    /// Użytkownik podaje NIP i Token KSeF wygenerowany w oficjalnej aplikacji.
    /// Backend wykonuje pełny flow uwierzytelniania i zapisuje sesję.
    /// 
    /// Przykład:
    /// ```json
    /// {
    ///     "nip": "5252161248",
    ///     "ksefToken": "20251210-EC-4FAFC0A000-554D466CBC-4F|nip-5252161248|hash..."
    /// }
    /// ```
    /// </remarks>
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        _logger.LogInformation("=== LOGIN REQUEST ===");
        _logger.LogInformation("NIP: {Nip}", request.Nip);

        // Walidacja
        if (string.IsNullOrWhiteSpace(request.Nip))
            return BadRequest(new { success = false, error = "NIP jest wymagany" });

        if (request.Nip.Length != 10 || !request.Nip.All(char.IsDigit))
            return BadRequest(new { success = false, error = "NIP musi mieć dokładnie 10 cyfr" });

        if (string.IsNullOrWhiteSpace(request.KsefToken))
            return BadRequest(new { success = false, error = "Token KSeF jest wymagany" });

        // Podstawowa walidacja formatu tokenu
        if (!request.KsefToken.Contains("|"))
            return BadRequest(new { success = false, error = "Nieprawidłowy format tokenu KSeF" });

        try
        {
            var result = await _authService.LoginAsync(request.Nip, request.KsefToken, ct);

            if (!result.Success)
            {
                _logger.LogWarning("Login failed: {Error}", result.Error);
                return BadRequest(new
                {
                    success = false,
                    error = result.Error
                });
            }

            _logger.LogInformation("Login successful for NIP: {Nip}", request.Nip);

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
            _logger.LogError(ex, "Unexpected error during login");
            return StatusCode(500, new
            {
                success = false,
                error = "Wystąpił nieoczekiwany błąd podczas logowania"
            });
        }
    }

    /// <summary>
    /// Wylogowanie z KSeF
    /// </summary>
    [HttpPost("logout")]
    public IActionResult Logout()
    {
        var nip = _session.Nip;
        _authService.Logout();

        _logger.LogInformation("Logged out NIP: {Nip}", nip ?? "none");

        return Ok(new
        {
            success = true,
            message = "Wylogowano pomyślnie"
        });
    }

    // ═══════════════════════════════════════════════════════════════
    // FAKTURY - POBIERANIE
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Pobierz listę faktur
    /// </summary>
    /// <remarks>
    /// Wymaga zalogowania. Zwraca faktury w podanym zakresie dat.
    /// 
    /// Przykład:
    /// ```json
    /// {
    ///     "subjectType": "Subject1",
    ///     "dateRange": {
    ///         "dateType": "PermanentStorage",
    ///         "from": "2025-11-01T00:00:00Z",
    ///         "to": "2025-12-31T23:59:59Z"
    ///     }
    /// }
    /// ```
    /// 
    /// SubjectType:
    /// - Subject1 = faktury wystawione (sprzedaż)
    /// - Subject2 = faktury otrzymane (zakupy)
    /// </remarks>
    [HttpPost("invoices")]
    public async Task<IActionResult> GetInvoices([FromBody] InvoiceQueryRequest request, CancellationToken ct)
    {
        _logger.LogInformation("=== GET INVOICES REQUEST ===");

        try
        {
            var result = await _invoiceService.GetInvoicesAsync(request, ct);

            return Ok(new
            {
                success = true,
                data = result
            });
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning("Unauthorized: {Message}", ex.Message);
            return Unauthorized(new
            {
                success = false,
                error = ex.Message
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting invoices");
            return BadRequest(new
            {
                success = false,
                error = ex.Message
            });
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // FAKTURY - WYSYŁANIE
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Otwórz sesję do wysyłki faktur
    /// </summary>
    /// <remarks>
    /// Wymaga zalogowania. Tworzy sesję interaktywną w KSeF.
    /// Jeśli sesja już istnieje i jest ważna, zwraca jej dane.
    /// 
    /// Sesja jest wymagana do wysyłania faktur.
    /// </remarks>
    [HttpPost("session/open")]
    public async Task<IActionResult> OpenSession(CancellationToken ct)
    {
        _logger.LogInformation("=== OPEN SESSION REQUEST ===");

        try
        {
            var result = await _invoiceService.OpenOnlineSessionAsync(ct);

            if (!result.Success)
            {
                return BadRequest(new
                {
                    success = false,
                    error = result.Error
                });
            }

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
            _logger.LogError(ex, "Error opening session");
            return BadRequest(new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// Zamknij sesję wysyłkową
    /// </summary>
    [HttpPost("session/close")]
    public IActionResult CloseSession()
    {
        _session.ClearOnlineSession();

        return Ok(new
        {
            success = true,
            message = "Sesja wysyłkowa zamknięta"
        });
    }

    /// <summary>
    /// Wyślij fakturę do KSeF
    /// </summary>
    /// <remarks>
    /// Wymaga zalogowania. Backend automatycznie:
    /// 1. Otwiera sesję jeśli nie istnieje
    /// 2. Generuje XML faktury na podstawie przekazanych danych
    /// 3. Szyfruje fakturę (AES-256-CBC)
    /// 4. Wysyła do KSeF
    /// 
    /// Przykład minimalny:
    /// ```json
    /// {
    ///     "invoiceNumber": "FV/2025/001",
    ///     "issueDate": "2025-12-13",
    ///     "saleDate": "2025-12-13",
    ///     "seller": {
    ///         "nip": "5252161248",
    ///         "name": "Firma Sprzedawcy Sp. z o.o.",
    ///         "addressLine1": "ul. Testowa 1, 00-001 Warszawa"
    ///     },
    ///     "buyer": {
    ///         "nip": "8521021463",
    ///         "name": "Firma Nabywcy S.A.",
    ///         "addressLine1": "ul. Przykładowa 2, 00-002 Warszawa"
    ///     },
    ///     "items": [
    ///         {
    ///             "name": "Usługa programistyczna",
    ///             "unit": "godz.",
    ///             "quantity": 10,
    ///             "unitPriceNet": 150.00,
    ///             "vatRate": "23"
    ///         }
    ///     ]
    /// }
    /// ```
    /// </remarks>
    [HttpPost("invoice/send")]
    public async Task<IActionResult> SendInvoice([FromBody] CreateInvoiceRequest request, CancellationToken ct)
    {
        _logger.LogInformation("=== SEND INVOICE REQUEST ===");
        _logger.LogInformation("Invoice number: {Number}", request.InvoiceNumber);

        // Walidacja
        var validationErrors = ValidateInvoiceRequest(request);
        if (validationErrors.Count > 0)
        {
            return BadRequest(new
            {
                success = false,
                error = "Błędy walidacji",
                details = validationErrors
            });
        }

        try
        {
            var result = await _invoiceService.SendInvoiceAsync(request, ct);

            if (!result.Success)
            {
                return BadRequest(new
                {
                    success = false,
                    error = result.Error
                });
            }

            return Ok(new
            {
                success = true,
                message = "Faktura wysłana pomyślnie do KSeF",
                data = new
                {
                    elementReferenceNumber = result.ElementReferenceNumber,
                    processingCode = result.ProcessingCode,
                    processingDescription = result.ProcessingDescription
                }
            });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { success = false, error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending invoice");
            return BadRequest(new { success = false, error = ex.Message });
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // HELPERS
    // ═══════════════════════════════════════════════════════════════

    private List<string> ValidateInvoiceRequest(CreateInvoiceRequest request)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(request.InvoiceNumber))
            errors.Add("Numer faktury jest wymagany");

        if (string.IsNullOrWhiteSpace(request.IssueDate))
            errors.Add("Data wystawienia jest wymagana");

        if (string.IsNullOrWhiteSpace(request.SaleDate))
            errors.Add("Data sprzedaży jest wymagana");

        // Seller
        if (request.Seller == null)
        {
            errors.Add("Dane sprzedawcy są wymagane");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(request.Seller.Nip))
                errors.Add("NIP sprzedawcy jest wymagany");
            if (string.IsNullOrWhiteSpace(request.Seller.Name))
                errors.Add("Nazwa sprzedawcy jest wymagana");
            if (string.IsNullOrWhiteSpace(request.Seller.AddressLine1))
                errors.Add("Adres sprzedawcy jest wymagany");
        }

        // Buyer
        if (request.Buyer == null)
        {
            errors.Add("Dane nabywcy są wymagane");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(request.Buyer.Nip))
                errors.Add("NIP nabywcy jest wymagany");
            if (string.IsNullOrWhiteSpace(request.Buyer.Name))
                errors.Add("Nazwa nabywcy jest wymagana");
            if (string.IsNullOrWhiteSpace(request.Buyer.AddressLine1))
                errors.Add("Adres nabywcy jest wymagany");
        }

        // Items
        if (request.Items == null || request.Items.Count == 0)
        {
            errors.Add("Faktura musi zawierać przynajmniej jedną pozycję");
        }
        else
        {
            for (int i = 0; i < request.Items.Count; i++)
            {
                var item = request.Items[i];
                if (string.IsNullOrWhiteSpace(item.Name))
                    errors.Add($"Pozycja {i + 1}: nazwa jest wymagana");
                if (item.Quantity <= 0)
                    errors.Add($"Pozycja {i + 1}: ilość musi być większa od 0");
                if (item.UnitPriceNet < 0)
                    errors.Add($"Pozycja {i + 1}: cena nie może być ujemna");
            }
        }

        return errors;
    }
}