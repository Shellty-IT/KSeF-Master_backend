// Models/Data/CompanyProfile.cs
namespace KSeF.Backend.Models.Data;

public class CompanyProfile
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string Nip { get; set; } = string.Empty;
    public string KsefTokenEncrypted { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public User User { get; set; } = null!;
}