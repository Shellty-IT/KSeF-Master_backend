// Services/Interfaces/IKSeFTokenRefreshService.cs
namespace KSeF.Backend.Services.Interfaces;

public interface IKSeFTokenRefreshService
{
    Task<bool> RefreshTokenIfNeededAsync(CancellationToken ct = default);
}