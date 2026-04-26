using System.Text;
using System.Text.Json.Nodes;
using KSeF.Backend.Infrastructure.KSeF;
using KSeF.Backend.Services.Interfaces.KSeF;

namespace KSeF.Backend.Services.KSeF.Auth;

public class KSeFChallengeService : IKSeFChallengeService
{
    private readonly ILogger<KSeFChallengeService> _logger;

    public KSeFChallengeService(ILogger<KSeFChallengeService> logger)
    {
        _logger = logger;
    }

    public async Task<(string challenge, long timestampMs)> GetChallengeAsync(
        HttpClient client,
        CancellationToken cancellationToken = default)
    {
        var request = new { ContextIdentifier = new { Type = "other" } };
        var content = new StringContent(
            System.Text.Json.JsonSerializer.Serialize(request),
            Encoding.UTF8,
            "application/json");

        var response = await client.PostAsync("auth/challenge", content, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var error = KSeFErrorParser.Parse(responseBody);
            throw new KSeFApiException($"Challenge request failed: {error}");
        }

        var jsonNode = JsonNode.Parse(responseBody);
        var challenge = jsonNode?["challenge"]?.GetValue<string>()
                        ?? throw new InvalidOperationException("Challenge not found in response");

        var timestampStr = jsonNode["timestamp"]?.GetValue<string>();
        long timestampMs = 0;
        
        if (!string.IsNullOrEmpty(timestampStr) && DateTime.TryParse(timestampStr, out var dt))
        {
            timestampMs = new DateTimeOffset(dt).ToUnixTimeMilliseconds();
        }

        return (challenge, timestampMs);
    }
}