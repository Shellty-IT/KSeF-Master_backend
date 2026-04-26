// Services/KSeF/Invoice/KSeFInvoiceSendService.cs
using System.Text;
using System.Text.Json;
using KSeF.Backend.Infrastructure.KSeF;
using KSeF.Backend.Models.Requests;
using KSeF.Backend.Models.Responses.Invoice;
using KSeF.Backend.Models.Responses.Common;
using KSeF.Backend.Services.Interfaces.KSeF;
using KSeF.Backend.Services.KSeF.Session;
using KSeF.Backend.Services.Invoice;

namespace KSeF.Backend.Services.KSeF.Invoice;

public class KSeFInvoiceSendService : IKSeFInvoiceSendService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IKSeFEnvironmentService _environmentService;
    private readonly IKSeFAuthService _authService;
    private readonly IKSeFCryptoService _cryptoService;
    private readonly IKSeFOnlineSessionService _sessionService;
    private readonly KSeFSessionManager _session;
    private readonly InvoiceXmlGenerator _xmlGenerator;
    private readonly ILogger<KSeFInvoiceSendService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public KSeFInvoiceSendService(
        IHttpClientFactory httpClientFactory,
        IKSeFEnvironmentService environmentService,
        IKSeFAuthService authService,
        IKSeFCryptoService cryptoService,
        IKSeFOnlineSessionService sessionService,
        KSeFSessionManager session,
        InvoiceXmlGenerator xmlGenerator,
        ILogger<KSeFInvoiceSendService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _environmentService = environmentService;
        _authService = authService;
        _cryptoService = cryptoService;
        _sessionService = sessionService;
        _session = session;
        _xmlGenerator = xmlGenerator;
        _logger = logger;
    }

    public async Task<SendInvoiceResult> SendInvoiceAsync(
        CreateInvoiceRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!_session.IsAuthenticated)
            throw new UnauthorizedAccessException("Brak aktywnej sesji KSeF");

        await _authService.RefreshTokenIfNeededAsync(cancellationToken);

        var sessionNip = _session.Nip;
        if (!string.IsNullOrEmpty(sessionNip) && request.Seller != null && request.Seller.Nip != sessionNip)
            request.Seller.Nip = sessionNip;

        if (!_session.HasActiveOnlineSession)
        {
            _logger.LogInformation("Brak aktywnej sesji wysyłkowej — otwieram automatycznie");
            var sessionResult = await _sessionService.OpenOnlineSessionAsync(cancellationToken);
            if (!sessionResult.Success)
                return Fail($"Nie można otworzyć sesji wysyłkowej: {sessionResult.Error}");
        }

        try
        {
            var invoiceXml = _xmlGenerator.Generate(request);
            var invoiceBytes = new UTF8Encoding(false).GetBytes(invoiceXml);
            var invoiceHash = _cryptoService.ComputeSha256Base64(invoiceBytes);

            var encryptedInvoice = _cryptoService.EncryptInvoiceXml(invoiceXml, _session.AesKey!, _session.Iv!);
            var encryptedInvoiceHash = _cryptoService.ComputeSha256Base64(encryptedInvoice);

            var requestBody = new
            {
                invoiceHash,
                invoiceSize = invoiceBytes.Length,
                encryptedInvoiceHash,
                encryptedInvoiceSize = encryptedInvoice.Length,
                encryptedInvoiceContent = Convert.ToBase64String(encryptedInvoice),
                offlineMode = false
            };

            var url = $"sessions/online/{_session.SessionReferenceNumber}/invoices";

            _logger.LogInformation("Wysyłanie faktury {Number} do sesji {Ref}",
                request.InvoiceNumber, _session.SessionReferenceNumber);

            var content = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json");

            var response = await SendAuthorizedAsync(HttpMethod.Post, url, content, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            _logger.LogInformation("Send invoice response: {Status}", response.StatusCode);

            if (!response.IsSuccessStatusCode)
            {
                var error = KSeFErrorParser.Parse(responseBody);
                _logger.LogError("Send invoice error: {Error} | Body: {Body}", error, responseBody);
                return Fail($"Błąd wysyłania faktury: {error}");
            }

            var apiResponse = JsonSerializer.Deserialize<SendInvoiceApiResponse>(responseBody, JsonOptions);

            return new SendInvoiceResult
            {
                Success = true,
                ElementReferenceNumber = apiResponse?.ElementReferenceNumber ?? apiResponse?.ReferenceNumber,
                ProcessingCode = apiResponse?.ProcessingCode,
                ProcessingDescription = apiResponse?.ProcessingDescription,
                InvoiceHash = invoiceHash
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Błąd wysyłania faktury: {Number}", request.InvoiceNumber);
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

    private static SendInvoiceResult Fail(string error) =>
        new() { Success = false, Error = error };
}