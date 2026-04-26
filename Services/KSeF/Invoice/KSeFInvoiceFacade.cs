// Services/KSeF/Invoice/KSeFInvoiceFacade.cs
using KSeF.Backend.Models.Requests;
using KSeF.Backend.Models.Responses.Invoice;
using KSeF.Backend.Models.Responses.Stats;
using KSeF.Backend.Models.Responses.Common;
using KSeF.Backend.Services.Interfaces.KSeF;
using KSeF.Backend.Services.KSeF.Session;

namespace KSeF.Backend.Services.KSeF.Invoice;

public class KSeFInvoiceFacade : IKSeFInvoiceService
{
    private readonly IKSeFInvoiceQueryService _queryService;
    private readonly IKSeFInvoiceDetailsService _detailsService;
    private readonly IKSeFInvoiceStatsService _statsService;
    private readonly IKSeFOnlineSessionService _sessionService;
    private readonly IKSeFInvoiceSendService _sendService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IKSeFEnvironmentService _environmentService;
    private readonly KSeFSessionManager _sessionManager;

    public KSeFInvoiceFacade(
        IKSeFInvoiceQueryService queryService,
        IKSeFInvoiceDetailsService detailsService,
        IKSeFInvoiceStatsService statsService,
        IKSeFOnlineSessionService sessionService,
        IKSeFInvoiceSendService sendService,
        IHttpClientFactory httpClientFactory,
        IKSeFEnvironmentService environmentService,
        KSeFSessionManager sessionManager)
    {
        _queryService = queryService;
        _detailsService = detailsService;
        _statsService = statsService;
        _sessionService = sessionService;
        _sendService = sendService;
        _httpClientFactory = httpClientFactory;
        _environmentService = environmentService;
        _sessionManager = sessionManager;
    }

    public async Task<InvoiceQueryResponse> GetInvoicesAsync(InvoiceQueryRequest request, CancellationToken ct = default)
    {
        if (!_sessionManager.IsAuthenticated)
            throw new UnauthorizedAccessException("Brak aktywnej sesji KSeF");

        var client = CreateAuthenticatedClient();
        return await _queryService.QueryInvoicesAsync(client, request, ct);
    }

    public async Task<InvoiceSyncResult> SyncInvoicesAsync(
        int companyProfileId,
        string nip,
        string environment,
        string direction,
        CancellationToken ct = default)
    {
        if (!_sessionManager.IsAuthenticated)
            throw new UnauthorizedAccessException("Brak aktywnej sesji KSeF");

        return await _queryService.SyncInvoicesAsync(companyProfileId, nip, environment, direction, ct);
    }

    public Task<InvoiceDetailsResult> GetInvoiceDetailsAsync(string ksefNumber, CancellationToken ct = default)
        => _detailsService.GetInvoiceDetailsAsync(ksefNumber, ct);

    public Task<InvoiceStatsResponse> GetInvoiceStatsAsync(int months = 3, CancellationToken ct = default)
        => _statsService.GetStatsAsync(months, ct);

    public Task<SessionResult> OpenOnlineSessionAsync(CancellationToken ct = default)
        => _sessionService.OpenOnlineSessionAsync(ct);

    public Task<SendInvoiceResult> SendInvoiceAsync(CreateInvoiceRequest invoiceData, CancellationToken ct = default)
        => _sendService.SendInvoiceAsync(invoiceData, ct);

    private HttpClient CreateAuthenticatedClient()
    {
        var client = _httpClientFactory.CreateClient("KSeF");
        client.BaseAddress = new Uri(_environmentService.GetApiBaseUrl("Test"));
        client.DefaultRequestHeaders.Add("SessionToken", _sessionManager.AccessToken);
        return client;
    }
}