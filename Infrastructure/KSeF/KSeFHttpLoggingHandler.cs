using System.Diagnostics;

namespace KSeF.Backend.Infrastructure.KSeF;

/// <summary>
/// Handler logujący wszystkie requesty/odpowiedzi do KSeF API.
/// Pomaga w diagnostyce problemów z API.
/// </summary>
public class KSeFHttpLoggingHandler : DelegatingHandler
{
    private readonly ILogger<KSeFHttpLoggingHandler> _logger;

    public KSeFHttpLoggingHandler(ILogger<KSeFHttpLoggingHandler> logger)
    {
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, 
        CancellationToken cancellationToken)
    {
        var requestId = Guid.NewGuid().ToString("N")[..8];
        var stopwatch = Stopwatch.StartNew();

        // Log request
        _logger.LogDebug("[{RequestId}] → {Method} {Uri}", 
            requestId, request.Method, request.RequestUri);
        
        foreach (var header in request.Headers)
        {
            _logger.LogDebug("[{RequestId}]   Header: {Name}: {Value}", 
                requestId, header.Key, string.Join(", ", header.Value));
        }

        if (request.Content != null)
        {
            var requestBody = await request.Content.ReadAsStringAsync(cancellationToken);
            if (!string.IsNullOrEmpty(requestBody) && requestBody.Length <= 2000)
            {
                _logger.LogDebug("[{RequestId}]   Body: {Body}", requestId, requestBody);
            }
        }

        // Execute request
        HttpResponseMessage response;
        try
        {
            response = await base.SendAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "[{RequestId}] ✗ Request failed after {Elapsed}ms", 
                requestId, stopwatch.ElapsedMilliseconds);
            throw;
        }

        stopwatch.Stop();

        // Log response
        var contentType = response.Content.Headers.ContentType?.MediaType ?? "unknown";
        _logger.LogDebug("[{RequestId}] ← {StatusCode} ({Status}) in {Elapsed}ms | Content-Type: {ContentType}",
            requestId,
            (int)response.StatusCode,
            response.StatusCode,
            stopwatch.ElapsedMilliseconds,
            contentType);

        // Log response body for errors or unexpected content types
        if (!response.IsSuccessStatusCode || !contentType.Contains("json"))
        {
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            var truncated = responseBody.Length > 1000 
                ? responseBody[..1000] + "... [truncated]" 
                : responseBody;
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("[{RequestId}] Response body: {Body}", requestId, truncated);
            }
            else
            {
                _logger.LogDebug("[{RequestId}] Response body (non-JSON): {Body}", requestId, truncated);
            }
        }

        return response;
    }
}