// Services/Interfaces/IJwtService.cs
using KSeF.Backend.Models.Data;

namespace KSeF.Backend.Services.Interfaces;

public interface IJwtService
{
    string GenerateToken(User user);
}