// Services/KSeF/Invoice/KSeFInvoiceFacade.cs
using KSeF.Backend.Models.Requests;
using KSeF.Backend.Models.Responses;
using KSeF.Backend.Services.Interfaces;

namespace KSeF.Backend.Services.KSeF.Invoice;

public class KSeFInvoiceFacade : IKSeFInvoiceService
{
    private readonly IKSeFInvoiceQueryService _queryService;
    private readonly IKSeFInvoiceDetailsService _detailsService;
    private readonly IKSeFInvoiceStatsService _statsService;
    private readonly IKSeFOnlineSessionService _sessionService;
    private readonly IKSeFInvoiceSendService _sendService;

    public KSeFInvoiceFacade(
        IKSeFInvoiceQueryService queryService,
        IKSeFInvoiceDetailsService detailsService,
        IKSeFInvoiceStatsService statsService,
        IKSeFOnlineSessionService sessionService,
        IKSeFInvoiceSendService sendService)
    {
        _queryService = queryService;
        _detailsService = detailsService;
        _statsService = statsService;
        _sessionService = sessionService;
        _sendService = sendService;
    }

    public Task<InvoiceQueryResponse> GetInvoicesAsync(InvoiceQueryRequest request, CancellationToken ct = default)
        => _queryService.GetInvoicesAsync(request, ct);

    public Task<InvoiceDetailsResult> GetInvoiceDetailsAsync(string ksefNumber, CancellationToken ct = default)
        => _detailsService.GetInvoiceDetailsAsync(ksefNumber, ct);

    public Task<InvoiceStatsResponse> GetInvoiceStatsAsync(int months = 3, CancellationToken ct = default)
        => _statsService.GetInvoiceStatsAsync(months, ct);

    public Task<SessionResult> OpenOnlineSessionAsync(CancellationToken ct = default)
        => _sessionService.OpenOnlineSessionAsync(ct);

    public Task<bool> CloseOnlineSessionAsync(CancellationToken ct = default)
        => _sessionService.CloseOnlineSessionAsync(ct);

    public Task<SendInvoiceResult> SendInvoiceAsync(CreateInvoiceRequest invoiceData, CancellationToken ct = default)
        => _sendService.SendInvoiceAsync(invoiceData, ct);
}