using KSeF.Backend.Models.Requests;
using KSeF.Backend.Models.Responses.Invoice;

namespace KSeF.Backend.Services.Interfaces;

public interface IKSeFInvoiceQueryService
{
    Task<InvoiceQueryResponse> QueryInvoicesAsync(
        HttpClient client,
        InvoiceQueryRequest request,
        CancellationToken cancellationToken = default);
}