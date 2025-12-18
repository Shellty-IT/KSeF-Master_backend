using KSeF.Backend.Models.Requests;
using KSeF.Backend.Models.Responses;

namespace KSeF.Backend.Services.Interfaces;

public interface IKSeFInvoiceService
{
    /// <summary>
    /// Pobiera faktury z KSeF
    /// </summary>
    Task<InvoiceQueryResponse> GetInvoicesAsync(InvoiceQueryRequest request, CancellationToken ct = default);

    /// <summary>
    /// Otwiera sesję interaktywną do wysyłki faktur
    /// Generuje klucz AES, szyfruje go i wysyła do KSeF
    /// </summary>
    Task<SessionResult> OpenOnlineSessionAsync(CancellationToken ct = default);

    /// <summary>
    /// Wysyła fakturę - generuje XML na podstawie danych, szyfruje i wysyła
    /// </summary>
    Task<SendInvoiceResult> SendInvoiceAsync(CreateInvoiceRequest invoiceData, CancellationToken ct = default);

    /// <summary>
    /// Zamyka sesję interaktywną
    /// </summary>
    Task<bool> CloseOnlineSessionAsync(CancellationToken ct = default);

    /// <summary>
    /// Pobiera szczegóły faktury z KSeF (XML + hash)
    /// </summary>
    Task<InvoiceDetailsResult> GetInvoiceDetailsAsync(string ksefNumber, CancellationToken ct = default);
}