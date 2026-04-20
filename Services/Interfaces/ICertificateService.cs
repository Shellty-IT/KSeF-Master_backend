// Services/Interfaces/ICertificateService.cs
using KSeF.Backend.Models.Requests;
using KSeF.Backend.Models.Responses;

namespace KSeF.Backend.Services.Interfaces;

public interface ICertificateService
{
    Task<AppAuthResult> UploadCertificateAsync(int userId, UploadCertificateRequest request);
    Task<AppAuthResult> SwitchAuthMethodAsync(int userId, SwitchAuthMethodRequest request);
    Task<AppAuthResult> DeleteCertificateAsync(int userId);
    Task<(byte[]? cert, byte[]? key, string? password)?> GetDecryptedCertificateAsync(int userId);
    Task<UserCertificateInfo?> GetCertificateInfoAsync(int userId);
}