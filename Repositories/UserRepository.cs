using Microsoft.EntityFrameworkCore;
using KSeF.Backend.Models.Data;

namespace KSeF.Backend.Repositories;

public class UserRepository : IUserRepository
{
    private readonly AppDbContext _db;

    public UserRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<User?> GetByIdAsync(int userId)
    {
        return await _db.Users.FindAsync(userId);
    }

    public async Task<User?> GetByIdWithCompanyAsync(int userId)
    {
        return await _db.Users
            .Include(u => u.CompanyProfile)
            .FirstOrDefaultAsync(u => u.Id == userId);
    }

    public async Task<User?> GetByEmailAsync(string email)
    {
        var emailLower = email.Trim().ToLowerInvariant();
        return await _db.Users
            .Include(u => u.CompanyProfile)
            .FirstOrDefaultAsync(u => u.Email == emailLower);
    }

    public async Task<bool> EmailExistsAsync(string email)
    {
        var emailLower = email.Trim().ToLowerInvariant();
        return await _db.Users.AnyAsync(u => u.Email == emailLower);
    }

    public async Task<User> CreateAsync(User user)
    {
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        return user;
    }

    public async Task UpdateAsync(User user)
    {
        _db.Users.Update(user);
        await _db.SaveChangesAsync();
    }

    public async Task DeleteAsync(User user)
    {
        _db.Users.Remove(user);
        await _db.SaveChangesAsync();
    }

    public async Task SaveChangesAsync()
    {
        await _db.SaveChangesAsync();
    }
}