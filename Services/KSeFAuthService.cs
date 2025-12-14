using System.Text;
using System.Text.Json;
using KSeF.Backend.Models.Responses;
using KSeF.Backend.Services.Interfaces;

namespace KSeF.Backend.Services;

public class KSeFAuthService : IKSeFAuthService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IKSeFCryptoService _cryptoService;
    private readonly KSeFSessionManager _session;
    private readonly ILogger<KSeFAuthService> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public KSeFAuthService(
        IHttpClientFactory httpClientFactory,
        IKSeFCryptoService cryptoService,
        KSeFSessionManager session,
        ILogger<KSeFAuthService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _cryptoService = cryptoService;
        _session = session;
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
    }

    private HttpClient CreateClient() => _httpClientFactory.CreateClient("KSeF");

    public async Task<AuthResult> LoginAsync(string nip, string ksefToken, CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("═══════════════════════════════════════════════════════════════");
            _logger.LogInformation("  LOGOWANIE DO KSeF - NIP: {Nip}", nip);
            _logger.LogInformation("═══════════════════════════════════════════════════════════════");

            var client = CreateClient();

            // ═══ KROK 1: Pobierz certyfikaty ═══
            _logger.LogInformation("Krok 1: GET /security/public-key-certificates");
            var certificates = await GetCertificatesAsync(client, ct);
            var tokenEncryptionCert = certificates.First(c => c.Usage?.Contains("KsefTokenEncryption") == true);
            _logger.LogInformation("  ✓ Certyfikat KsefTokenEncryption pobrany");

            // ═══ KROK 2: Pobierz challenge ═══
            _logger.LogInformation("Krok 2: POST /auth/challenge");
            var challenge = await GetChallengeAsync(client, ct);
            _logger.LogInformation("  ✓ Challenge: {Challenge}", challenge.Challenge);
            _logger.LogInformation("  ✓ Timestamp: {Timestamp}", challenge.Timestamp);

            // ═══ KROK 3: Zaszyfruj token ═══
            _logger.LogInformation("Krok 3: Szyfrowanie tokena (RSA-OAEP SHA-256)");
            var timestampMs = new DateTimeOffset(challenge.Timestamp).ToUnixTimeMilliseconds();
            var encryptedToken = _cryptoService.EncryptToken(ksefToken, timestampMs, tokenEncryptionCert.Certificate);
            _logger.LogInformation("  ✓ Token zaszyfrowany");

            // ═══ KROK 4: POST /auth/ksef-token ═══
            _logger.LogInformation("Krok 4: POST /auth/ksef-token");
            var authTokenRequest = new
            {
                challenge = challenge.Challenge,
                contextIdentifier = new { type = "Nip", value = nip },
                encryptedToken
            };

            var authTokenResponse = await client.PostAsync(
                "auth/ksef-token",
                new StringContent(JsonSerializer.Serialize(authTokenRequest), Encoding.UTF8, "application/json"),
                ct);

            var authTokenContent = await authTokenResponse.Content.ReadAsStringAsync(ct);
            _logger.LogInformation("  Response: {Status}", authTokenResponse.StatusCode);

            if (!authTokenResponse.IsSuccessStatusCode)
            {
                _logger.LogError("  ✗ Błąd: {Content}", authTokenContent);
                return new AuthResult { Success = false, Error = $"auth/ksef-token failed: {authTokenContent}" };
            }

            var authToken = JsonSerializer.Deserialize<AuthTokenResponse>(authTokenContent, _jsonOptions)!;
            var authenticationToken = authToken.AuthenticationToken!.Token;
            var referenceNumber = authToken.ReferenceNumber;

            _logger.LogInformation("  ✓ ReferenceNumber: {Ref}", referenceNumber);
            _logger.LogInformation("  ✓ AuthenticationToken otrzymany (Bearer dla kroków 5-6)");

            // ═══ KROK 5: GET /auth/{referenceNumber} ═══
            _logger.LogInformation("Krok 5: GET /auth/{Ref}", referenceNumber);
            using var statusRequest = new HttpRequestMessage(HttpMethod.Get, $"auth/{referenceNumber}");
            statusRequest.Headers.Add("Authorization", $"Bearer {authenticationToken}");

            var statusResponse = await client.SendAsync(statusRequest, ct);
            var statusContent = await statusResponse.Content.ReadAsStringAsync(ct);

            if (!statusResponse.IsSuccessStatusCode)
            {
                _logger.LogError("  ✗ Błąd: {Content}", statusContent);
                return new AuthResult { Success = false, Error = $"auth status failed: {statusContent}" };
            }

            var status = JsonSerializer.Deserialize<AuthStatusResponse>(statusContent, _jsonOptions)!;
            _logger.LogInformation("  ✓ Status: {Code} - {Desc}", status.Status.Code, status.Status.Description);

            // ═══ KROK 6: POST /auth/token/redeem ═══
            _logger.LogInformation("Krok 6: POST /auth/token/redeem");
            using var redeemRequest = new HttpRequestMessage(HttpMethod.Post, "auth/token/redeem");
            redeemRequest.Headers.Add("Authorization", $"Bearer {authenticationToken}");

            var redeemResponse = await client.SendAsync(redeemRequest, ct);
            var redeemContent = await redeemResponse.Content.ReadAsStringAsync(ct);

            if (!redeemResponse.IsSuccessStatusCode)
            {
                _logger.LogError("  ✗ Błąd: {Content}", redeemContent);
                return new AuthResult { Success = false, Error = $"token/redeem failed: {redeemContent}" };
            }

            var tokens = JsonSerializer.Deserialize<TokenRedeemResponse>(redeemContent, _jsonOptions)!;

            // ═══ ZAPISZ SESJĘ ═══
            _session.SetAuthSession(nip, tokens);

            _logger.LogInformation("═══════════════════════════════════════════════════════════════");
            _logger.LogInformation("  ✅ ZALOGOWANO POMYŚLNIE!");
            _logger.LogInformation("  AccessToken ważny do: {Until}", tokens.AccessToken?.ValidUntil);
            _logger.LogInformation("  RefreshToken ważny do: {Until}", tokens.RefreshToken?.ValidUntil);
            _logger.LogInformation("  OD TERAZ BEARER TOKEN = accessToken!");
            _logger.LogInformation("═══════════════════════════════════════════════════════════════");

            return new AuthResult
            {
                Success = true,
                ReferenceNumber = referenceNumber,
                AccessTokenValidUntil = tokens.AccessToken?.ValidUntil,
                RefreshTokenValidUntil = tokens.RefreshToken?.ValidUntil
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Błąd logowania");
            return new AuthResult { Success = false, Error = ex.Message };
        }
    }

    public async Task<bool> RefreshTokenIfNeededAsync(CancellationToken ct = default)
    {
        if (!_session.NeedsTokenRefresh)
            return true;

        _logger.LogInformation("Odświeżanie accessToken...");

        try
        {
            var client = CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Post, "auth/token/refresh");
            request.Headers.Add("Authorization", $"Bearer {_session.RefreshToken}");

            var response = await client.SendAsync(request, ct);
            var content = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Błąd refresh: {Content}", content);
                return false;
            }

            var tokens = JsonSerializer.Deserialize<TokenRefreshResponse>(content, _jsonOptions)!;
            _session.UpdateAccessToken(tokens);

            _logger.LogInformation("AccessToken odświeżony, ważny do: {Until}", tokens.AccessToken?.ValidUntil);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Błąd odświeżania tokena");
            return false;
        }
    }

    public void Logout()
    {
        _session.ClearAuthSession();
        _logger.LogInformation("Wylogowano - sesja wyczyszczona");
    }

    #region Private Helpers

    private async Task<List<CertificateInfo>> GetCertificatesAsync(HttpClient client, CancellationToken ct)
    {
        // Sprawdź cache
        var cached = _session.GetCachedCertificates();
        if (cached != null)
        {
            _logger.LogDebug("  Certyfikaty z cache");
            return cached;
        }

        var response = await client.GetAsync("security/public-key-certificates", ct);
        var content = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Błąd pobierania certyfikatów: {response.StatusCode} - {content}");

        var certificates = JsonSerializer.Deserialize<List<CertificateInfo>>(content, _jsonOptions)
            ?? throw new InvalidOperationException("Nie udało się zdeserializować certyfikatów");

        _session.SetCertificates(certificates);
        _logger.LogInformation("  Pobrano {Count} certyfikatów", certificates.Count);

        return certificates;
    }

    private async Task<ChallengeResponse> GetChallengeAsync(HttpClient client, CancellationToken ct)
    {
        var response = await client.PostAsync(
            "auth/challenge",
            new StringContent("{}", Encoding.UTF8, "application/json"),
            ct);

        var content = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Błąd challenge: {response.StatusCode} - {content}");

        return JsonSerializer.Deserialize<ChallengeResponse>(content, _jsonOptions)
            ?? throw new InvalidOperationException("Nie udało się zdeserializować challenge");
    }

    #endregion
}