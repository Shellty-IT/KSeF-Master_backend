namespace KSeF.Backend.Services.Interfaces.KSeF;

public interface IKSeFChallengeService
{
    Task<(string challenge, long timestampMs)> GetChallengeAsync(
        HttpClient client,
        CancellationToken ct = default);
}