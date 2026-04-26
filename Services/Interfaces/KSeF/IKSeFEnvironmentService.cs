using KSeF.Backend.Models.Configuration;

namespace KSeF.Backend.Services.Interfaces.KSeF;

public interface IKSeFEnvironmentService
{
    KSeFEnvironmentConfig GetEnvironmentConfig(string environment);
    string GetApiBaseUrl(string environment);
    string GetAppUrl(string environment);
    string GetQrBaseUrl(string environment);
}