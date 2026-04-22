// Models/Data/AppDbContext.cs
using Microsoft.EntityFrameworkCore;

namespace KSeF.Backend.Models.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<CompanyProfile> CompanyProfiles => Set<CompanyProfile>();
    public DbSet<Invoice> Invoices => Set<Invoice>();

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

            entity.HasMany(e => e.Invoices)
                .WithOne(e => e.CompanyProfile)
                .HasForeignKey(e => e.CompanyProfileId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Invoice>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasIndex(e => e.KsefReferenceNumber).IsUnique();
            entity.HasIndex(e => e.CompanyProfileId);
            entity.HasIndex(e => new { e.CompanyProfileId, e.Direction });
            entity.HasIndex(e => new { e.CompanyProfileId, e.InvoiceDate });

            entity.Property(e => e.KsefReferenceNumber).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Nip).IsRequired().HasMaxLength(10);
            entity.Property(e => e.InvoiceType).IsRequired().HasMaxLength(20);
            entity.Property(e => e.Direction).IsRequired().HasMaxLength(10);
            entity.Property(e => e.InvoiceNumber).HasMaxLength(100);
            entity.Property(e => e.SellerNip).HasMaxLength(20);
            entity.Property(e => e.SellerName).HasMaxLength(300);
            entity.Property(e => e.BuyerNip).HasMaxLength(20);
            entity.Property(e => e.BuyerName).HasMaxLength(300);
            entity.Property(e => e.Currency).HasMaxLength(3);
            entity.Property(e => e.NetAmount).HasPrecision(18, 2);
            entity.Property(e => e.VatAmount).HasPrecision(18, 2);
            entity.Property(e => e.GrossAmount).HasPrecision(18, 2);
            entity.Property(e => e.KsefEnvironment).IsRequired().HasMaxLength(20);
        });
    }
}