// Services/KSeFAuthService.cs
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false
        };
    }

    private HttpClient CreateClient() => _httpClientFactory.CreateClient("KSeF");

    public async Task<AuthResult> LoginAsync(string nip, string ksefToken, CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("═══════════════════════════════════════════════════════════════");
            _logger.LogInformation("  LOGOWANIE DO KSeF API v2 — NIP: {Nip}", nip);
            _logger.LogInformation("═══════════════════════════════════════════════════════════════");

            var client = CreateClient();
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
                return new AuthResult { Success = false, Error = "Brak certyfikatów w odpowiedzi KSeF API" };

            _logger.LogInformation("  ✓ Certyfikat wybrany, Usage: [{Usage}]",
                string.Join(", ", tokenEncryptionCert.Usage ?? new List<string>()));

            _logger.LogInformation("--- Krok 2: POST /auth/challenge ---");
            var (challenge, timestampMs) = await GetChallengeWithTimestampAsync(client, ct);
            _logger.LogInformation("  ✓ Challenge: {Ch}", challenge.Challenge);
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
                return new AuthResult { Success = false, Error = $"Błąd szyfrowania tokenu: {ex.Message}" };
            }

            _logger.LogInformation("--- Krok 4: POST /auth/ksef-token ---");
            var authTokenRequestBody = new
            {
                challenge = challenge.Challenge,
                contextIdentifier = new { type = "Nip", value = nip },
                encryptedToken
            };

            var authTokenJson = JsonSerializer.Serialize(authTokenRequestBody, _jsonOptions);
            _logger.LogDebug("  Request body: {Body}", authTokenJson);

            var authTokenHttpResponse = await client.PostAsync(
                "auth/ksef-token",
                new StringContent(authTokenJson, Encoding.UTF8, "application/json"),
                ct);

            var authTokenContent = await authTokenHttpResponse.Content.ReadAsStringAsync(ct);
            _logger.LogInformation("  Response status: {Status} ({Code})",
                authTokenHttpResponse.StatusCode, (int)authTokenHttpResponse.StatusCode);
            _logger.LogInformation("  Response body: {Body}", SanitizeForLog(authTokenContent));

            if (!authTokenHttpResponse.IsSuccessStatusCode)
            {
                var errMsg = ExtractKsefError(authTokenContent, $"HTTP {(int)authTokenHttpResponse.StatusCode}");
                _logger.LogError("  ✗ Błąd auth/ksef-token: {Err}", errMsg);
                return new AuthResult { Success = false, Error = errMsg };
            }

            var authToken = SafeDeserialize<AuthTokenResponse>(authTokenContent, "auth/ksef-token");
            if (authToken == null)
                return new AuthResult { Success = false, Error = "Nie można sparsować odpowiedzi auth/ksef-token" };

            var authenticationToken = authToken.AuthenticationToken?.Token;
            var referenceNumber = authToken.ReferenceNumber;

            if (string.IsNullOrEmpty(authenticationToken))
            {
                _logger.LogError("  ✗ Brak authenticationToken. Body: {Body}", authTokenContent);
                return new AuthResult
                {
                    Success = false,
                    Error = "Brak tokenu autoryzacyjnego w odpowiedzi. Sprawdź poprawność tokenu KSeF i NIP."
                };
            }

            _logger.LogInformation("  ✓ ReferenceNumber: {Ref}", referenceNumber);
            _logger.LogInformation("  ✓ AuthenticationToken otrzymany (len={Len})", authenticationToken.Length);

            _logger.LogInformation("--- Krok 5: Polling GET /auth/{Ref} (czekamy na status 200) ---", referenceNumber);

            string? finalToken = null;
            var maxAttempts = 15;
            var delayMs = 1000;

            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                using var statusRequest = new HttpRequestMessage(HttpMethod.Get, $"auth/{referenceNumber}");
                statusRequest.Headers.Add("Authorization", $"Bearer {authenticationToken}");

                var statusResponse = await client.SendAsync(statusRequest, ct);
                var statusContent = await statusResponse.Content.ReadAsStringAsync(ct);

                _logger.LogInformation("  [Próba {A}/{Max}] HTTP {Code}", attempt, maxAttempts, (int)statusResponse.StatusCode);
                _logger.LogInformation("  Body: {Body}", SanitizeForLog(statusContent));

                if (!statusResponse.IsSuccessStatusCode)
                {
                    var errMsg = ExtractKsefError(statusContent, $"HTTP {(int)statusResponse.StatusCode}");
                    _logger.LogError("  ✗ Błąd GET /auth/{Ref}: {Err}", referenceNumber, errMsg);
                    return new AuthResult { Success = false, Error = $"Błąd sprawdzania statusu: {errMsg}" };
                }

                var status = SafeDeserialize<AuthStatusResponse>(statusContent, "auth/status");
                var statusCode = status?.Status?.Code ?? -1;
                _logger.LogInformation("  Status.Code={Code}, Status.Description={Desc}, IsTokenRedeemed={Redeemed}",
                    statusCode, status?.Status?.Description, status?.IsTokenRedeemed);

                if (status?.AccessToken != null && !string.IsNullOrEmpty(status.AccessToken.Token))
                {
                    _logger.LogInformation("  ✓ AccessToken w odpowiedzi statusu — sukces bez token/redeem");
                    _session.SetAuthSessionFromStatus(nip, status);

                    _logger.LogInformation("  ✅ ZALOGOWANO (via status). AccessToken ważny do: {Until}",
                        status.AccessToken.ValidUntil);

                    return new AuthResult
                    {
                        Success = true,
                        ReferenceNumber = referenceNumber,
                        AccessTokenValidUntil = status.AccessToken.ValidUntil,
                        RefreshTokenValidUntil = status.RefreshToken?.ValidUntil
                    };
                }

                if (statusCode == 200)
                {
                    _logger.LogInformation("  ✓ Status 200 — gotowy do token/redeem");
                    finalToken = authenticationToken;
                    break;
                }

                if (statusCode == 450)
                {
                    _logger.LogInformation("  Status 450 (w toku) — czekam {Delay}ms...", delayMs);
                    if (attempt < maxAttempts)
                        await Task.Delay(delayMs, ct);
                    continue;
                }

                _logger.LogError("  ✗ Nieoczekiwany status: {Code} — {Desc}", statusCode, status?.Status?.Description);
                return new AuthResult
                {
                    Success = false,
                    Error = $"Nieoczekiwany status autoryzacji: {statusCode} — {status?.Status?.Description ?? "brak opisu"}"
                };
            }

            if (finalToken == null)
            {
                _logger.LogError("  ✗ Timeout — status nie osiągnął 200 po {Max} próbach", maxAttempts);
                return new AuthResult
                {
                    Success = false,
                    Error = $"Timeout autoryzacji — serwer KSeF nie potwierdził autoryzacji po {maxAttempts} próbach"
                };
            }

            _logger.LogInformation("--- Krok 6: POST /auth/token/redeem ---");
            using var redeemRequest = new HttpRequestMessage(HttpMethod.Post, "auth/token/redeem");
            redeemRequest.Headers.Add("Authorization", $"Bearer {finalToken}");
            redeemRequest.Content = new StringContent("{}", Encoding.UTF8, "application/json");

            var redeemResponse = await client.SendAsync(redeemRequest, ct);
            var redeemContent = await redeemResponse.Content.ReadAsStringAsync(ct);

            _logger.LogInformation("  Response status: {Status} ({Code})",
                redeemResponse.StatusCode, (int)redeemResponse.StatusCode);
            _logger.LogInformation("  Response body: {Body}", SanitizeForLog(redeemContent));

            if (!redeemResponse.IsSuccessStatusCode)
            {
                var errMsg = ExtractKsefError(redeemContent, $"HTTP {(int)redeemResponse.StatusCode}");
                _logger.LogError("  ✗ Błąd token/redeem: {Err}", errMsg);
                return new AuthResult { Success = false, Error = $"Błąd pobierania tokenów: {errMsg}" };
            }

            var tokens = SafeDeserialize<TokenRedeemResponse>(redeemContent, "token/redeem");
            if (tokens?.AccessToken == null || string.IsNullOrEmpty(tokens.AccessToken.Token))
            {
                _logger.LogError("  ✗ Brak accessToken w redeem response: {Body}", redeemContent);
                return new AuthResult
                {
                    Success = false,
                    Error = "Brak accessToken w odpowiedzi token/redeem"
                };
            }

            _session.SetAuthSession(nip, tokens);

            _logger.LogInformation("═══════════════════════════════════════════════════════════════");
            _logger.LogInformation("  ✅ ZALOGOWANO POMYŚLNIE!");
            _logger.LogInformation("  AccessToken ważny do: {Until}", tokens.AccessToken.ValidUntil);
            _logger.LogInformation("  RefreshToken ważny do: {Until}", tokens.RefreshToken?.ValidUntil);
            _logger.LogInformation("═══════════════════════════════════════════════════════════════");

            return new AuthResult
            {
                Success = true,
                ReferenceNumber = referenceNumber,
                AccessTokenValidUntil = tokens.AccessToken.ValidUntil,
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
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "Timeout połączenia z KSeF");
            return new AuthResult { Success = false, Error = "Timeout — serwer KSeF nie odpowiedział w czasie" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Nieoczekiwany błąd logowania do KSeF");
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
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");

            var response = await client.SendAsync(request, ct);
            var content = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Błąd refresh: {Content}", content);
                return false;
            }

            var tokens = SafeDeserialize<TokenRefreshResponse>(content, "token/refresh");
            if (tokens == null) return false;

            _session.UpdateAccessToken(tokens);
            _logger.LogInformation("AccessToken odświeżony do: {Until}", tokens.AccessToken?.ValidUntil);
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
        _logger.LogInformation("Wylogowano — sesja wyczyszczona");
    }

    #region Private Helpers

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
        var contentType = response.Content.Headers.ContentType?.MediaType ?? "unknown";

        _logger.LogInformation("  Response status: {Status} ({Code})", response.StatusCode, (int)response.StatusCode);
        _logger.LogInformation("  Content-Type: {CT}", contentType);
        _logger.LogDebug("  Body: {Body}", content.Length > 500 ? content[..500] + "..." : content);

        if (response.StatusCode == System.Net.HttpStatusCode.Redirect ||
            response.StatusCode == System.Net.HttpStatusCode.Found ||
            response.StatusCode == System.Net.HttpStatusCode.MovedPermanently)
        {
            var location = response.Headers.Location?.ToString() ?? "unknown";
            throw new KSeFApiException(
                $"API KSeF przekierowuje na {location}. Sprawdź URL — powinno być: api-test.ksef.mf.gov.pl/v2/",
                response.StatusCode, content);
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new KSeFApiException(
                $"Błąd pobierania certyfikatów: HTTP {(int)response.StatusCode} — {ExtractKsefError(content, "")}",
                response.StatusCode, content);
        }

        if (!contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
        {
            var preview = content.Length > 300 ? content[..300] + "..." : content;
            throw new KSeFApiException(
                $"API KSeF zwróciło {contentType} zamiast JSON. Sprawdź URL: https://api-test.ksef.mf.gov.pl/v2/",
                response.StatusCode, preview);
        }

        List<CertificateInfo>? certificates = null;
        try
        {
            certificates = JsonSerializer.Deserialize<List<CertificateInfo>>(content, _jsonOptions);
        }
        catch (JsonException)
        {
            try
            {
                var node = JsonNode.Parse(content);
                var arr = node?["certificates"] ?? node?["data"] ?? node?["items"];
                if (arr != null)
                    certificates = arr.Deserialize<List<CertificateInfo>>(_jsonOptions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "  ✗ Nie można sparsować certyfikatów");
            }
        }

        if (certificates == null || certificates.Count == 0)
        {
            throw new KSeFApiException(
                "API KSeF zwróciło pustą listę certyfikatów lub nieoczekiwany format",
                response.StatusCode, content);
        }

        _logger.LogInformation("  ✓ Pobrano {Count} certyfikat(ów)", certificates.Count);
        foreach (var cert in certificates)
        {
            _logger.LogInformation("    Usage: [{U}] | ValidTo: {VT}",
                string.Join(", ", cert.Usage ?? new List<string>()), cert.ValidTo);
        }

        _session.SetCertificates(certificates);
        return certificates;
    }

    private async Task<(ChallengeResponse Challenge, long TimestampMs)> GetChallengeWithTimestampAsync(
        HttpClient client, CancellationToken ct)
    {
        var response = await client.PostAsync(
            "auth/challenge",
            new StringContent("{}", Encoding.UTF8, "application/json"),
            ct);

        var content = await response.Content.ReadAsStringAsync(ct);
        _logger.LogInformation("  Response status: {Status} ({Code})", response.StatusCode, (int)response.StatusCode);
        _logger.LogDebug("  Body: {Body}", content);

        if (!response.IsSuccessStatusCode)
        {
            throw new KSeFApiException(
                $"Błąd pobierania challenge: HTTP {(int)response.StatusCode} — {ExtractKsefError(content, "")}",
                response.StatusCode, content);
        }

        var challengeResponse = SafeDeserialize<ChallengeResponse>(content, "auth/challenge");
        if (challengeResponse == null || string.IsNullOrEmpty(challengeResponse.Challenge))
            throw new KSeFApiException("Brak pola 'challenge' w odpowiedzi", response.StatusCode, content);

        long timestampMs = 0;
        try
        {
            var node = JsonNode.Parse(content);
            var tsMs = node?["timestampMs"];
            if (tsMs != null)
                timestampMs = tsMs.GetValue<long>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Nie można pobrać timestampMs z JSON — używam timestamp");
        }

        if (timestampMs == 0)
        {
            var timestampUtc = challengeResponse.Timestamp.ToUniversalTime();
            timestampMs = new DateTimeOffset(timestampUtc).ToUnixTimeMilliseconds();
            _logger.LogWarning("timestampMs nie znaleziono w JSON — obliczono z timestamp: {Ms}", timestampMs);
        }

        return (challengeResponse, timestampMs);
    }

    private T? SafeDeserialize<T>(string json, string context)
    {
        if (string.IsNullOrWhiteSpace(json)) return default;

        var trimmed = json.TrimStart();
        if (trimmed.StartsWith('<'))
        {
            throw new KSeFApiException(
                $"Endpoint {context} zwrócił HTML zamiast JSON",
                null, json.Length > 200 ? json[..200] : json);
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json, _jsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogError("  ✗ Błąd JSON parse ({Context}): {Msg}", context, ex.Message);
            return default;
        }
    }

    private static string ExtractKsefError(string responseBody, string fallback)
    {
        if (string.IsNullOrWhiteSpace(responseBody)) return fallback;

        try
        {
            var node = JsonNode.Parse(responseBody);
            if (node == null) return fallback;

            var exceptionDetails = node["exception"]?["exceptionDetailList"];
            if (exceptionDetails is JsonArray arr && arr.Count > 0)
            {
                var first = arr[0];
                var desc = first?["exceptionDescription"]?.GetValue<string>();
                var details = first?["details"] as JsonArray;
                var detailStr = details?.Count > 0
                    ? " — " + string.Join("; ", details.Select(d => d?.GetValue<string>() ?? ""))
                    : "";
                if (!string.IsNullOrEmpty(desc))
                    return desc + detailStr;
            }

            var msg = node["message"]?.GetValue<string>()
                ?? node["error"]?.GetValue<string>()
                ?? node["description"]?.GetValue<string>();

            return !string.IsNullOrEmpty(msg) ? msg : fallback;
        }
        catch
        {
            return responseBody.Length > 300 ? responseBody[..300] : responseBody;
        }
    }

    private static string SanitizeForLog(string content)
    {
        if (string.IsNullOrEmpty(content)) return "(empty)";
        return content.Length > 600 ? content[..600] + "... [truncated]" : content;
    }

    #endregion
}

public class KSeFApiException : Exception
{
    public System.Net.HttpStatusCode? StatusCode { get; }
    public string? RawResponse { get; }

    public KSeFApiException(
        string message,
        System.Net.HttpStatusCode? statusCode = null,
        string? rawResponse = null)
        : base(message)
    {
        StatusCode = statusCode;
        RawResponse = rawResponse;
    }
}