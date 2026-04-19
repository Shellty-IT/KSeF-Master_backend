// Models/Data/AppDbContext.cs
using Microsoft.EntityFrameworkCore;

namespace KSeF.Backend.Models.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<CompanyProfile> CompanyProfiles => Set<CompanyProfile>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Email).IsUnique();
            entity.Property(e => e.Email).IsRequired().HasMaxLength(256);
            entity.Property(e => e.PasswordHash).IsRequired();
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.Property(e => e.CreatedAt).IsRequired();

            entity.HasOne(e => e.CompanyProfile)
                .WithOne(e => e.User)
                .HasForeignKey<CompanyProfile>(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CompanyProfile>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.UserId).IsUnique();
            entity.HasIndex(e => e.Nip);
            
            entity.Property(e => e.CompanyName).IsRequired().HasMaxLength(300);
            entity.Property(e => e.Nip).IsRequired().HasMaxLength(10);
            entity.Property(e => e.AuthMethod).IsRequired().HasMaxLength(20).HasDefaultValue("token");
            entity.Property(e => e.IsActive).IsRequired();
            entity.Property(e => e.CreatedAt).IsRequired();
            
            entity.Property(e => e.KsefTokenEncrypted).HasMaxLength(2000);
            entity.Property(e => e.CertificateEncrypted).HasMaxLength(10000);
            entity.Property(e => e.PrivateKeyEncrypted).HasMaxLength(10000);
            entity.Property(e => e.CertificatePasswordEncrypted).HasMaxLength(500);
            entity.Property(e => e.LastSuccessfulAuthMethod).HasMaxLength(20);
        });
    }
}