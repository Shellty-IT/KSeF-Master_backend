using Microsoft.EntityFrameworkCore;
using KSeF.Backend.Models.Data;

namespace KSeF.Backend.Repositories;

public class CompanyRepository : ICompanyRepository
{
    private readonly AppDbContext _db;

    public CompanyRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<CompanyProfile?> GetByIdAsync(int companyId)
    {
        return await _db.CompanyProfiles.FindAsync(companyId);
    }

    public async Task<CompanyProfile?> GetByUserIdAsync(int userId)
    {
        return await _db.CompanyProfiles
            .FirstOrDefaultAsync(c => c.UserId == userId && c.IsActive);
    }

    public async Task<CompanyProfile?> GetByNipAsync(string nip)
    {
        return await _db.CompanyProfiles
            .FirstOrDefaultAsync(c => c.Nip == nip && c.IsActive);
    }

    public async Task<CompanyProfile> CreateAsync(CompanyProfile company)
    {
        _db.CompanyProfiles.Add(company);
        await _db.SaveChangesAsync();
        return company;
    }

    public async Task UpdateAsync(CompanyProfile company)
    {
        _db.CompanyProfiles.Update(company);
        await _db.SaveChangesAsync();
    }

    public async Task DeleteAsync(CompanyProfile company)
    {
        _db.CompanyProfiles.Remove(company);
        await _db.SaveChangesAsync();
    }

    public async Task SaveChangesAsync()
    {
        await _db.SaveChangesAsync();
    }
}