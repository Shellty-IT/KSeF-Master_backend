// Services/KSeF/Invoice/KSeFInvoiceQueryService.cs
using System.Text;
using System.Text.Json;
using KSeF.Backend.Models.Requests;
using KSeF.Backend.Models.Responses.Invoice;
using KSeF.Backend.Repositories;
using KSeF.Backend.Services.Interfaces.KSeF;
using InvoiceModel = KSeF.Backend.Models.Data.Invoice;
using KSeF.Backend.Services.KSeF.Session;

namespace KSeF.Backend.Services.KSeF.Invoice;

public class KSeFInvoiceQueryService : IKSeFInvoiceQueryService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IKSeFAuthService _authService;
    private readonly KSeFSessionManager _session;
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly ILogger<KSeFInvoiceQueryService> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public KSeFInvoiceQueryService(
        IHttpClientFactory httpClientFactory,
        IKSeFAuthService authService,
        KSeFSessionManager session,
        IInvoiceRepository invoiceRepository,
        ILogger<KSeFInvoiceQueryService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _authService = authService;
        _session = session;
        _invoiceRepository = invoiceRepository;
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };
    }

    public async Task<List<InvoiceModel>> GetCachedInvoicesAsync(int companyProfileId)
    {
        return await _invoiceRepository.GetByCompanyProfileIdAsync(companyProfileId);
    }

    public async Task<InvoiceSyncResult> SyncInvoicesAsync(
        int companyProfileId,
        string nip,
        string environment,
        string direction,
        CancellationToken cancellationToken = default)
    {
        if (!_session.IsAuthenticated)
            throw new UnauthorizedAccessException("Nie jesteś zalogowany do KSeF");

        await _authService.RefreshTokenIfNeededAsync(cancellationToken);

        var latestTimestamp = await _invoiceRepository.GetLatestAcquisitionTimestampAsync(companyProfileId, direction);
        var existingNumbers = await _invoiceRepository.GetExistingKsefNumbersAsync(companyProfileId);
        var existingSet = new HashSet<string>(existingNumbers);

        var subjectType = direction == "issued" ? "subject1" : "subject2";
        var syncFrom = latestTimestamp.HasValue
            ? latestTimestamp.Value.AddSeconds(-30)
            : DateTime.UtcNow.AddMonths(-3);
        var syncTo = DateTime.UtcNow;

        _logger.LogInformation(
            "Delta sync [{Direction}] companyProfileId={CompanyProfileId} od {From} do {To}",
            direction, companyProfileId, syncFrom, syncTo);

        var windows = BuildDateWindows(syncFrom, syncTo, months: 1);
        var allNewInvoices = new List<InvoiceModel>();
        var totalFetched = 0;
        var httpClient = _httpClientFactory.CreateClient("KSeF");

        foreach (var (windowFrom, windowTo) in windows)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var request = new InvoiceQueryRequest
            {
                SubjectType = subjectType,
                DateRange = new DateRangeFilter
                {
                    DateType = "PermanentStorage",
                    From = windowFrom,
                    To = windowTo
                },
                SortDescending = false,
                MaxResults = 9900
            };

            var queryResult = await QueryInvoicesAsync(httpClient, request, cancellationToken);
            totalFetched += queryResult.TotalCount;

            var newInvoices = queryResult.Invoices
                .Where(i => !existingSet.Contains(i.KsefNumber))
                .Select(i => MapToInvoice(i, companyProfileId, nip, direction, environment))
                .ToList();

            foreach (var inv in newInvoices)
                existingSet.Add(inv.KsefReferenceNumber);

            allNewInvoices.AddRange(newInvoices);

            _logger.LogInformation(
                "Okno [{From} → {To}]: +{New} nowych faktur [{Direction}]",
                windowFrom, windowTo, newInvoices.Count, direction);
        }

        if (allNewInvoices.Count > 0)
        {
            await _invoiceRepository.UpsertManyAsync(allNewInvoices);
            _logger.LogInformation("Zapisano łącznie {Count} nowych faktur [{Direction}]", allNewInvoices.Count, direction);
        }
        else
        {
            _logger.LogInformation("Brak nowych faktur [{Direction}]", direction);
        }

        return new InvoiceSyncResult
        {
            NewCount = allNewInvoices.Count,
            TotalFetched = totalFetched,
            SyncedAt = DateTime.UtcNow,
            Direction = direction
        };
    }

    public async Task<InvoiceQueryResponse> QueryInvoicesAsync(
        HttpClient client,
        InvoiceQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!_session.IsAuthenticated)
            throw new UnauthorizedAccessException("Nie jesteś zalogowany do KSeF");

        await _authService.RefreshTokenIfNeededAsync(cancellationToken);

        var httpClient = _httpClientFactory.CreateClient("KSeF");
        var allInvoices = new List<InvoiceMetadata>();
        var seenIds = new HashSet<string>();
        var maxResults = request.MaxResults ?? 9900;
        var sortOrder = request.SortDescending ? "Desc" : "Asc";
        var pageOffset = 0;
        const int pageSize = 100;
        const int maxPageOffset = 9900;
        var hasMore = true;
        var iteration = 0;
        const int maxIterations = 200;
        var currentFrom = request.DateRange!.From.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
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

            var response = await httpClient.SendAsync(httpRequest, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"Query failed: {response.StatusCode} - {content}");

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

            if (pageIsTruncated || pageOffset + pageSize >= maxPageOffset)
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

    private static List<(DateTime From, DateTime To)> BuildDateWindows(DateTime from, DateTime to, int months)
    {
        var windows = new List<(DateTime, DateTime)>();
        var current = from;

        while (current < to)
        {
            var windowEnd = current.AddMonths(months);
            if (windowEnd > to)
                windowEnd = to;

            windows.Add((current, windowEnd));
            current = windowEnd;
        }

        return windows;
    }

    private static InvoiceModel MapToInvoice(
        InvoiceMetadata metadata,
        int companyProfileId,
        string nip,
        string direction,
        string environment)
    {
        return new InvoiceModel
        {
            CompanyProfileId = companyProfileId,
            KsefReferenceNumber = metadata.KsefNumber,
            Nip = nip,
            InvoiceType = metadata.InvoiceType ?? "FA",
            Direction = direction,
            InvoiceNumber = metadata.InvoiceNumber,
            SellerNip = metadata.Seller?.Nip,
            SellerName = metadata.Seller?.Name,
            BuyerNip = metadata.Buyer?.Identifier?.Value,
            BuyerName = metadata.Buyer?.Name,
            NetAmount = metadata.NetAmount,
            VatAmount = metadata.VatAmount,
            GrossAmount = metadata.GrossAmount,
            Currency = metadata.Currency,
            InvoiceDate = ToUtc(metadata.InvoicingDate ?? metadata.PermanentStorageDate),
            AcquisitionTimestamp = ToUtc(metadata.AcquisitionDate) ?? DateTime.UtcNow,
            SyncedAt = DateTime.UtcNow,
            KsefEnvironment = environment
        };
    }

    private static DateTime? ToUtc(DateTime? dt)
    {
        if (dt == null) return null;
        return dt.Value.Kind == DateTimeKind.Utc
            ? dt.Value
            : DateTime.SpecifyKind(dt.Value, DateTimeKind.Utc);
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
}