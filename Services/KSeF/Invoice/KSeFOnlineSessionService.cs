using System.Text;
using System.Text.Json;
using KSeF.Backend.Infrastructure.KSeF;
using KSeF.Backend.Models.Responses.Certificate;
using KSeF.Backend.Models.Responses.Common;
using KSeF.Backend.Models.Responses.Session;
using KSeF.Backend.Services.Interfaces.KSeF;
using KSeF.Backend.Services.KSeF.Session;

namespace KSeF.Backend.Services.KSeF.Invoice;

public class KSeFOnlineSessionService : IKSeFOnlineSessionService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IKSeFEnvironmentService _environmentService;
    private readonly IKSeFAuthService _authService;
    private readonly IKSeFCryptoService _cryptoService;
    private readonly KSeFSessionManager _session;
    private readonly ILogger<KSeFOnlineSessionService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public KSeFOnlineSessionService(
        IHttpClientFactory httpClientFactory,
        IKSeFEnvironmentService environmentService,
        IKSeFAuthService authService,
        IKSeFCryptoService cryptoService,
        KSeFSessionManager session,
        ILogger<KSeFOnlineSessionService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _environmentService = environmentService;
        _authService = authService;
        _cryptoService = cryptoService;
        _session = session;
        _logger = logger;
    }

    public async Task<SessionResult> OpenOnlineSessionAsync(CancellationToken ct = default)
    {
        if (!_session.IsAuthenticated)
            throw new UnauthorizedAccessException("Brak aktywnej sesji KSeF");

        await _authService.RefreshTokenIfNeededAsync(ct);

        if (_session.HasActiveOnlineSession)
        {
            _logger.LogInformation("Reusing existing online session: {Ref}", _session.SessionReferenceNumber);
            return new SessionResult
            {
                Success = true,
                SessionReferenceNumber = _session.SessionReferenceNumber,
                ValidUntil = _session.SessionValidUntil ?? DateTime.UtcNow.AddHours(1)
            };
        }

        try
        {
            var certificates = _session.GetCachedCertificates() ?? await FetchCertificatesAsync(ct);

            var symCert = certificates.FirstOrDefault(c =>
                c.Usage?.Any(u => u.Contains("SymmetricKeyEncryption", StringComparison.OrdinalIgnoreCase)) == true)
                ?? certificates.FirstOrDefault();

            if (symCert == null)
                return Fail("Brak certyfikatu do szyfrowania klucza symetrycznego");

            var (aesKey, iv) = _cryptoService.GenerateAesKeyAndIv();
            var encryptedSymmetricKey = _cryptoService.EncryptAesKey(aesKey, symCert.Certificate);

            var requestBody = new
            {
                formCode = new
                {
                    systemCode = "FA (3)",
                    schemaVersion = "1-0E",
                    value = "FA"
                },
                encryption = new
                {
                    encryptedSymmetricKey,
                    initializationVector = Convert.ToBase64String(iv)
                }
            };

            var content = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json");

            _logger.LogInformation("Otwieram sesję online dla NIP: {Nip}", _session.Nip);

            var response = await SendAuthorizedAsync(HttpMethod.Post, "sessions/online", content, ct);
            var responseBody = await response.Content.ReadAsStringAsync(ct);

            _logger.LogInformation("Open session response: {Status}", response.StatusCode);

            if (!response.IsSuccessStatusCode)
            {
                var error = KSeFErrorParser.Parse(responseBody);
                _logger.LogError("Open session error: {Error} | Status: {Status} | Body: {Body}",
                    error, response.StatusCode, responseBody);
                return Fail($"Błąd otwierania sesji [{response.StatusCode}]: {error}");
            }

            var sessionResponse = JsonSerializer.Deserialize<OpenSessionResponse>(responseBody, JsonOptions);

            if (sessionResponse == null || string.IsNullOrEmpty(sessionResponse.ReferenceNumber))
                return Fail("Brak referenceNumber w odpowiedzi sesji");

            _session.SetOnlineSession(sessionResponse.ReferenceNumber, sessionResponse.ValidUntil, aesKey, iv);

            _logger.LogInformation("Sesja online otwarta: {Ref}, ważna do: {ValidUntil}",
                sessionResponse.ReferenceNumber, sessionResponse.ValidUntil);

            return new SessionResult
            {
                Success = true,
                SessionReferenceNumber = sessionResponse.ReferenceNumber,
                ValidUntil = sessionResponse.ValidUntil
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Błąd otwierania sesji online");
            return Fail(ex.Message);
        }
    }

    public async Task<SessionResult> CloseOnlineSessionAsync(CancellationToken ct = default)
    {
        if (!_session.IsAuthenticated)
            throw new UnauthorizedAccessException("Brak aktywnej sesji KSeF");

        var referenceNumber = _session.GetRawSessionReferenceNumber();

        if (string.IsNullOrEmpty(referenceNumber))
        {
            _logger.LogInformation("Brak numeru referencyjnego sesji online do zamknięcia");
            return new SessionResult { Success = true, SessionReferenceNumber = null };
        }

        try
        {
            _logger.LogInformation(
                "Zamykam sesję online: {Ref} | Token (pierwsze 20): {Token}",
                referenceNumber,
                string.IsNullOrEmpty(_session.AccessToken) ? "(brak)" : _session.AccessToken[..Math.Min(20, _session.AccessToken.Length)] + "...");

            var response = await SendAuthorizedAsync(
                HttpMethod.Post,
                $"sessions/online/{referenceNumber}/close",
                new StringContent("{}", Encoding.UTF8, "application/json"),
                ct);

            var responseBody = await response.Content.ReadAsStringAsync(ct);

            _logger.LogInformation("Close session response: {Status} | Body: {Body}",
                response.StatusCode, responseBody);

            if (!response.IsSuccessStatusCode)
            {
                var error = KSeFErrorParser.Parse(responseBody);
                _logger.LogError("Close session error: {Error} | Status: {Status}",
                    error, response.StatusCode);
                return Fail($"Błąd zamknięcia sesji [{response.StatusCode}]: {error}");
            }

            _logger.LogInformation("Sesja online zamknięta pomyślnie: {Ref}", referenceNumber);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Błąd zamykania sesji online w KSeF");
            return Fail(ex.Message);
        }
        finally
        {
            _session.ClearOnlineSession();
        }

        return new SessionResult { Success = true, SessionReferenceNumber = referenceNumber };
    }

public async Task<SessionUpoResult> CloseSessionAndFetchUpoAsync(CancellationToken ct = default)
{
    var referenceNumber = _session.GetRawSessionReferenceNumber();

    if (string.IsNullOrEmpty(referenceNumber))
    {
        return new SessionUpoResult
        {
            Success = false,
            Error = "Brak aktywnej sesji online. Otwórz sesję i wyślij fakturę przed pobraniem UPO."
        };
    }

    var closeResult = await CloseOnlineSessionAsync(ct);
    if (!closeResult.Success)
    {
        return new SessionUpoResult
        {
            Success = false,
            Error = closeResult.Error,
            SessionReferenceNumber = referenceNumber
        };
    }
    
    const int maxAttempts = 30;
    const int delayMs = 2000;

    for (int attempt = 1; attempt <= maxAttempts; attempt++)
    {
        _logger.LogInformation("UPO polling: próba {Attempt}/{Max} dla sesji {Ref}",
            attempt, maxAttempts, referenceNumber);

        var (upoRef, downloadUrl, statusCode) = await FetchSessionUpoInfoAsync(referenceNumber, ct);

        if (!string.IsNullOrEmpty(downloadUrl))
        {
            var upoXml = await DownloadUpoFromUrlAsync(downloadUrl, ct);
            return new SessionUpoResult
            {
                Success = true,
                SessionReferenceNumber = referenceNumber,
                UpoReferenceNumber = upoRef,
                UpoAvailable = !string.IsNullOrEmpty(upoXml),
                UpoXml = upoXml,
                Message = "Sesja zamknięta. UPO zbiorcze pobrane pomyślnie."
            };
        }
        
        if (statusCode == 440)
        {
            _logger.LogWarning("Sesja {Ref} anulowana (kod 440) - UPO niedostępne", referenceNumber);
            return new SessionUpoResult
            {
                Success = true,
                SessionReferenceNumber = referenceNumber,
                UpoAvailable = false,
                Message = "Sesja zamknięta, ale UPO niedostępne - sesja została anulowana przez KSeF."
            };
        }

        if (attempt < maxAttempts)
            await Task.Delay(delayMs, ct);
    }

    _logger.LogWarning("UPO polling wyczerpany dla sesji {Ref}", referenceNumber);
    return new SessionUpoResult
    {
        Success = true,
        SessionReferenceNumber = referenceNumber,
        UpoAvailable = false,
        Message = "Sesja zamknięta. UPO nie było gotowe w ciągu 60 sekund - spróbuj pobrać je później."
    };
}

private async Task<(string? upoRef, string? downloadUrl, int statusCode)> FetchSessionUpoInfoAsync(
    string sessionRef, CancellationToken ct)
{
    try
    {
        var response = await SendAuthorizedAsync(
            HttpMethod.Get,
            $"sessions/{sessionRef}",
            null,
            ct);

        var body = await response.Content.ReadAsStringAsync(ct);
        _logger.LogInformation("Session status: {Status} | Ref: {Ref} | Body: {Body}",
            response.StatusCode, sessionRef, body);

        if (!response.IsSuccessStatusCode)
            return (null, null, 0);

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        
        var statusCode = 0;
        if (root.TryGetProperty("status", out var statusEl) &&
            statusEl.TryGetProperty("code", out var codeEl))
        {
            statusCode = codeEl.GetInt32();
        }

        // Sprawdź czy UPO jest już gotowe (status 200 + pole upo.pages)
        if (root.TryGetProperty("upo", out var upoEl) &&
            upoEl.TryGetProperty("pages", out var pagesEl) &&
            pagesEl.ValueKind == JsonValueKind.Array &&
            pagesEl.GetArrayLength() > 0)
        {
            var firstPage = pagesEl[0];
            var upoRef = firstPage.TryGetProperty("referenceNumber", out var refEl)
                ? refEl.GetString()
                : null;
            var downloadUrl = firstPage.TryGetProperty("downloadUrl", out var urlEl)
                ? urlEl.GetString()
                : null;

            _logger.LogInformation("UPO gotowe: ref={Ref}, statusCode={Code}", upoRef, statusCode);
            return (upoRef, downloadUrl, statusCode);
        }

        _logger.LogInformation("UPO jeszcze niedostępne, statusCode={Code}", statusCode);
        return (null, null, statusCode);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Błąd pobierania statusu sesji {Ref}", sessionRef);
        return (null, null, 0);
    }
}

    private async Task<string?> DownloadUpoFromUrlAsync(string downloadUrl, CancellationToken ct)
    {
        try
        {
            // downloadUrl jest pre-signed (Azure Blob Storage) - nie wymaga Authorization
            var client = _httpClientFactory.CreateClient();
            var response = await client.GetAsync(downloadUrl, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            _logger.LogInformation("Download UPO XML: {Status}", response.StatusCode);

            return response.IsSuccessStatusCode ? body : null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Błąd pobierania UPO z URL: {Url}", downloadUrl);
            return null;
        }
    }

    private async Task<HttpResponseMessage> SendAuthorizedAsync(
        HttpMethod method,
        string relativeUrl,
        HttpContent? content,
        CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("KSeF");
        using var request = new HttpRequestMessage(method, relativeUrl);
        request.Headers.Add("Authorization", $"Bearer {_session.AccessToken}");
        if (content != null)
            request.Content = content;
        return await client.SendAsync(request, ct);
    }

    private async Task<List<CertificateInfo>> FetchCertificatesAsync(CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("KSeF");
        var response = await client.GetAsync("security/public-key-certificates", ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Błąd pobierania certyfikatów: {responseBody}");

        try
        {
            var list = JsonSerializer.Deserialize<List<CertificateInfo>>(responseBody, JsonOptions);
            if (list != null && list.Count > 0)
            {
                _session.SetCertificates(list);
                return list;
            }
        }
        catch { }

        var wrapper = JsonSerializer.Deserialize<CertificatesWrapper>(responseBody, JsonOptions);
        var certs = wrapper?.Certificates ?? new List<CertificateInfo>();
        _session.SetCertificates(certs);
        return certs;
    }

    private static SessionResult Fail(string error) =>
        new() { Success = false, Error = error };

    private class CertificatesWrapper
    {
        public List<CertificateInfo> Certificates { get; set; } = new();
    }
}

public class SessionUpoResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? Message { get; set; }
    public string? SessionReferenceNumber { get; set; }
    public string? UpoReferenceNumber { get; set; }
    public bool UpoAvailable { get; set; }
    public string? UpoXml { get; set; }
}