using KSeF.Backend.Models.Requests;
using KSeF.Backend.Models.Responses.Invoice;
using KSeF.Backend.Models.Responses.Stats;
using KSeF.Backend.Models.Responses.Common;

namespace KSeF.Backend.Services.Interfaces;

public interface IKSeFInvoiceService
{
    Task<InvoiceQueryResponse> GetInvoicesAsync(InvoiceQueryRequest request, CancellationToken ct = default);

    Task<InvoiceStatsResponse> GetInvoiceStatsAsync(int months, CancellationToken ct = default);

    Task<SessionResult> OpenOnlineSessionAsync(CancellationToken ct = default);

    Task<SendInvoiceResult> SendInvoiceAsync(CreateInvoiceRequest request, CancellationToken ct = default);

    Task<InvoiceDetailsResult> GetInvoiceDetailsAsync(string ksefNumber, CancellationToken ct = default);
}