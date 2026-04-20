// Services/KSeF/Auth/KSeFAuthRedeemService.cs
using System.Text;
using System.Text.Json;
using KSeF.Backend.Infrastructure.KSeF;
using KSeF.Backend.Models.Responses;
using KSeF.Backend.Services.Interfaces;

namespace KSeF.Backend.Services.KSeF.Auth;

public class KSeFAuthRedeemService : IKSeFAuthRedeemService
{
    private readonly ILogger<KSeFAuthRedeemService> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public KSeFAuthRedeemService(ILogger<KSeFAuthRedeemService> logger)
    {
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
    }

    public async Task<TokenRedeemResponse?> RedeemTokenAsync(
        HttpClient client,
        string authenticationToken,
        CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "auth/token/redeem");
        request.Headers.Add("Authorization", $"Bearer {authenticationToken}");
        request.Content = new StringContent("{}", Encoding.UTF8, "application/json");

        var response = await client.SendAsync(request, ct);
        var content = await response.Content.ReadAsStringAsync(ct);

        _logger.LogInformation("Redeem response: {Status}", response.StatusCode);
        _logger.LogDebug("Body: {Body}", KSeFResponseLogger.Sanitize(content));

        if (!response.IsSuccessStatusCode)
        {
            var errMsg = KSeFErrorParser.ExtractError(content, "");
            throw new KSeFApiException($"Błąd token/redeem: {errMsg}", response.StatusCode, content);
        }

        return JsonSerializer.Deserialize<TokenRedeemResponse>(content, _jsonOptions);
    }
}