// Services/KSeF/Invoice/KSeFOnlineSessionService.cs
using System.Text;
using System.Text.Json;
using KSeF.Backend.Models.Responses;
using KSeF.Backend.Services.Interfaces;

namespace KSeF.Backend.Services.KSeF.Invoice;

public class KSeFOnlineSessionService : IKSeFOnlineSessionService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IKSeFAuthService _authService;
    private readonly IKSeFCryptoService _cryptoService;
    private readonly KSeFSessionManager _session;
    private readonly ILogger<KSeFOnlineSessionService> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public KSeFOnlineSessionService(
        IHttpClientFactory httpClientFactory,
        IKSeFAuthService authService,
        IKSeFCryptoService cryptoService,
        KSeFSessionManager session,
        ILogger<KSeFOnlineSessionService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _authService = authService;
        _cryptoService = cryptoService;
        _session = session;
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
    }

    public async Task<SessionResult> OpenOnlineSessionAsync(CancellationToken ct = default)
    {
        EnsureAuthenticated();
        await _authService.RefreshTokenIfNeededAsync(ct);

        if (_session.HasActiveOnlineSession)
        {
            return new SessionResult
            {
                Success = true,
                SessionReferenceNumber = _session.SessionReferenceNumber,
                ValidUntil = _session.SessionValidUntil
            };
        }

        try
        {
            var client = _httpClientFactory.CreateClient("KSeF");
            var certificates = _session.GetCachedCertificates() ?? await GetCertificatesAsync(client, ct);
            var symCert = certificates.First(c => c.Usage?.Contains("SymmetricKeyEncryption") == true);
            var (aesKey, iv) = _cryptoService.GenerateAesKeyAndIv();
            var encryptedSymmetricKey = _cryptoService.EncryptAesKey(aesKey, symCert.Certificate);

            var requestBody = new
            {
                formCode = new { systemCode = "FA (3)", schemaVersion = "1-0E", value = "FA" },
                encryption = new { encryptedSymmetricKey, initializationVector = Convert.ToBase64String(iv) }
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, "sessions/online");
            request.Headers.Add("Authorization", $"Bearer {_session.AccessToken}");
            request.Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

            var response = await client.SendAsync(request, ct);
            var content = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
                return new SessionResult { Success = false, Error = content };

            var sessionResponse = JsonSerializer.Deserialize<OpenSessionResponse>(content, _jsonOptions)!;
            _session.SetOnlineSession(sessionResponse.ReferenceNumber, sessionResponse.ValidUntil, aesKey, iv);

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
            return new SessionResult { Success = false, Error = ex.Message };
        }
    }

    public async Task<bool> CloseOnlineSessionAsync(CancellationToken ct = default)
    {
        if (!_session.HasActiveOnlineSession) return true;

        try
        {
            var client = _httpClientFactory.CreateClient("KSeF");
            using var request = new HttpRequestMessage(HttpMethod.Delete,
                $"sessions/online/{_session.SessionReferenceNumber}");
            request.Headers.Add("Authorization", $"Bearer {_session.AccessToken}");
            await client.SendAsync(request, ct);
            _session.ClearOnlineSession();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Błąd zamykania sesji online");
            _session.ClearOnlineSession();
            return false;
        }
    }

    private async Task<List<CertificateInfo>> GetCertificatesAsync(HttpClient client, CancellationToken ct)
    {
        var response = await client.GetAsync("security/public-key-certificates", ct);
        var content = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Błąd pobierania certyfikatów: {content}");

        var certificates = JsonSerializer.Deserialize<List<CertificateInfo>>(content, _jsonOptions)!;
        _session.SetCertificates(certificates);
        return certificates;
    }

    private void EnsureAuthenticated()
    {
        if (!_session.IsAuthenticated)
            throw new UnauthorizedAccessException("Nie jesteś zalogowany do KSeF");
    }
}