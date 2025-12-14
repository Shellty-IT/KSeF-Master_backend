namespace KSeF.Backend.Models.Requests;

public class LoginRequest
{
    /// <summary>
    /// NIP podatnika (10 cyfr)
    /// </summary>
    public string Nip { get; set; } = string.Empty;
    
    /// <summary>
    /// Token KSeF wygenerowany w oficjalnej aplikacji
    /// Format: XXXXXXXX-XX-XXXXXXXXXX-XXXXXXXXXX-XX|nip-XXXXXXXXXX|hash
    /// </summary>
    public string KsefToken { get; set; } = string.Empty;
}