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
            
            var tokenEncryptionCert = certificates.FirstOrDefault(c => 
                c.Usage != null && c.Usage.Contains("KsefTokenEncryption"));
            
            if (tokenEncryptionCert == null)
            {
                var availableUsages = certificates
                    .Where(c => c.Usage != null)
                    .SelectMany(c => c.Usage!)
                    .Distinct();
                    
                _logger.LogError("Nie znaleziono certyfikatu KsefTokenEncryption. Dostępne: {Usages}", 
                    string.Join(", ", availableUsages));
                return new AuthResult { Success = false, Error = "Brak certyfikatu szyfrującego w odpowiedzi KSeF" };
            }
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

            var authToken = DeserializeJson<AuthTokenResponse>(authTokenContent, "auth/ksef-token");
            var authenticationToken = authToken.AuthenticationToken?.Token;
            var referenceNumber = authToken.ReferenceNumber;

            if (string.IsNullOrEmpty(authenticationToken))
            {
                return new AuthResult { Success = false, Error = "Brak tokenu autoryzacyjnego w odpowiedzi" };
            }

            _logger.LogInformation("  ✓ ReferenceNumber: {Ref}", referenceNumber);
            _logger.LogInformation("  ✓ AuthenticationToken otrzymany");

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

            var status = DeserializeJson<AuthStatusResponse>(statusContent, "auth/status");
            _logger.LogInformation("  ✓ Status: {Code} - {Desc}", status.Status?.Code, status.Status?.Description);

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

            var tokens = DeserializeJson<TokenRedeemResponse>(redeemContent, "token/redeem");

            // ═══ ZAPISZ SESJĘ ═══
            _session.SetAuthSession(nip, tokens);

            _logger.LogInformation("═══════════════════════════════════════════════════════════════");
            _logger.LogInformation("  ✅ ZALOGOWANO POMYŚLNIE!");
            _logger.LogInformation("  AccessToken ważny do: {Until}", tokens.AccessToken?.ValidUntil);
            _logger.LogInformation("  RefreshToken ważny do: {Until}", tokens.RefreshToken?.ValidUntil);
            _logger.LogInformation("═══════════════════════════════════════════════════════════════");

            return new AuthResult
            {
                Success = true,
                ReferenceNumber = referenceNumber,
                AccessTokenValidUntil = tokens.AccessToken?.ValidUntil,
                RefreshTokenValidUntil = tokens.RefreshToken?.ValidUntil
            };
        }
        catch (KSeFApiException ex)
        {
            _logger.LogError("Błąd API KSeF: {Message}", ex.Message);
            return new AuthResult { Success = false, Error = ex.Message };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Błąd połączenia z KSeF");
            return new AuthResult { Success = false, Error = $"Błąd połączenia z serwerem KSeF: {ex.Message}" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Nieoczekiwany błąd logowania");
            return new AuthResult { Success = false, Error = $"Nieoczekiwany błąd: {ex.Message}" };
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

            var tokens = DeserializeJson<TokenRefreshResponse>(content, "token/refresh");
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
        var cached = _session.GetCachedCertificates();
        if (cached != null)
        {
            _logger.LogDebug("  Certyfikaty z cache");
            return cached;
        }

        var response = await client.GetAsync("security/public-key-certificates", ct);
        var content = await response.Content.ReadAsStringAsync(ct);
        var contentType = response.Content.Headers.ContentType?.MediaType ?? "unknown";

        _logger.LogDebug("  Response Status: {Status}", response.StatusCode);
        _logger.LogDebug("  Response Content-Type: {ContentType}", contentType);

        // Sprawdź czy to przekierowanie (302)
        if (response.StatusCode == System.Net.HttpStatusCode.Redirect ||
            response.StatusCode == System.Net.HttpStatusCode.Found ||
            response.StatusCode == System.Net.HttpStatusCode.MovedPermanently)
        {
            var location = response.Headers.Location?.ToString() ?? "unknown";
            _logger.LogError("API KSeF zwróciło przekierowanie do: {Location}", location);
            throw new KSeFApiException(
                $"API KSeF przekierowuje na {location}. Serwer KSeF może być niedostępny lub zmienił się adres API.",
                response.StatusCode,
                content);
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new KSeFApiException(
                $"Błąd pobierania certyfikatów: HTTP {(int)response.StatusCode} ({response.StatusCode})",
                response.StatusCode,
                content);
        }

        // Sprawdź czy odpowiedź to JSON
        if (!contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
        {
            var preview = content.Length > 500 ? content[..500] + "..." : content;
            _logger.LogError("Nieoczekiwany Content-Type: {ContentType}. Treść: {Content}", contentType, preview);
            
            throw new KSeFApiException(
                $"API KSeF zwróciło nieoczekiwany format: {contentType}. Oczekiwano application/json. " +
                "Możliwe przyczyny: zmiana w API KSeF, problem z serwerem, lub błędny URL.",
                response.StatusCode,
                preview);
        }

        var certificates = DeserializeJson<List<CertificateInfo>>(content, "certificates");
        
        if (certificates == null || certificates.Count == 0)
        {
            throw new KSeFApiException("API KSeF zwróciło pustą listę certyfikatów", response.StatusCode, content);
        }

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
        {
            throw new KSeFApiException(
                $"Błąd pobierania challenge: HTTP {(int)response.StatusCode}",
                response.StatusCode,
                content);
        }

        return DeserializeJson<ChallengeResponse>(content, "challenge");
    }

    /// <summary>
    /// Bezpieczna deserializacja JSON z czytelnym komunikatem błędu
    /// </summary>
    private T DeserializeJson<T>(string json, string context)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new KSeFApiException($"Pusta odpowiedź z endpointu {context}", null, json);
        }

        // Sprawdź czy to nie XML/HTML
        var trimmed = json.TrimStart();
        if (trimmed.StartsWith('<'))
        {
            var preview = json.Length > 200 ? json[..200] + "..." : json;
            throw new KSeFApiException(
                $"Endpoint {context} zwrócił XML/HTML zamiast JSON. " +
                "API KSeF mogło się zmienić lub występuje problem z serwerem.",
                null,
                preview);
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json, _jsonOptions)
                ?? throw new KSeFApiException($"Deserializacja {context} zwróciła null", null, json);
        }
        catch (JsonException ex)
        {
            var preview = json.Length > 200 ? json[..200] + "..." : json;
            throw new KSeFApiException(
                $"Błąd parsowania JSON z {context}: {ex.Message}",
                null,
                preview);
        }
    }

    #endregion
}

/// <summary>
/// Wyjątek specyficzny dla błędów API KSeF
/// </summary>
public class KSeFApiException : Exception
{
    public System.Net.HttpStatusCode? StatusCode { get; }
    public string? RawResponse { get; }

    public KSeFApiException(string message, System.Net.HttpStatusCode? statusCode = null, string? rawResponse = null) 
        : base(message)
    {
        StatusCode = statusCode;
        RawResponse = rawResponse;
    }
}