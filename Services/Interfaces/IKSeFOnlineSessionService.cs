// Services/Interfaces/IKSeFOnlineSessionService.cs
using KSeF.Backend.Models.Responses;

namespace KSeF.Backend.Services.Interfaces;

public interface IKSeFOnlineSessionService
{
    Task<SessionResult> OpenOnlineSessionAsync(CancellationToken ct = default);
    Task<bool> CloseOnlineSessionAsync(CancellationToken ct = default);
}