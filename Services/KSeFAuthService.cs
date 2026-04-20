// Services/KSeFAuthService.cs
using System.Text;
using System.Text.Json;
using KSeF.Backend.Infrastructure.KSeF;
using KSeF.Backend.Models.Responses;
using KSeF.Backend.Services.Interfaces;
using KSeF.Backend.Services.KSeF.Auth;

namespace KSeF.Backend.Services;

public class KSeFAuthService : IKSeFAuthService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IKSeFEnvironmentService _environmentService;
    private readonly IKSeFCryptoService _cryptoService;
    private readonly IKSeFChallengeService _challengeService;
    private readonly IKSeFAuthPollingService _pollingService;
    private readonly IKSeFAuthRedeemService _redeemService;
    private readonly IKSeFTokenRefreshService _refreshService;
    private readonly KSeFSessionManager _session;
    private readonly ILogger<KSeFAuthService> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public KSeFAuthService(
        IHttpClientFactory httpClientFactory,
        IKSeFEnvironmentService environmentService,
        IKSeFCryptoService cryptoService,
        IKSeFChallengeService challengeService,
        IKSeFAuthPollingService pollingService,
        IKSeFAuthRedeemService redeemService,
        IKSeFTokenRefreshService refreshService,
        KSeFSessionManager session,
        ILogger<KSeFAuthService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _environmentService = environmentService;
        _cryptoService = cryptoService;
        _challengeService = challengeService;
        _pollingService = pollingService;
        _redeemService = redeemService;
        _refreshService = refreshService;
        _session = session;
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
    }

    public async Task<AuthResult> LoginAsync(
        string nip,
        string ksefToken,
        string environment = "Test",
        CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("═══════════════════════════════════════════════════════════════");
            _logger.LogInformation("  LOGOWANIE DO KSeF API v2 — NIP: {Nip}, ENV: {Env}", nip, environment);
            _logger.LogInformation("═══════════════════════════════════════════════════════════════");

            var apiBaseUrl = _environmentService.GetApiBaseUrl(environment);
            var client = CreateClient(apiBaseUrl);

            _logger.LogInformation("  BaseAddress: {BaseAddress}", client.BaseAddress);

            _logger.LogInformation("--- Krok 1: GET /security/public-key-certificates ---");
            var certificates = await GetCertificatesAsync(client, ct);

            var tokenEncryptionCert = certificates.FirstOrDefault(c =>
                c.Usage != null && c.Usage.Any(u =>
                    u.Contains("KsefTokenEncryption", StringComparison.OrdinalIgnoreCase) ||
                    u.Contains("Encryption", StringComparison.OrdinalIgnoreCase) ||
                    u.Contains("Token", StringComparison.OrdinalIgnoreCase)));

            if (tokenEncryptionCert == null)
            {
                _logger.LogWarning("  Nie znaleziono certyfikatu Encryption — próbuję pierwszy dostępny");
                tokenEncryptionCert = certificates.FirstOrDefault();
            }

            if (tokenEncryptionCert == null)
                return Fail("Brak certyfikatów w odpowiedzi KSeF API");

            _logger.LogInformation("  ✓ Certyfikat wybrany, Usage: [{Usage}]",
                string.Join(", ", tokenEncryptionCert.Usage ?? new List<string>()));

            _logger.LogInformation("--- Krok 2: POST /auth/challenge ---");
            var (challenge, timestampMs) = await _challengeService.GetChallengeAsync(client, ct);
            _logger.LogInformation("  ✓ Challenge: {Ch}", challenge);
            _logger.LogInformation("  ✓ Timestamp ms: {Ms}", timestampMs);

            _logger.LogInformation("--- Krok 3: Szyfrowanie tokenu (RSA-OAEP SHA-256) ---");
            string encryptedToken;
            try
            {
                encryptedToken = _cryptoService.EncryptToken(ksefToken, timestampMs, tokenEncryptionCert.Certificate);
                _logger.LogInformation("  ✓ Token zaszyfrowany (Base64 len={Len})", encryptedToken.Length);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "  ✗ Błąd szyfrowania tokenu");
                return Fail($"Błąd szyfrowania tokenu: {ex.Message}");
            }

            _logger.LogInformation("--- Krok 4: POST /auth/ksef-token ---");
            var authTokenRequestBody = new
            {
                challenge,
                contextIdentifier = new { type = "Nip", value = nip },
                encryptedToken
            };

            var authTokenJson = JsonSerializer.Serialize(authTokenRequestBody, _jsonOptions);
            var authTokenHttpResponse = await client.PostAsync(
                "auth/ksef-token",
                new StringContent(authTokenJson, Encoding.UTF8, "application/json"),
                ct);

            var authTokenContent = await authTokenHttpResponse.Content.ReadAsStringAsync(ct);
            _logger.LogInformation("  Response status: {Status} ({Code})",
                authTokenHttpResponse.StatusCode, (int)authTokenHttpResponse.StatusCode);
            _logger.LogDebug("  Response body: {Body}", KSeFResponseLogger.Sanitize(authTokenContent));

            if (!authTokenHttpResponse.IsSuccessStatusCode)
            {
                var errMsg = KSeFErrorParser.ExtractError(authTokenContent, $"HTTP {(int)authTokenHttpResponse.StatusCode}");
                _logger.LogError("  ✗ Błąd auth/ksef-token: {Err}", errMsg);
                return Fail(errMsg);
            }

            var authToken = JsonSerializer.Deserialize<AuthTokenResponse>(authTokenContent, _jsonOptions);
            if (authToken == null)
                return Fail("Nie można sparsować odpowiedzi auth/ksef-token");

            var authenticationToken = authToken.AuthenticationToken?.Token;
            var referenceNumber = authToken.ReferenceNumber;

            if (string.IsNullOrEmpty(authenticationToken))
            {
                _logger.LogError("  ✗ Brak authenticationToken");
                return Fail("Brak tokenu autoryzacyjnego w odpowiedzi. Sprawdź poprawność tokenu KSeF i NIP.");
            }

            _logger.LogInformation("  ✓ ReferenceNumber: {ReferenceNumber}", referenceNumber);

            _logger.LogInformation("--- Krok 5: Polling ---");
            var finalToken = await _pollingService.PollAuthStatusAsync(client, referenceNumber, authenticationToken, ct);

            if (finalToken == null)
            {
                _logger.LogError("  ✗ Timeout — polling nie osiągnął statusu gotowości");
                return Fail("Timeout autoryzacji");
            }

            _logger.LogInformation("--- Krok 6: POST /auth/token/redeem ---");
            var tokens = await _redeemService.RedeemTokenAsync(client, finalToken, ct);

            if (tokens?.AccessToken == null || string.IsNullOrEmpty(tokens.AccessToken.Token))
            {
                _logger.LogError("  ✗ Brak accessToken w redeem response");
                return Fail("Brak accessToken w odpowiedzi token/redeem");
            }

            _session.SetAuthSession(nip, tokens);

            _logger.LogInformation("═══════════════════════════════════════════════════════════════");
            _logger.LogInformation("  ✅ ZALOGOWANO POMYŚLNIE!");
            _logger.LogInformation("  AccessToken ważny do: {Until}", tokens.AccessToken.ValidUntil);
            _logger.LogInformation("═══════════════════════════════════════════════════════════════");

            return new AuthResult
            {
                Success = true,
                SessionToken = tokens.AccessToken.Token,
                ReferenceNumber = referenceNumber,
                AccessTokenValidUntil = tokens.AccessToken.ValidUntil,
                RefreshTokenValidUntil = tokens.RefreshToken?.ValidUntil
            };
        }
        catch (KSeFApiException ex)
        {
            _logger.LogError("Błąd API KSeF: {Message}", ex.Message);
            return Fail(ex.Message);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Błąd połączenia z KSeF");
            return Fail($"Błąd połączenia z serwerem KSeF: {ex.Message}");
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "Timeout połączenia z KSeF");
            return Fail("Timeout — serwer KSeF nie odpowiedział w czasie");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Nieoczekiwany błąd logowania do KSeF");
            return Fail($"Nieoczekiwany błąd: {ex.Message}");
        }
    }

    public Task<bool> RefreshTokenIfNeededAsync(CancellationToken ct = default)
        => _refreshService.RefreshTokenIfNeededAsync(ct);

    public void Logout()
    {
        _session.ClearAuthSession();
        _logger.LogInformation("Wylogowano — sesja wyczyszczona");
    }

    private HttpClient CreateClient(string baseUrl)
    {
        var client = _httpClientFactory.CreateClient("KSeF");
        client.BaseAddress = new Uri(baseUrl);
        return client;
    }

    private async Task<List<CertificateInfo>> GetCertificatesAsync(HttpClient client, CancellationToken ct)
    {
        var cached = _session.GetCachedCertificates();
        if (cached != null)
        {
            _logger.LogDebug("  Certyfikaty z cache ({Count} szt.)", cached.Count);
            return cached;
        }

        var response = await client.GetAsync("security/public-key-certificates", ct);
        var content = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            throw new KSeFApiException(
                $"Błąd pobierania certyfikatów: {KSeFErrorParser.ExtractError(content, "")}",
                response.StatusCode, content);
        }

        var certificates = JsonSerializer.Deserialize<List<CertificateInfo>>(content, _jsonOptions)!;
        _session.SetCertificates(certificates);

        _logger.LogInformation("  ✓ Pobrano {Count} certyfikat(ów)", certificates.Count);
        return certificates;
    }

    private static AuthResult Fail(string error) => new() { Success = false, Error = error };
}