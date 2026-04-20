// Infrastructure/KSeF/KSeFResponseLogger.cs
namespace KSeF.Backend.Infrastructure.KSeF;

public static class KSeFResponseLogger
{
    public static string Sanitize(string content, int maxLength = 600)
    {
        if (string.IsNullOrEmpty(content)) 
            return "(empty)";
        
        return content.Length > maxLength 
            ? content[..maxLength] + "... [truncated]" 
            : content;
    }
}