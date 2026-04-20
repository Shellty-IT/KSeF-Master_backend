// Services/Interfaces/IKSeFAuthPollingService.cs
namespace KSeF.Backend.Services.Interfaces;

public interface IKSeFAuthPollingService
{
    Task<string?> PollAuthStatusAsync(
        HttpClient client,
        string referenceNumber,
        string authenticationToken,
        CancellationToken ct = default);
}