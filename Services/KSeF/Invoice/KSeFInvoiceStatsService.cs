// Services/KSeF/Invoice/KSeFInvoiceStatsService.cs
using KSeF.Backend.Models.Requests;
using KSeF.Backend.Models.Responses;
using KSeF.Backend.Services.Interfaces;

namespace KSeF.Backend.Services.KSeF.Invoice;

public class KSeFInvoiceStatsService : IKSeFInvoiceStatsService
{
    private readonly IKSeFInvoiceQueryService _queryService;
    private readonly ILogger<KSeFInvoiceStatsService> _logger;

    public KSeFInvoiceStatsService(
        IKSeFInvoiceQueryService queryService,
        ILogger<KSeFInvoiceStatsService> logger)
    {
        _queryService = queryService;
        _logger = logger;
    }

    public async Task<InvoiceStatsResponse> GetInvoiceStatsAsync(int months = 3, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var windowCount = Math.Max(1, (int)Math.Ceiling(months / 3.0));

        var issuedTasks = new List<Task<InvoiceQueryResponse>>();
        var receivedTasks = new List<Task<InvoiceQueryResponse>>();

        for (var i = 0; i < windowCount; i++)
        {
            var windowTo = now.AddMonths(-i * 3);
            var windowFrom = now.AddMonths(-(i + 1) * 3);

            if (windowFrom < now.AddMonths(-months))
                windowFrom = now.AddMonths(-months);

            issuedTasks.Add(_queryService.GetInvoicesAsync(BuildRequest("Subject1", windowFrom, windowTo), ct));
            receivedTasks.Add(_queryService.GetInvoicesAsync(BuildRequest("Subject2", windowFrom, windowTo), ct));
        }

        await Task.WhenAll(issuedTasks.Concat(receivedTasks));

        var allIssued = MergeUnique(issuedTasks);
        var allReceived = MergeUnique(receivedTasks);

        var stats = new InvoiceStatsResponse
        {
            IssuedCount = allIssued.Count,
            ReceivedCount = allReceived.Count,
            IssuedNetTotal = allIssued.Sum(i => i.NetAmount ?? 0),
            IssuedGrossTotal = allIssued.Sum(i => i.GrossAmount ?? 0),
            ReceivedNetTotal = allReceived.Sum(i => i.NetAmount ?? 0),
            ReceivedGrossTotal = allReceived.Sum(i => i.GrossAmount ?? 0),
            PeriodFrom = now.AddMonths(-months),
            PeriodTo = now,
            FetchedAt = DateTime.UtcNow
        };

        var allMonths = Enumerable.Range(0, months)
            .Select(i => now.AddMonths(-i))
            .Select(d => d.ToString("yyyy-MM"))
            .Reverse()
            .ToList();

        foreach (var month in allMonths)
        {
            var monthIssued = allIssued.Where(i => GetMonth(i) == month).ToList();
            var monthReceived = allReceived.Where(i => GetMonth(i) == month).ToList();

            stats.Monthly.Add(new MonthlyStats
            {
                Month = month,
                IssuedCount = monthIssued.Count,
                ReceivedCount = monthReceived.Count,
                IssuedGross = monthIssued.Sum(i => i.GrossAmount ?? 0),
                ReceivedGross = monthReceived.Sum(i => i.GrossAmount ?? 0)
            });
        }

        stats.TopContractors = allReceived
            .Where(i => i.Seller?.Nip != null)
            .GroupBy(i => i.Seller!.Nip!)
            .OrderByDescending(g => g.Count())
            .Take(10)
            .ToDictionary(g => g.Key, g => g.Count());

        stats.ByCurrency = allIssued.Concat(allReceived)
            .GroupBy(i => i.Currency ?? "PLN")
            .ToDictionary(
                g => g.Key,
                g => new CurrencyStats
                {
                    Count = g.Count(),
                    NetTotal = g.Sum(i => i.NetAmount ?? 0),
                    GrossTotal = g.Sum(i => i.GrossAmount ?? 0)
                });

        return stats;
    }

    private static InvoiceQueryRequest BuildRequest(string subjectType, DateTime from, DateTime to)
    {
        return new InvoiceQueryRequest
        {
            SubjectType = subjectType,
            DateRange = new DateRangeFilter
            {
                DateType = "PermanentStorage",
                From = from,
                To = to
            },
            SortDescending = false
        };
    }

    private static List<InvoiceMetadata> MergeUnique(List<Task<InvoiceQueryResponse>> tasks)
    {
        var seen = new HashSet<string>();
        var result = new List<InvoiceMetadata>();

        foreach (var task in tasks)
            foreach (var inv in task.Result.Invoices)
                if (seen.Add(inv.KsefNumber))
                    result.Add(inv);

        return result;
    }

    private static string GetMonth(InvoiceMetadata invoice)
    {
        var date = invoice.InvoicingDate
            ?? invoice.PermanentStorageDate
            ?? (DateTime.TryParse(invoice.IssueDate, out var parsed) ? parsed : DateTime.MinValue);
        return date.ToString("yyyy-MM");
    }
}