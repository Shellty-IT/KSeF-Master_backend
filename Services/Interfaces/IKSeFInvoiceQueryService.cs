// Services/Interfaces/IKSeFInvoiceQueryService.cs
using KSeF.Backend.Models.Requests;
using KSeF.Backend.Models.Responses;

namespace KSeF.Backend.Services.Interfaces;

public interface IKSeFInvoiceQueryService
{
    Task<InvoiceQueryResponse> GetInvoicesAsync(InvoiceQueryRequest request, CancellationToken ct = default);
}