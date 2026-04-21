// Infrastructure/Extensions/ServicesExtensions.cs
using KSeF.Backend.Repositories;
using KSeF.Backend.Services;
using KSeF.Backend.Services.Auth;
using KSeF.Backend.Services.Interfaces;
using KSeF.Backend.Services.KSeF.Auth;
using KSeF.Backend.Services.KSeF.Certificate;
using KSeF.Backend.Services.KSeF.Invoice;
using KSeF.Backend.Services.Pdf;
using FluentValidation;
using FluentValidation.AspNetCore;

namespace KSeF.Backend.Infrastructure.Extensions;

public static class ServicesExtensions
{
    public static WebApplicationBuilder AddAppServices(this WebApplicationBuilder builder)
    {
        RegisterSingletons(builder.Services);
        RegisterRepositories(builder.Services);
        RegisterAuthServices(builder.Services);
        RegisterKSeFAuthServices(builder.Services);
        RegisterKSeFInvoiceServices(builder.Services);
        RegisterPdfServices(builder.Services);
        RegisterValidators(builder.Services);
        RegisterOtherServices(builder.Services);

        return builder;
    }

    private static void RegisterSingletons(IServiceCollection services)
    {
        services.AddSingleton<KSeFSessionManager>();
        services.AddSingleton<IExternalDraftService, ExternalDraftService>();
        services.AddSingleton<ITokenEncryptionService, TokenEncryptionService>();
    }

    private static void RegisterRepositories(IServiceCollection services)
    {
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<ICompanyRepository, CompanyRepository>();
    }

    private static void RegisterAuthServices(IServiceCollection services)
    {
        services.AddScoped<IJwtService, JwtService>();
        services.AddScoped<IUserAuthService, UserAuthService>();
        services.AddScoped<ICompanyService, CompanyService>();
        services.AddScoped<ICertificateService, CertificateService>();
    }

    private static void RegisterKSeFAuthServices(IServiceCollection services)
    {
        services.AddScoped<IKSeFEnvironmentService, KSeFEnvironmentService>();
        services.AddScoped<IKSeFCryptoService, KSeFCryptoService>();

        services.AddScoped<IKSeFChallengeService, KSeFChallengeService>();
        services.AddScoped<IKSeFAuthPollingService, KSeFAuthPollingService>();
        services.AddScoped<IKSeFAuthRedeemService, KSeFAuthRedeemService>();
        services.AddScoped<IKSeFTokenRefreshService, KSeFTokenRefreshService>();

        services.AddScoped<IKSeFAuthService, KSeFAuthService>();
        services.AddScoped<IKSeFCertAuthService, KSeFCertAuthService>();
    }

    private static void RegisterKSeFInvoiceServices(IServiceCollection services)
    {
        services.AddScoped<IKSeFInvoiceQueryService, KSeFInvoiceQueryService>();
        services.AddScoped<IKSeFInvoiceDetailsService, KSeFInvoiceDetailsService>();
        services.AddScoped<IKSeFInvoiceStatsService, KSeFInvoiceStatsService>();
        services.AddScoped<IKSeFOnlineSessionService, KSeFOnlineSessionService>();
        services.AddScoped<IKSeFInvoiceSendService, KSeFInvoiceSendService>();
        services.AddScoped<IKSeFInvoiceService, KSeFInvoiceFacade>();
    }

    private static void RegisterPdfServices(IServiceCollection services)
    {
        services.AddScoped<PdfUrlBuilder>();
        services.AddScoped<PdfQrCodeGenerator>();
        services.AddScoped<PdfSectionRenderer>();
        services.AddScoped<PdfDocumentComposer>();
        services.AddScoped<IPdfGeneratorService, PdfGeneratorService>();
    }

    private static void RegisterValidators(IServiceCollection services)
    {
        services.AddFluentValidationAutoValidation();
        services.AddValidatorsFromAssemblyContaining<Program>();
    }

    private static void RegisterOtherServices(IServiceCollection services)
    {
        services.AddScoped<InvoiceXmlGenerator>();
    }
}