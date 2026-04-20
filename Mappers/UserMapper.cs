// Mappers/UserMapper.cs
using KSeF.Backend.Models.Data;
using KSeF.Backend.Services.Interfaces;

namespace KSeF.Backend.Mappers;

public static class UserMapper
{
    public static UserInfo ToUserInfo(User user)
    {
        return new UserInfo
        {
            Id = user.Id,
            Email = user.Email,
            Name = user.Name,
            Company = user.CompanyProfile != null ? ToCompanyInfo(user.CompanyProfile) : null
        };
    }

    public static CompanyInfo ToCompanyInfo(CompanyProfile company)
    {
        return new CompanyInfo
        {
            Id = company.Id,
            CompanyName = company.CompanyName,
            Nip = company.Nip,
            IsActive = company.IsActive,
            HasKsefToken = !string.IsNullOrEmpty(company.KsefTokenEncrypted),
            AuthMethod = company.AuthMethod,
            KsefEnvironment = company.KsefEnvironment,
            HasCertificate = !string.IsNullOrEmpty(company.CertificateEncrypted)
        };
    }
}