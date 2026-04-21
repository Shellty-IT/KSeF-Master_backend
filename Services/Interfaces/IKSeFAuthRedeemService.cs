using KSeF.Backend.Models.Responses.Auth;

namespace KSeF.Backend.Services.Interfaces;

public interface IKSeFAuthRedeemService
{
    Task<TokenRedeemResponse?> RedeemTokenAsync(
        HttpClient client,
        string authenticationToken,
        CancellationToken ct = default);
}