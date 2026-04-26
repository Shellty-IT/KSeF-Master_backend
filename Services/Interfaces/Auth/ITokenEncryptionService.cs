// Services/Interfaces/Auth/ITokenEncryptionService.cs
namespace KSeF.Backend.Services.Interfaces.Auth;

public interface ITokenEncryptionService
{
    string Encrypt(string plainText);
    string Decrypt(string cipherText);
}