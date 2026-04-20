// Infrastructure/KSeF/KSeFApiException.cs
namespace KSeF.Backend.Infrastructure.KSeF;

public class KSeFApiException : Exception
{
    public System.Net.HttpStatusCode? StatusCode { get; }
    public string? RawResponse { get; }

    public KSeFApiException(
        string message,
        System.Net.HttpStatusCode? statusCode = null,
        string? rawResponse = null)
        : base(message)
    {
        StatusCode = statusCode;
        RawResponse = rawResponse;
    }
}