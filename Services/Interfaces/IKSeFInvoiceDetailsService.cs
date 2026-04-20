// Services/Interfaces/IKSeFInvoiceDetailsService.cs
using KSeF.Backend.Models.Responses;

namespace KSeF.Backend.Services.Interfaces;

public interface IKSeFInvoiceDetailsService
{
    Task<InvoiceDetailsResult> GetInvoiceDetailsAsync(string ksefNumber, CancellationToken ct = default);
}