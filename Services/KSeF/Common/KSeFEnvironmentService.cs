using KSeF.Backend.Models.Configuration;
using KSeF.Backend.Services.Interfaces.KSeF;

namespace KSeF.Backend.Services.KSeF.Common;

public class KSeFEnvironmentService : IKSeFEnvironmentService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<KSeFEnvironmentService> _logger;

    public KSeFEnvironmentService(
        IConfiguration configuration,
        ILogger<KSeFEnvironmentService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public KSeFEnvironmentConfig GetEnvironmentConfig(string environment)
    {
        var config = new KSeFEnvironmentConfig();
        
        var section = _configuration.GetSection($"KSeF:Environments:{environment}");
        
        if (!section.Exists())
        {
            _logger.LogWarning(
                "⚠️ KSeF environment '{Environment}' not found in configuration, using Test fallback",
                environment);
            section = _configuration.GetSection("KSeF:Environments:Test");
        }

        config.ApiBaseUrl = section.GetValue<string>("ApiBaseUrl") 
                            ?? "https://api-test.ksef.mf.gov.pl/v2/";
        config.AppUrl = section.GetValue<string>("AppUrl") 
                        ?? "https://ap-test.ksef.mf.gov.pl";
        config.QrBaseUrl = section.GetValue<string>("QrBaseUrl") 
                           ?? "https://qr-test.ksef.mf.gov.pl";

        _logger.LogDebug(
            "KSeF environment '{Environment}' config loaded: API={ApiUrl}",
            environment, config.ApiBaseUrl);

        return config;
    }

    public string GetApiBaseUrl(string environment)
    {
        return GetEnvironmentConfig(environment).ApiBaseUrl;
    }

    public string GetAppUrl(string environment)
    {
        return GetEnvironmentConfig(environment).AppUrl;
    }

    public string GetQrBaseUrl(string environment)
    {
        return GetEnvironmentConfig(environment).QrBaseUrl;
    }
}