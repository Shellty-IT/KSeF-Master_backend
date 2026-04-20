// Services/Interfaces/IUserAuthService.cs
using KSeF.Backend.Models.Requests;
using KSeF.Backend.Models.Responses;

namespace KSeF.Backend.Services.Interfaces;

public interface IUserAuthService
{
    Task<AppAuthResult> RegisterAsync(RegisterRequest request);
    Task<AppAuthResult> LoginAsync(LoginAppRequest request);
    Task<UserInfo?> GetUserByIdAsync(int userId);
}