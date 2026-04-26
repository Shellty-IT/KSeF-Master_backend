// Services/Interfaces/Auth/IJwtService.cs
using KSeF.Backend.Models.Data;

namespace KSeF.Backend.Services.Interfaces.Auth;

public interface IJwtService
{
    string GenerateToken(User user);
}