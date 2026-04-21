using KSeF.Backend.Models.Responses.Stats;

namespace KSeF.Backend.Services.Interfaces;

public interface IKSeFInvoiceStatsService
{
    Task<InvoiceStatsResponse> GetStatsAsync(int months, CancellationToken cancellationToken = default);
}