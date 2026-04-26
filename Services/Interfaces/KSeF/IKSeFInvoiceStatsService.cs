using KSeF.Backend.Models.Responses.Stats;

namespace KSeF.Backend.Services.Interfaces.KSeF;

public interface IKSeFInvoiceStatsService
{
    Task<InvoiceStatsResponse> GetStatsAsync(int months, CancellationToken cancellationToken = default);
}