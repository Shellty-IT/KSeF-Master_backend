using KSeF.Backend.Models.Requests;
using KSeF.Backend.Models.Responses.Common;

namespace KSeF.Backend.Services.Interfaces;

public interface IKSeFInvoiceSendService
{
    Task<SendInvoiceResult> SendInvoiceAsync(
        CreateInvoiceRequest request,
        CancellationToken cancellationToken = default);
}