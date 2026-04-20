// Services/Interfaces/IKSeFAuthRedeemService.cs
using KSeF.Backend.Models.Responses;

namespace KSeF.Backend.Services.Interfaces;

public interface IKSeFAuthRedeemService
{
    Task<TokenRedeemResponse?> RedeemTokenAsync(
        HttpClient client,
        string authenticationToken,
        CancellationToken ct = default);
}