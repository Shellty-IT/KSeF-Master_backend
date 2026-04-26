using KSeF.Backend.Models.Responses.Common;

namespace KSeF.Backend.Services.Interfaces.KSeF;

public interface IKSeFInvoiceDetailsService
{
    Task<InvoiceDetailsResult> GetInvoiceDetailsAsync(
        string ksefNumber,
        CancellationToken cancellationToken = default);
}