using KSeF.Backend.Models.Data;

namespace KSeF.Backend.Repositories;

public interface ICompanyRepository
{
    Task<CompanyProfile?> GetByIdAsync(int companyId);
    Task<CompanyProfile?> GetByUserIdAsync(int userId);
    Task<CompanyProfile?> GetByNipAsync(string nip);
    Task<CompanyProfile> CreateAsync(CompanyProfile company);
    Task UpdateAsync(CompanyProfile company);
    Task DeleteAsync(CompanyProfile company);
    Task SaveChangesAsync();
}