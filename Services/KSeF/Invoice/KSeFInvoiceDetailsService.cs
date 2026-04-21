using System.Text;
using System.Text.Json;
using System.Xml;
using KSeF.Backend.Infrastructure.KSeF;
using KSeF.Backend.Models.Responses.Invoice;
using KSeF.Backend.Models.Responses.Common;
using KSeF.Backend.Services.Interfaces;

namespace KSeF.Backend.Services.KSeF.Invoice;

public class KSeFInvoiceDetailsService : IKSeFInvoiceDetailsService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IKSeFEnvironmentService _environmentService;
    private readonly KSeFSessionManager _sessionManager;
    private readonly ILogger<KSeFInvoiceDetailsService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public KSeFInvoiceDetailsService(
        IHttpClientFactory httpClientFactory,
        IKSeFEnvironmentService environmentService,
        KSeFSessionManager sessionManager,
        ILogger<KSeFInvoiceDetailsService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _environmentService = environmentService;
        _sessionManager = sessionManager;
        _logger = logger;
    }

    public async Task<InvoiceDetailsResult> GetInvoiceDetailsAsync(
        string ksefNumber,
        CancellationToken cancellationToken = default)
    {
        if (!_sessionManager.IsAuthenticated)
            throw new UnauthorizedAccessException("Brak aktywnej sesji KSeF");

        var client = CreateAuthenticatedClient();

        try
        {
            _logger.LogInformation("Fetching invoice details: {KsefNumber}", ksefNumber);

            var response = await client.GetAsync($"invoices/{ksefNumber}", cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var error = KSeFErrorParser.Parse(responseBody);
                return Fail($"Błąd pobierania faktury: {error}");
            }

            var details = JsonSerializer.Deserialize<InvoiceDetailsResponse>(responseBody, JsonOptions);

            if (details?.InvoicePayload?.InvoiceBody == null)
                return Fail("Brak danych faktury w odpowiedzi");

            var invoiceXml = Encoding.UTF8.GetString(Convert.FromBase64String(details.InvoicePayload.InvoiceBody));
            var parsed = ParseInvoiceXml(invoiceXml);

            return new InvoiceDetailsResult
            {
                Success = true,
                InvoiceHash = details.InvoiceHash?.HashSHA?.Value,
                InvoiceXml = invoiceXml,
                KsefNumber = ksefNumber,
                InvoiceNumber = parsed.InvoiceNumber,
                IssueDate = parsed.IssueDate,
                SellerNip = parsed.SellerNip,
                SellerName = parsed.SellerName,
                SellerAddress = parsed.SellerAddress,
                BuyerNip = parsed.BuyerNip,
                BuyerName = parsed.BuyerName,
                BuyerAddress = parsed.BuyerAddress,
                NetTotal = parsed.NetTotal,
                VatTotal = parsed.VatTotal,
                GrossTotal = parsed.GrossTotal,
                Items = parsed.Items
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching invoice details: {KsefNumber}", ksefNumber);
            return Fail(ex.Message);
        }
    }

    private HttpClient CreateAuthenticatedClient()
    {
        var client = _httpClientFactory.CreateClient("KSeF");
        client.BaseAddress = new Uri(_environmentService.GetApiBaseUrl("Test"));
        client.DefaultRequestHeaders.Add("SessionToken", _sessionManager.AccessToken);
        return client;
    }

    private static InvoiceDetailsResult Fail(string error) =>
        new() { Success = false, Error = error };

    private static ParsedInvoice ParseInvoiceXml(string xml)
    {
        var doc = new XmlDocument();
        doc.LoadXml(xml);

        var nsmgr = new XmlNamespaceManager(doc.NameTable);
        nsmgr.AddNamespace("f", "http://crd.gov.pl/wzor/2023/06/29/12648/");

        return new ParsedInvoice
        {
            InvoiceNumber = doc.SelectSingleNode("//f:P_2", nsmgr)?.InnerText,
            IssueDate = doc.SelectSingleNode("//f:P_1", nsmgr)?.InnerText,
            SellerNip = doc.SelectSingleNode("//f:Podmiot1/f:DaneIdentyfikacyjne/f:NIP", nsmgr)?.InnerText,
            SellerName = doc.SelectSingleNode("//f:Podmiot1/f:DaneIdentyfikacyjne/f:Nazwa", nsmgr)?.InnerText,
            SellerAddress = "N/A",
            BuyerNip = doc.SelectSingleNode("//f:Podmiot2/f:DaneIdentyfikacyjne/f:NIP", nsmgr)?.InnerText,
            BuyerName = doc.SelectSingleNode("//f:Podmiot2/f:DaneIdentyfikacyjne/f:Nazwa", nsmgr)?.InnerText,
            BuyerAddress = "N/A",
            NetTotal = 0,
            VatTotal = 0,
            GrossTotal = 0,
            Items = new List<InvoiceItemResult>()
        };
    }

    private class ParsedInvoice
    {
        public string? InvoiceNumber { get; set; }
        public string? IssueDate { get; set; }
        public string? SellerNip { get; set; }
        public string? SellerName { get; set; }
        public string? SellerAddress { get; set; }
        public string? BuyerNip { get; set; }
        public string? BuyerName { get; set; }
        public string? BuyerAddress { get; set; }
        public decimal NetTotal { get; set; }
        public decimal VatTotal { get; set; }
        public decimal GrossTotal { get; set; }
        public List<InvoiceItemResult> Items { get; set; } = new();
    }
}