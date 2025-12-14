using System.Text;
using System.Text.Json;
using KSeF.Backend.Models.Requests;
using KSeF.Backend.Models.Responses;
using KSeF.Backend.Services.Interfaces;

namespace KSeF.Backend.Services;

public class KSeFInvoiceService : IKSeFInvoiceService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IKSeFAuthService _authService;
    private readonly IKSeFCryptoService _cryptoService;
    private readonly KSeFSessionManager _session;
    private readonly InvoiceXmlGenerator _xmlGenerator;
    private readonly ILogger<KSeFInvoiceService> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public KSeFInvoiceService(
        IHttpClientFactory httpClientFactory,
        IKSeFAuthService authService,
        IKSeFCryptoService cryptoService,
        KSeFSessionManager session,
        InvoiceXmlGenerator xmlGenerator,
        ILogger<KSeFInvoiceService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _authService = authService;
        _cryptoService = cryptoService;
        _session = session;
        _xmlGenerator = xmlGenerator;
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };
    }

    private HttpClient CreateClient() => _httpClientFactory.CreateClient("KSeF");

    #region Get Invoices

    public async Task<InvoiceQueryResponse> GetInvoicesAsync(InvoiceQueryRequest request, CancellationToken ct = default)
    {
        EnsureAuthenticated();
        await _authService.RefreshTokenIfNeededAsync(ct);

        _logger.LogInformation("Pobieranie faktur...");
        _logger.LogDebug("Request: {@Request}", request);

        var client = CreateClient();

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "invoices/query/metadata");
        httpRequest.Headers.Add("Authorization", $"Bearer {_session.AccessToken}");
        httpRequest.Content = new StringContent(
            JsonSerializer.Serialize(request, _jsonOptions),
            Encoding.UTF8,
            "application/json");

        var response = await client.SendAsync(httpRequest, ct);
        var content = await response.Content.ReadAsStringAsync(ct);

        _logger.LogInformation("Response: {Status}", response.StatusCode);

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Błąd pobierania faktur: {response.StatusCode} - {content}");

        var result = JsonSerializer.Deserialize<InvoiceQueryResponse>(content, _jsonOptions)!;
        _logger.LogInformation("Pobrano {Count} faktur", result.Invoices.Count);

        return result;
    }

    #endregion

    #region Open Online Session

    public async Task<SessionResult> OpenOnlineSessionAsync(CancellationToken ct = default)
    {
        EnsureAuthenticated();
        await _authService.RefreshTokenIfNeededAsync(ct);

        // Sprawdź czy jest już aktywna sesja
        if (_session.HasActiveOnlineSession)
        {
            _logger.LogInformation("Używam istniejącej sesji: {Ref}", _session.SessionReferenceNumber);
            return new SessionResult
            {
                Success = true,
                SessionReferenceNumber = _session.SessionReferenceNumber,
                ValidUntil = _session.SessionValidUntil
            };
        }

        try
        {
            _logger.LogInformation("═══════════════════════════════════════════════════════════════");
            _logger.LogInformation("  OTWIERANIE SESJI INTERAKTYWNEJ");
            _logger.LogInformation("═══════════════════════════════════════════════════════════════");

            var client = CreateClient();

            // Pobierz certyfikat SymmetricKeyEncryption
            _logger.LogInformation("Krok 1: Pobieranie certyfikatu SymmetricKeyEncryption...");
            var certificates = _session.GetCachedCertificates()
                ?? await GetCertificatesAsync(client, ct);

            var symCert = certificates.First(c => c.Usage?.Contains("SymmetricKeyEncryption") == true);
            _logger.LogInformation("  ✓ Certyfikat pobrany");

            // Generuj klucz AES i IV
            _logger.LogInformation("Krok 2: Generowanie klucza AES-256 i IV...");
            var (aesKey, iv) = _cryptoService.GenerateAesKeyAndIv();
            _logger.LogInformation("  ✓ Klucz AES: {KeyLen} bajtów, IV: {IvLen} bajtów", aesKey.Length, iv.Length);

            // Zaszyfruj klucz AES certyfikatem
            _logger.LogInformation("Krok 3: Szyfrowanie klucza AES (RSA-OAEP SHA-256)...");
            var encryptedSymmetricKey = _cryptoService.EncryptAesKey(aesKey, symCert.Certificate);
            var ivBase64 = Convert.ToBase64String(iv);
            _logger.LogInformation("  ✓ Klucz zaszyfrowany");

            // POST /sessions/online
            _logger.LogInformation("Krok 4: POST /sessions/online");
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
                    initializationVector = ivBase64
                }
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, "sessions/online");
            request.Headers.Add("Authorization", $"Bearer {_session.AccessToken}");
            request.Content = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json");

            var response = await client.SendAsync(request, ct);
            var content = await response.Content.ReadAsStringAsync(ct);

            _logger.LogInformation("  Response: {Status}", response.StatusCode);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("  ✗ Błąd: {Content}", content);
                return new SessionResult { Success = false, Error = content };
            }

            var sessionResponse = JsonSerializer.Deserialize<OpenSessionResponse>(content, _jsonOptions)!;

            // Zapisz sesję
            _session.SetOnlineSession(
                sessionResponse.ReferenceNumber,
                sessionResponse.ValidUntil,
                aesKey,
                iv);

            _logger.LogInformation("═══════════════════════════════════════════════════════════════");
            _logger.LogInformation("  ✅ SESJA OTWARTA!");
            _logger.LogInformation("  ReferenceNumber: {Ref}", sessionResponse.ReferenceNumber);
            _logger.LogInformation("  ValidUntil: {Until}", sessionResponse.ValidUntil);
            _logger.LogInformation("═══════════════════════════════════════════════════════════════");

            return new SessionResult
            {
                Success = true,
                SessionReferenceNumber = sessionResponse.ReferenceNumber,
                ValidUntil = sessionResponse.ValidUntil
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Błąd otwierania sesji");
            return new SessionResult { Success = false, Error = ex.Message };
        }
    }

    #endregion

    #region Send Invoice

    public async Task<SendInvoiceResult> SendInvoiceAsync(CreateInvoiceRequest invoiceData, CancellationToken ct = default)
    {
        EnsureAuthenticated();
        await _authService.RefreshTokenIfNeededAsync(ct);

        // Upewnij się że jest otwarta sesja
        if (!_session.HasActiveOnlineSession)
        {
            var sessionResult = await OpenOnlineSessionAsync(ct);
            if (!sessionResult.Success)
                return new SendInvoiceResult { Success = false, Error = $"Nie można otworzyć sesji: {sessionResult.Error}" };
        }

        try
        {
            _logger.LogInformation("═══════════════════════════════════════════════════════════════");
            _logger.LogInformation("  WYSYŁANIE FAKTURY: {Number}", invoiceData.InvoiceNumber);
            _logger.LogInformation("═══════════════════════════════════════════════════════════════");

            var client = CreateClient();
            var aesKey = _session.AesKey!;
            var iv = _session.Iv!;
            var sessionRef = _session.SessionReferenceNumber!;

            // Krok 1: Wygeneruj XML
            _logger.LogInformation("Krok 1: Generowanie XML faktury...");
            var invoiceXml = _xmlGenerator.GenerateInvoiceXml(invoiceData);
            var invoiceBytes = new UTF8Encoding(false).GetBytes(invoiceXml);
            _logger.LogInformation("  ✓ XML wygenerowany: {Size} bajtów", invoiceBytes.Length);

            // Krok 2: Oblicz hash oryginalnej faktury
            _logger.LogInformation("Krok 2: Obliczanie hash SHA-256 faktury...");
            var invoiceHash = _cryptoService.ComputeSha256Base64(invoiceBytes);
            _logger.LogInformation("  ✓ Hash: {Hash}", invoiceHash);

            // Krok 3: Zaszyfruj fakturę AES-256-CBC
            _logger.LogInformation("Krok 3: Szyfrowanie faktury (AES-256-CBC)...");
            var encryptedInvoice = _cryptoService.EncryptInvoiceXml(invoiceXml, aesKey, iv);
            var encryptedInvoiceHash = _cryptoService.ComputeSha256Base64(encryptedInvoice);
            var encryptedInvoiceBase64 = Convert.ToBase64String(encryptedInvoice);
            _logger.LogInformation("  ✓ Zaszyfrowano: {OrigSize} -> {EncSize} bajtów",
                invoiceBytes.Length, encryptedInvoice.Length);

            // Krok 4: Wyślij fakturę
            _logger.LogInformation("Krok 4: POST /sessions/online/{Ref}/invoices", sessionRef);
            var requestBody = new
            {
                invoiceHash,
                invoiceSize = invoiceBytes.Length,
                encryptedInvoiceHash,
                encryptedInvoiceSize = encryptedInvoice.Length,
                encryptedInvoiceContent = encryptedInvoiceBase64,
                offlineMode = false
            };

            var url = $"sessions/online/{sessionRef}/invoices";

            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Add("Authorization", $"Bearer {_session.AccessToken}");
            request.Content = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json");

            var response = await client.SendAsync(request, ct);
            var content = await response.Content.ReadAsStringAsync(ct);

            _logger.LogInformation("  Response: {Status}", response.StatusCode);
            _logger.LogDebug("  Content: {Content}", content);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("  ✗ Błąd: {Content}", content);
                return new SendInvoiceResult { Success = false, Error = content };
            }

            var sendResponse = JsonSerializer.Deserialize<SendInvoiceApiResponse>(content, _jsonOptions);

            _logger.LogInformation("═══════════════════════════════════════════════════════════════");
            _logger.LogInformation("  ✅ FAKTURA WYSŁANA!");
            _logger.LogInformation("  ElementReferenceNumber: {Ref}", sendResponse?.ElementReferenceNumber);
            _logger.LogInformation("  ProcessingCode: {Code}", sendResponse?.ProcessingCode);
            _logger.LogInformation("═══════════════════════════════════════════════════════════════");

            return new SendInvoiceResult
            {
                Success = true,
                ElementReferenceNumber = sendResponse?.ElementReferenceNumber,
                ProcessingCode = sendResponse?.ProcessingCode,
                ProcessingDescription = sendResponse?.ProcessingDescription
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Błąd wysyłania faktury");
            return new SendInvoiceResult { Success = false, Error = ex.Message };
        }
    }

    #endregion

    #region Helpers

    private void EnsureAuthenticated()
    {
        if (!_session.IsAuthenticated)
            throw new UnauthorizedAccessException("Nie jesteś zalogowany. Użyj POST /api/ksef/login");
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

    #endregion
}