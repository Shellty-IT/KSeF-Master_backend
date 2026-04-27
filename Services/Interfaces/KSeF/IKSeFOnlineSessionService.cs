using KSeF.Backend.Models.Responses.Common;
using KSeF.Backend.Services.KSeF.Invoice;

namespace KSeF.Backend.Services.Interfaces.KSeF;

public interface IKSeFOnlineSessionService
{
    Task<SessionResult> OpenOnlineSessionAsync(CancellationToken ct = default);
    Task<SessionResult> CloseOnlineSessionAsync(CancellationToken ct = default);
    Task<SessionUpoResult> CloseSessionAndFetchUpoAsync(CancellationToken ct = default);
}