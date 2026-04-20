// Services/Interfaces/ICompanyService.cs
using KSeF.Backend.Models.Requests;
using KSeF.Backend.Models.Responses;

namespace KSeF.Backend.Services.Interfaces;

public interface ICompanyService
{
    Task<AppAuthResult> SetupCompanyAsync(int userId, CompanySetupRequest request);
    Task<AppAuthResult> UpdateCompanyProfileAsync(int userId, UpdateCompanyProfileRequest request);
    Task<AppAuthResult> UpdateKsefEnvironmentAsync(int userId, UpdateKsefEnvironmentRequest request);
    Task<AppAuthResult> UpdateKsefTokenAsync(int userId, UpdateKsefTokenRequest request);
    Task<string?> GetDecryptedKsefTokenAsync(int userId);
}