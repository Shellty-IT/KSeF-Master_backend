// Services/KSeF/Invoice/KSeFOnlineSessionService.cs
using System.Text;
using System.Text.Json;
using KSeF.Backend.Infrastructure.KSeF;
using KSeF.Backend.Models.Responses.Common;
using KSeF.Backend.Models.Responses.Certificate;
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