// Services/Interfaces/IKSeFInvoiceService.cs
using KSeF.Backend.Models.Requests;
using KSeF.Backend.Models.Responses;

namespace KSeF.Backend.Services.Interfaces;

public interface IKSeFInvoiceService
{
    Task<InvoiceQueryResponse> GetInvoicesAsync(InvoiceQueryRequest request, CancellationToken ct = default);

    Task<InvoiceStatsResponse> GetInvoiceStatsAsync(int months = 3, CancellationToken ct = default);

    Task<SessionResult> OpenOnlineSessionAsync(CancellationToken ct = default);

    Task<SendInvoiceResult> SendInvoiceAsync(CreateInvoiceRequest invoiceData, CancellationToken ct = default);

    Task<bool> CloseOnlineSessionAsync(CancellationToken ct = default);

    Task<InvoiceDetailsResult> GetInvoiceDetailsAsync(string ksefNumber, CancellationToken ct = default);
}