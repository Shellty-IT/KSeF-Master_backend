// Services/Interfaces/IKSeFOnlineSessionService.cs
using KSeF.Backend.Models.Responses.Common;

namespace KSeF.Backend.Services.Interfaces;

public interface IKSeFOnlineSessionService
{
    Task<SessionResult> OpenOnlineSessionAsync(CancellationToken ct = default);
}