// Services/KSeF/Auth/KSeFTokenRefreshService.cs
using System.Text;
using System.Text.Json;
using KSeF.Backend.Models.Responses;
using KSeF.Backend.Services.Interfaces;

namespace KSeF.Backend.Services.KSeF.Auth;

public class KSeFTokenRefreshService : IKSeFTokenRefreshService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly KSeFSessionManager _session;
    private readonly ILogger<KSeFTokenRefreshService> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public KSeFTokenRefreshService(
        IHttpClientFactory httpClientFactory,
        KSeFSessionManager session,
        ILogger<KSeFTokenRefreshService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _session = session;
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
    }

    public async Task<bool> RefreshTokenIfNeededAsync(CancellationToken ct = default)
    {
        if (!_session.NeedsTokenRefresh)
            return true;

        _logger.LogInformation("Odświeżanie accessToken...");

        try
        {
            var client = _httpClientFactory.CreateClient("KSeF");
            using var request = new HttpRequestMessage(HttpMethod.Post, "auth/token/refresh");
            request.Headers.Add("Authorization", $"Bearer {_session.RefreshToken}");
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");

            var response = await client.SendAsync(request, ct);
            var content = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Błąd refresh: {Content}", content);
                return false;
            }

            var tokens = JsonSerializer.Deserialize<TokenRefreshResponse>(content, _jsonOptions);
            if (tokens == null) return false;

            _session.UpdateAccessToken(tokens);
            _logger.LogInformation("AccessToken odświeżony do: {Until}", tokens.AccessToken?.ValidUntil);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Błąd odświeżania tokena");
            return false;
        }
    }
}