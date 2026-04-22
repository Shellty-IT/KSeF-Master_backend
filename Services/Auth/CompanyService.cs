// Services/Auth/CompanyService.cs
using KSeF.Backend.Models.Data;
using KSeF.Backend.Models.Requests;
using KSeF.Backend.Models.Responses;
using KSeF.Backend.Repositories;
using KSeF.Backend.Services.Interfaces;

namespace KSeF.Backend.Services.Auth;

public class CompanyService : ICompanyService
{
    private readonly IUserRepository _userRepository;
    private readonly ICompanyRepository _companyRepository;
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly ITokenEncryptionService _encryption;
    private readonly IJwtService _jwtService;
    private readonly ILogger<CompanyService> _logger;

    public CompanyService(
        IUserRepository userRepository,
        ICompanyRepository companyRepository,
        IInvoiceRepository invoiceRepository,
        ITokenEncryptionService encryption,
        IJwtService jwtService,
        ILogger<CompanyService> logger)
    {
        _userRepository = userRepository;
        _companyRepository = companyRepository;
        _invoiceRepository = invoiceRepository;
        _encryption = encryption;
        _jwtService = jwtService;
        _logger = logger;
    }

    public async Task<AppAuthResult> SetupCompanyAsync(int userId, CompanySetupRequest request)
    {
        var errors = ValidateCompanySetup(request);
        if (errors.Count > 0)
            return Fail(string.Join("; ", errors));

        var user = await _userRepository.GetByIdWithCompanyAsync(userId);
        if (user == null)
            return Fail("Użytkownik nie istnieje");

        if (user.CompanyProfile != null)
            return Fail("Firma jest już skonfigurowana. Użyj aktualizacji tokenu.");

        var company = new CompanyProfile
        {
            UserId = userId,
            CompanyName = request.CompanyName.Trim(),
            Nip = request.Nip.Trim(),
            KsefTokenEncrypted = _encryption.Encrypt(request.KsefToken.Trim()),
            AuthMethod = "token",
            KsefEnvironment = request.KsefEnvironment,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        await _companyRepository.CreateAsync(company);

        _logger.LogInformation("Company configured for user {UserId}: NIP {Nip}, Environment {Env}",
            userId, request.Nip, request.KsefEnvironment);

        user.CompanyProfile = company;

        return new AppAuthResult
        {
            Success = true,
            Token = _jwtService.GenerateToken(user),
            User = UserAuthService.MapUserInfo(user)
        };
    }

    public async Task<AppAuthResult> UpdateCompanyProfileAsync(int userId, UpdateCompanyProfileRequest request)
    {
        var errors = ValidateCompanyProfile(request);
        if (errors.Count > 0)
            return Fail(string.Join("; ", errors));

        var company = await _companyRepository.GetByUserIdAsync(userId);
        if (company == null)
            return Fail("Profil firmy nie istnieje. Najpierw skonfiguruj firmę.");

        var nipChanged = company.Nip != request.Nip.Trim();

        company.CompanyName = request.CompanyName.Trim();
        company.Nip = request.Nip.Trim();
        company.UpdatedAt = DateTime.UtcNow;

        await _companyRepository.UpdateAsync(company);

        if (nipChanged)
        {
            _logger.LogInformation(
                "NIP changed for companyProfileId={CompanyProfileId} — clearing invoices",
                company.Id);
            await _invoiceRepository.DeleteByCompanyProfileIdAsync(company.Id);
        }

        _logger.LogInformation("Company profile updated for user {UserId}: {CompanyName}, NIP {Nip}",
            userId, request.CompanyName, request.Nip);

        var user = await _userRepository.GetByIdWithCompanyAsync(userId);
        return new AppAuthResult { Success = true, User = UserAuthService.MapUserInfo(user!) };
    }

    public async Task<AppAuthResult> UpdateKsefEnvironmentAsync(int userId, UpdateKsefEnvironmentRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.KsefEnvironment))
            return Fail("Środowisko KSeF jest wymagane");

        if (request.KsefEnvironment != "Test" && request.KsefEnvironment != "Production")
            return Fail("Dozwolone środowiska: 'Test' lub 'Production'");

        var company = await _companyRepository.GetByUserIdAsync(userId);
        if (company == null)
            return Fail("Najpierw skonfiguruj firmę");

        company.KsefEnvironment = request.KsefEnvironment;
        company.UpdatedAt = DateTime.UtcNow;

        await _companyRepository.UpdateAsync(company);

        _logger.LogInformation("KSeF environment switched to {Environment} for user {UserId}",
            request.KsefEnvironment, userId);

        var user = await _userRepository.GetByIdWithCompanyAsync(userId);
        return new AppAuthResult { Success = true, User = UserAuthService.MapUserInfo(user!) };
    }

    public async Task<AppAuthResult> UpdateKsefTokenAsync(int userId, UpdateKsefTokenRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.KsefToken))
            return Fail("Token KSeF jest wymagany");

        if (!request.KsefToken.Contains('|'))
            return Fail("Nieprawidłowy format tokenu KSeF");

        var company = await _companyRepository.GetByUserIdAsync(userId);
        if (company == null)
            return Fail("Najpierw skonfiguruj firmę");

        company.KsefTokenEncrypted = _encryption.Encrypt(request.KsefToken.Trim());
        company.UpdatedAt = DateTime.UtcNow;

        await _companyRepository.UpdateAsync(company);

        _logger.LogInformation("KSeF token updated for user {UserId}", userId);

        var user = await _userRepository.GetByIdWithCompanyAsync(userId);
        return new AppAuthResult { Success = true, User = UserAuthService.MapUserInfo(user!) };
    }

    public async Task<string?> GetDecryptedKsefTokenAsync(int userId)
    {
        var company = await _companyRepository.GetByUserIdAsync(userId);
        if (company == null || string.IsNullOrEmpty(company.KsefTokenEncrypted))
            return null;

        try
        {
            return _encryption.Decrypt(company.KsefTokenEncrypted);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to decrypt KSeF token for user {UserId}", userId);
            return null;
        }
    }

    private static AppAuthResult Fail(string error) =>
        new() { Success = false, Error = error };

    private static List<string> ValidateCompanySetup(CompanySetupRequest request)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(request.CompanyName))
            errors.Add("Nazwa firmy jest wymagana");

        if (string.IsNullOrWhiteSpace(request.Nip))
            errors.Add("NIP jest wymagany");
        else if (request.Nip.Trim().Length != 10 || !request.Nip.Trim().All(char.IsDigit))
            errors.Add("NIP musi mieć dokładnie 10 cyfr");

        if (string.IsNullOrWhiteSpace(request.KsefToken))
            errors.Add("Token KSeF jest wymagany");
        else if (!request.KsefToken.Contains('|'))
            errors.Add("Nieprawidłowy format tokenu KSeF");

        if (request.KsefEnvironment != "Test" && request.KsefEnvironment != "Production")
            errors.Add("Dozwolone środowiska: 'Test' lub 'Production'");

        return errors;
    }

    private static List<string> ValidateCompanyProfile(UpdateCompanyProfileRequest request)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(request.CompanyName))
            errors.Add("Nazwa firmy jest wymagana");

        if (string.IsNullOrWhiteSpace(request.Nip))
            errors.Add("NIP jest wymagany");
        else if (request.Nip.Trim().Length != 10 || !request.Nip.Trim().All(char.IsDigit))
            errors.Add("NIP musi mieć dokładnie 10 cyfr");

        return errors;
    }
}