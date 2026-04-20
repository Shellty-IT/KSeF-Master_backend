// Services/Interfaces/IKSeFInvoiceStatsService.cs
using KSeF.Backend.Models.Responses;

namespace KSeF.Backend.Services.Interfaces;

public interface IKSeFInvoiceStatsService
{
    Task<InvoiceStatsResponse> GetInvoiceStatsAsync(int months = 3, CancellationToken ct = default);
}