// Services/Interfaces/IKSeFChallengeService.cs
namespace KSeF.Backend.Services.Interfaces;

public interface IKSeFChallengeService
{
    Task<(string challenge, long timestampMs)> GetChallengeAsync(
        HttpClient client, 
        CancellationToken ct = default);
}