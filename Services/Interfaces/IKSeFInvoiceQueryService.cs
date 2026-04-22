// Services/Interfaces/IKSeFInvoiceQueryService.cs
using KSeF.Backend.Models.Data;
using KSeF.Backend.Models.Requests;
using KSeF.Backend.Models.Responses.Invoice;
using InvoiceModel = KSeF.Backend.Models.Data.Invoice;

namespace KSeF.Backend.Services.Interfaces;

public interface IKSeFInvoiceQueryService
{
    Task<InvoiceQueryResponse> QueryInvoicesAsync(
        HttpClient client,
        InvoiceQueryRequest request,
        CancellationToken cancellationToken = default);

    Task<InvoiceSyncResult> SyncInvoicesAsync(
        int companyProfileId,
        string nip,
        string environment,
        string direction,
        CancellationToken cancellationToken = default);

    Task<List<InvoiceModel>> GetCachedInvoicesAsync(int companyProfileId);
}