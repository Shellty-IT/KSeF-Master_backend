using KSeF.Backend.Services.Interfaces;

namespace KSeF.Backend.Services.Pdf;

public class PdfUrlBuilder
{
    private readonly IKSeFEnvironmentService _environmentService;

    public PdfUrlBuilder(IKSeFEnvironmentService environmentService)
    {
        _environmentService = environmentService;
    }

    public string BuildVerificationUrl(string sellerNip, string issueDate, string invoiceHash, string environment)
    {
        if (string.IsNullOrEmpty(sellerNip) || string.IsNullOrEmpty(issueDate) || string.IsNullOrEmpty(invoiceHash))
            return string.Empty;

        var baseUrl = _environmentService.GetQrBaseUrl(environment);
        var formattedDate = FormatDateForUrl(issueDate);
        var hashBase64Url = ConvertToBase64Url(invoiceHash);

        return $"{baseUrl}/client-app/invoice/{sellerNip}/{formattedDate}/{hashBase64Url}";
    }

    public string BuildQrUrl(string sellerNip, string issueDate, string invoiceHash, string environment)
    {
        if (string.IsNullOrEmpty(sellerNip) || string.IsNullOrEmpty(issueDate) || string.IsNullOrEmpty(invoiceHash))
            return string.Empty;

        var baseUrl = _environmentService.GetQrBaseUrl(environment);
        var formattedDate = FormatDateForUrl(issueDate);
        var hashBase64Url = ConvertToBase64Url(invoiceHash);

        return $"{baseUrl}/{sellerNip}/{formattedDate}/{hashBase64Url}";
    }

    private static string FormatDateForUrl(string issueDate)
    {
        var parts = issueDate.Split('-');
        return parts.Length == 3
            ? $"{parts[2]}-{parts[1]}-{parts[0]}"
            : issueDate;
    }

    private static string ConvertToBase64Url(string base64)
    {
        if (string.IsNullOrEmpty(base64))
            return string.Empty;

        return base64.Replace("+", "-").Replace("/", "_").TrimEnd('=');
    }
}