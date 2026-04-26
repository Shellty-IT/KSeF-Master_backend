// Services/Interfaces/External/IExternalDraftService.cs
using KSeF.Backend.Models;
using KSeF.Backend.Models.Requests;

namespace KSeF.Backend.Services.Interfaces.External;

public interface IExternalDraftService
{
    ExternalDraft? GetById(string id);
    ExternalDraft? GetBySmartQuoteId(string smartQuoteId);
    List<ExternalDraft> GetAll(string? statusFilter = null);
    ExternalDraft Import(SmartQuoteImportRequest request);
    bool ExistsBySmartQuoteId(string smartQuoteId);
    ExternalDraft? Approve(string id, string processedBy);
    ExternalDraft? Reject(string id, string processedBy, string reason);
    List<string> Validate(SmartQuoteImportRequest request);
}