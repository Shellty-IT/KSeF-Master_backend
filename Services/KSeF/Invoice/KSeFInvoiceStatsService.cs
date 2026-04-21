using KSeF.Backend.Models.Requests;
using KSeF.Backend.Models.Responses.Invoice;
using KSeF.Backend.Models.Responses.Stats;
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

    public async Task<InvoiceStatsResponse> GetStatsAsync(int months, CancellationToken cancellationToken = default)
    {
        var periodTo = DateTime.UtcNow.Date;
        var periodFrom = periodTo.AddMonths(-months);

        _logger.LogInformation("Computing stats from {From} to {To}", periodFrom, periodTo);

        var issuedRequest = new InvoiceQueryRequest
        {
            SubjectType = "Subject1",
            DateRange = new DateRangeFilter
            {
                From = periodFrom,
                To = periodTo,
                DateType = "InvoicingDate"
            }
        };

        var receivedRequest = new InvoiceQueryRequest
        {
            SubjectType = "Subject2",
            DateRange = new DateRangeFilter
            {
                From = periodFrom,
                To = periodTo,
                DateType = "InvoicingDate"
            }
        };

        InvoiceQueryResponse issued;
        InvoiceQueryResponse received;

        try
        {
            var httpClient = new HttpClient();
            issued = await _queryService.QueryInvoicesAsync(httpClient, issuedRequest, cancellationToken);
            received = await _queryService.QueryInvoicesAsync(httpClient, receivedRequest, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error computing stats");
            return new InvoiceStatsResponse
            {
                PeriodFrom = periodFrom,
                PeriodTo = periodTo
            };
        }

        var stats = new InvoiceStatsResponse
        {
            IssuedCount = issued.Invoices.Count,
            ReceivedCount = received.Invoices.Count,
            IssuedNetTotal = issued.Invoices.Sum(i => i.NetAmount ?? 0),
            IssuedGrossTotal = issued.Invoices.Sum(i => i.GrossAmount ?? 0),
            ReceivedNetTotal = received.Invoices.Sum(i => i.NetAmount ?? 0),
            ReceivedGrossTotal = received.Invoices.Sum(i => i.GrossAmount ?? 0),
            PeriodFrom = periodFrom,
            PeriodTo = periodTo
        };

        stats.Monthly = ComputeMonthlyStats(issued, received);
        stats.TopContractors = ComputeTopContractors(issued.Invoices.Concat(received.Invoices).ToList());
        stats.ByCurrency = ComputeCurrencyStats(issued.Invoices.Concat(received.Invoices).ToList());

        return stats;
    }

    private static List<MonthlyStats> ComputeMonthlyStats(
        InvoiceQueryResponse issued,
        InvoiceQueryResponse received)
    {
        var monthlyStats = new Dictionary<string, MonthlyStats>();

        foreach (var inv in issued.Invoices)
        {
            if (!inv.InvoicingDate.HasValue) continue;
            var month = inv.InvoicingDate.Value.ToString("yyyy-MM");

            if (!monthlyStats.ContainsKey(month))
                monthlyStats[month] = new MonthlyStats { Month = month };

            monthlyStats[month].IssuedCount++;
            monthlyStats[month].IssuedGross += inv.GrossAmount ?? 0;
        }

        foreach (var inv in received.Invoices)
        {
            if (!inv.InvoicingDate.HasValue) continue;
            var month = inv.InvoicingDate.Value.ToString("yyyy-MM");

            if (!monthlyStats.ContainsKey(month))
                monthlyStats[month] = new MonthlyStats { Month = month };

            monthlyStats[month].ReceivedCount++;
            monthlyStats[month].ReceivedGross += inv.GrossAmount ?? 0;
        }

        return monthlyStats.Values.OrderBy(m => m.Month).ToList();
    }

    private static Dictionary<string, int> ComputeTopContractors(List<InvoiceMetadata> invoices)
    {
        return invoices
            .Where(i => !string.IsNullOrEmpty(i.Seller?.Name))
            .GroupBy(i => i.Seller!.Name!)
            .OrderByDescending(g => g.Count())
            .Take(10)
            .ToDictionary(g => g.Key, g => g.Count());
    }

    private static Dictionary<string, CurrencyStats> ComputeCurrencyStats(List<InvoiceMetadata> invoices)
    {
        return invoices
            .Where(i => !string.IsNullOrEmpty(i.Currency))
            .GroupBy(i => i.Currency!)
            .ToDictionary(
                g => g.Key,
                g => new CurrencyStats
                {
                    Count = g.Count(),
                    NetTotal = g.Sum(i => i.NetAmount ?? 0),
                    GrossTotal = g.Sum(i => i.GrossAmount ?? 0)
                });
    }
}