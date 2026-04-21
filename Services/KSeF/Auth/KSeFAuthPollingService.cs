// Services/KSeF/Auth/KSeFAuthPollingService.cs
using System.Text.Json;
using KSeF.Backend.Infrastructure.KSeF;
using KSeF.Backend.Models.Responses.Auth;
using KSeF.Backend.Services.Interfaces;

namespace KSeF.Backend.Services.KSeF.Auth;

public class KSeFAuthPollingService : IKSeFAuthPollingService
{
    private const int MaxPollingAttempts = 60;
    private const int PollingDelayMs = 2000;

    private readonly ILogger<KSeFAuthPollingService> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public KSeFAuthPollingService(ILogger<KSeFAuthPollingService> logger)
    {
        _logger = logger;
    }

    public async Task<string?> PollAuthStatusAsync(
        HttpClient client,
        string referenceNumber,
        string authenticationToken,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Rozpoczynam polling statusu autoryzacji: {Ref}", referenceNumber);

        for (var attempt = 1; attempt <= MaxPollingAttempts; attempt++)
        {
            await Task.Delay(PollingDelayMs, cancellationToken);

            var request = new HttpRequestMessage(HttpMethod.Get, $"auth/{referenceNumber}");
            request.Headers.Add("Authorization", $"Bearer {authenticationToken}");

            var response = await client.SendAsync(request, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var error = KSeFErrorParser.Parse(responseBody);
                _logger.LogDebug("Polling attempt {Attempt}: {Status} - {Error}",
                    attempt, response.StatusCode, error);
                continue;
            }

            var status = JsonSerializer.Deserialize<AuthStatusResponse>(responseBody, JsonOptions);

            if (status?.AccessToken != null && !string.IsNullOrEmpty(status.AccessToken.Token))
            {
                _logger.LogInformation("✓ AccessToken w odpowiedzi statusu po {Attempt} próbach", attempt);
                return authenticationToken;
            }

            if (status?.Status?.Code == 200)
            {
                _logger.LogInformation("✓ Status 200 — gotowy do redeem po {Attempt} próbach", attempt);
                return authenticationToken;
            }

            _logger.LogDebug("Polling attempt {Attempt}/{Max}: Status={Code}",
                attempt, MaxPollingAttempts, status?.Status?.Code);
        }

        _logger.LogWarning("Timeout autoryzacji po {Max} próbach", MaxPollingAttempts);
        return null;
    }
}