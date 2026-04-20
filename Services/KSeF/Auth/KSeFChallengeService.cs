// Services/KSeF/Auth/KSeFChallengeService.cs
using System.Text;
using System.Text.Json.Nodes;
using KSeF.Backend.Infrastructure.KSeF;
using KSeF.Backend.Services.Interfaces;

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
        CancellationToken ct = default)
    {
        var response = await client.PostAsync(
            "auth/challenge",
            new StringContent("{}", Encoding.UTF8, "application/json"),
            ct);

        var content = await response.Content.ReadAsStringAsync(ct);

        _logger.LogInformation("Challenge response: {Status} ({Code})",
            response.StatusCode, (int)response.StatusCode);
        _logger.LogDebug("Body: {Body}", KSeFResponseLogger.Sanitize(content));

        if (!response.IsSuccessStatusCode)
        {
            throw new KSeFApiException(
                $"Błąd pobierania challenge: HTTP {(int)response.StatusCode} — {KSeFErrorParser.ExtractError(content, "")}",
                response.StatusCode, content);
        }

        var node = JsonNode.Parse(content);
        var challenge = node?["challenge"]?.GetValue<string>();

        if (string.IsNullOrWhiteSpace(challenge))
            throw new KSeFApiException("Brak pola 'challenge' w odpowiedzi", response.StatusCode, content);

        long timestampMs = 0;
        try
        {
            var tsMs = node?["timestampMs"];
            if (tsMs != null)
                timestampMs = tsMs.GetValue<long>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Nie można pobrać timestampMs z JSON");
        }

        if (timestampMs == 0)
        {
            var timestamp = node?["timestamp"];
            if (timestamp != null && DateTime.TryParse(timestamp.GetValue<string>(), out var parsedDate))
            {
                timestampMs = new DateTimeOffset(parsedDate.ToUniversalTime()).ToUnixTimeMilliseconds();
                _logger.LogWarning("timestampMs obliczono z timestamp: {Ms}", timestampMs);
            }
        }

        return (challenge, timestampMs);
    }
}