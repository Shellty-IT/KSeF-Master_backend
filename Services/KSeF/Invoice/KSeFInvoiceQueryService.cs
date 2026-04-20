// Services/KSeF/Invoice/KSeFInvoiceQueryService.cs
using System.Text;
using System.Text.Json;
using KSeF.Backend.Models.Requests;
using KSeF.Backend.Models.Responses;
using KSeF.Backend.Services.Interfaces;

namespace KSeF.Backend.Services.KSeF.Invoice;

public class KSeFInvoiceQueryService : IKSeFInvoiceQueryService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IKSeFAuthService _authService;
    private readonly KSeFSessionManager _session;
    private readonly ILogger<KSeFInvoiceQueryService> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public KSeFInvoiceQueryService(
        IHttpClientFactory httpClientFactory,
        IKSeFAuthService authService,
        KSeFSessionManager session,
        ILogger<KSeFInvoiceQueryService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _authService = authService;
        _session = session;
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };
    }

    public async Task<InvoiceQueryResponse> GetInvoicesAsync(InvoiceQueryRequest request, CancellationToken ct = default)
    {
        EnsureAuthenticated();
        await _authService.RefreshTokenIfNeededAsync(ct);

        var client = _httpClientFactory.CreateClient("KSeF");
        var allInvoices = new List<InvoiceMetadata>();
        var seenIds = new HashSet<string>();
        var maxResults = request.MaxResults ?? int.MaxValue;
        var sortOrder = request.SortDescending ? "Desc" : "Asc";
        var pageOffset = 0;
        const int pageSize = 100;
        var hasMore = true;
        var iteration = 0;
        const int maxIterations = 200;
        var currentFrom = request.DateRange.From.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        var currentTo = request.DateRange.To.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

        _logger.LogInformation(
            "Pobieranie faktur (SubjectType: {Type}, dateType: {DateType}, od: {From}, do: {To})",
            request.SubjectType, request.DateRange.DateType, currentFrom, currentTo);

        while (hasMore && iteration < maxIterations && allInvoices.Count < maxResults)
        {
            iteration++;

            var requestBodyJson = JsonSerializer.Serialize(new
            {
                subjectType = request.SubjectType,
                dateRange = new
                {
                    dateType = request.DateRange.DateType,
                    from = currentFrom,
                    to = currentTo
                }
            });

            var url = $"invoices/query/metadata?sortOrder={sortOrder}&pageOffset={pageOffset}&pageSize={pageSize}";

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url);
            httpRequest.Headers.Add("Authorization", $"Bearer {_session.AccessToken}");
            httpRequest.Content = new StringContent(requestBodyJson, Encoding.UTF8, "application/json");

            var response = await client.SendAsync(httpRequest, ct);
            var content = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"Błąd pobierania faktur: {response.StatusCode} - {content}");

            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            var pageHasMore = root.TryGetProperty("hasMore", out var hasMoreEl) && hasMoreEl.GetBoolean();
            var pageIsTruncated = root.TryGetProperty("isTruncated", out var truncEl) && truncEl.GetBoolean();
            var newCount = 0;
            var lastInvoiceDate = string.Empty;

            if (root.TryGetProperty("invoices", out var invoicesEl) && invoicesEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var invEl in invoicesEl.EnumerateArray())
                {
                    var invoice = JsonSerializer.Deserialize<InvoiceMetadata>(invEl.GetRawText(), _jsonOptions);
                    if (invoice == null) continue;

                    if (seenIds.Add(invoice.KsefNumber))
                    {
                        allInvoices.Add(invoice);
                        newCount++;
                        lastInvoiceDate = GetInvoiceDateForCursor(invoice, request.DateRange.DateType);

                        if (allInvoices.Count >= maxResults)
                            break;
                    }
                }
            }

            _logger.LogInformation(
                "Iter {Iter}: +{New} faktur (łącznie: {Total}, hasMore: {HasMore}, isTruncated: {Truncated})",
                iteration, newCount, allInvoices.Count, pageHasMore, pageIsTruncated);

            if (!pageHasMore)
            {
                hasMore = false;
                break;
            }

            if (allInvoices.Count >= maxResults)
                break;

            if (pageIsTruncated)
            {
                if (string.IsNullOrEmpty(lastInvoiceDate))
                    break;

                var newFrom = NormalizeToUtcString(lastInvoiceDate);
                if (newFrom == currentFrom)
                {
                    if (DateTimeOffset.TryParse(lastInvoiceDate, out var parsedDate))
                        newFrom = parsedDate.AddMilliseconds(1).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ");
                    else
                        break;
                }

                currentFrom = newFrom;
                pageOffset = 0;
            }
            else
            {
                pageOffset += pageSize;
            }
        }

        if (request.SortDescending)
            allInvoices = allInvoices.OrderByDescending(i => i.InvoicingDate ?? i.PermanentStorageDate ?? DateTime.MinValue).ToList();
        else
            allInvoices = allInvoices.OrderBy(i => i.InvoicingDate ?? i.PermanentStorageDate ?? DateTime.MinValue).ToList();

        _logger.LogInformation("Pobieranie zakończone: {Count} faktur w {Iterations} iteracjach", allInvoices.Count, iteration);

        return new InvoiceQueryResponse
        {
            HasMore = false,
            IsTruncated = allInvoices.Count >= maxResults,
            Invoices = allInvoices,
            TotalCount = allInvoices.Count,
            FetchedAt = DateTime.UtcNow,
            PagesProcessed = iteration
        };
    }

    private string GetInvoiceDateForCursor(InvoiceMetadata invoice, string dateType)
    {
        return dateType switch
        {
            "Issue" => invoice.IssueDate ?? string.Empty,
            "Invoicing" => invoice.InvoicingDate?.ToString("O") ?? string.Empty,
            _ => invoice.PermanentStorageDate?.ToString("O") ?? string.Empty
        };
    }

    private static string NormalizeToUtcString(string dateString)
    {
        if (DateTimeOffset.TryParse(dateString, out var dto))
            return dto.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ");
        return dateString;
    }

    private void EnsureAuthenticated()
    {
        if (!_session.IsAuthenticated)
            throw new UnauthorizedAccessException("Nie jesteś zalogowany do KSeF");
    }
}