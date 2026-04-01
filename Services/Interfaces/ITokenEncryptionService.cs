// Services/Interfaces/ITokenEncryptionService.cs
namespace KSeF.Backend.Services.Interfaces;

public interface ITokenEncryptionService
{
    string Encrypt(string plainText);
    string Decrypt(string cipherText);
}