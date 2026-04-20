// Services/KSeF/Auth/KSeFAuthPollingService.cs
using System.Text.Json;
using KSeF.Backend.Infrastructure.KSeF;
using KSeF.Backend.Models.Responses;
using KSeF.Backend.Services.Interfaces;

namespace KSeF.Backend.Services.KSeF.Auth;

public class KSeFAuthPollingService : IKSeFAuthPollingService
{
    private readonly ILogger<KSeFAuthPollingService> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public KSeFAuthPollingService(ILogger<KSeFAuthPollingService> logger)
    {
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
    }

    public async Task<string?> PollAuthStatusAsync(
        HttpClient client,
        string referenceNumber,
        string authenticationToken,
        CancellationToken ct = default)
    {
        const int maxAttempts = 20;
        const int delayMs = 1500;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"auth/{referenceNumber}");
            request.Headers.Add("Authorization", $"Bearer {authenticationToken}");

            var response = await client.SendAsync(request, ct);
            var content = await response.Content.ReadAsStringAsync(ct);

            _logger.LogInformation("[Polling {A}/{Max}] HTTP {Code}",
                attempt, maxAttempts, (int)response.StatusCode);
            _logger.LogDebug("Body: {Body}", KSeFResponseLogger.Sanitize(content));

            if (!response.IsSuccessStatusCode)
            {
                var errMsg = KSeFErrorParser.ExtractError(content, $"HTTP {(int)response.StatusCode}");
                throw new KSeFApiException($"Błąd sprawdzania statusu: {errMsg}", response.StatusCode, content);
            }

            var status = JsonSerializer.Deserialize<AuthStatusResponse>(content, _jsonOptions);
            var statusCode = status?.Status?.Code ?? -1;

            _logger.LogInformation("Status.Code={Code}, Description={Desc}",
                statusCode, status?.Status?.Description);

            if (status?.AccessToken != null && !string.IsNullOrEmpty(status.AccessToken.Token))
            {
                _logger.LogInformation("✓ AccessToken w odpowiedzi statusu — autoryzacja zakończona");
                return authenticationToken;
            }

            if (statusCode == 200)
            {
                _logger.LogInformation("✓ Status code=200 — gotowy do redeem");
                return authenticationToken;
            }

            if (statusCode == 100 || statusCode == 450)
            {
                _logger.LogInformation("⏳ Status {Code} — czekam {Delay}ms...", statusCode, delayMs);
                if (attempt < maxAttempts)
                    await Task.Delay(delayMs, ct);
                continue;
            }

            throw new InvalidOperationException(
                $"Nieoczekiwany status code: {statusCode} — {status?.Status?.Description ?? "brak opisu"}");
        }

        return null;
    }
}