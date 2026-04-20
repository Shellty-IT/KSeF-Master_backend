// Services/KSeF/Invoice/KSeFInvoiceSendService.cs
using System.Text;
using System.Text.Json;
using KSeF.Backend.Infrastructure.KSeF;
using KSeF.Backend.Models.Requests;
using KSeF.Backend.Models.Responses;
using KSeF.Backend.Services.Interfaces;

namespace KSeF.Backend.Services.KSeF.Invoice;

public class KSeFInvoiceSendService : IKSeFInvoiceSendService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IKSeFAuthService _authService;
    private readonly IKSeFCryptoService _cryptoService;
    private readonly IKSeFOnlineSessionService _sessionService;
    private readonly KSeFSessionManager _session;
    private readonly InvoiceXmlGenerator _xmlGenerator;
    private readonly ILogger<KSeFInvoiceSendService> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public KSeFInvoiceSendService(
        IHttpClientFactory httpClientFactory,
        IKSeFAuthService authService,
        IKSeFCryptoService cryptoService,
        IKSeFOnlineSessionService sessionService,
        KSeFSessionManager session,
        InvoiceXmlGenerator xmlGenerator,
        ILogger<KSeFInvoiceSendService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _authService = authService;
        _cryptoService = cryptoService;
        _sessionService = sessionService;
        _session = session;
        _xmlGenerator = xmlGenerator;
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    public async Task<SendInvoiceResult> SendInvoiceAsync(CreateInvoiceRequest invoiceData, CancellationToken ct = default)
    {
        if (!_session.IsAuthenticated)
            throw new UnauthorizedAccessException("Nie jesteś zalogowany do KSeF");

        await _authService.RefreshTokenIfNeededAsync(ct);

        var sessionNip = _session.Nip;
        if (!string.IsNullOrEmpty(sessionNip) && invoiceData.Seller.Nip != sessionNip)
            invoiceData.Seller.Nip = sessionNip;

        if (!_session.HasActiveOnlineSession)
        {
            var sessionResult = await _sessionService.OpenOnlineSessionAsync(ct);
            if (!sessionResult.Success)
                return new SendInvoiceResult { Success = false, Error = $"Nie można otworzyć sesji: {sessionResult.Error}" };
        }

        try
        {
            var client = _httpClientFactory.CreateClient("KSeF");
            var invoiceXml = _xmlGenerator.GenerateInvoiceXml(invoiceData, sessionNip);
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

            using var request = new HttpRequestMessage(HttpMethod.Post,
                $"sessions/online/{_session.SessionReferenceNumber}/invoices");
            request.Headers.Add("Authorization", $"Bearer {_session.AccessToken}");
            request.Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

            var response = await client.SendAsync(request, ct);
            var content = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
                return new SendInvoiceResult { Success = false, Error = KSeFErrorParser.ParseInvoiceError(content) ?? content };

            var sendResponse = JsonSerializer.Deserialize<SendInvoiceApiResponse>(content, _jsonOptions);

            return new SendInvoiceResult
            {
                Success = true,
                ElementReferenceNumber = sendResponse?.ElementReferenceNumber,
                ProcessingCode = sendResponse?.ProcessingCode,
                ProcessingDescription = sendResponse?.ProcessingDescription,
                InvoiceHash = invoiceHash
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Błąd wysyłania faktury");
            return new SendInvoiceResult { Success = false, Error = ex.Message };
        }
    }
}