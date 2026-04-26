namespace KSeF.Backend.Services.Interfaces.KSeF;

public interface IKSeFTokenRefreshService
{
    Task<bool> RefreshTokenIfNeededAsync(CancellationToken ct = default);
}