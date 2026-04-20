// Services/KSeF/Invoice/KSeFInvoiceDetailsService.cs
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using KSeF.Backend.Models.Responses;
using KSeF.Backend.Services.Interfaces;

namespace KSeF.Backend.Services.KSeF.Invoice;

public class KSeFInvoiceDetailsService : IKSeFInvoiceDetailsService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IKSeFAuthService _authService;
    private readonly KSeFSessionManager _session;
    private readonly ILogger<KSeFInvoiceDetailsService> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public KSeFInvoiceDetailsService(
        IHttpClientFactory httpClientFactory,
        IKSeFAuthService authService,
        KSeFSessionManager session,
        ILogger<KSeFInvoiceDetailsService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _authService = authService;
        _session = session;
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    public async Task<InvoiceDetailsResult> GetInvoiceDetailsAsync(string ksefNumber, CancellationToken ct = default)
    {
        if (!_session.IsAuthenticated)
            throw new UnauthorizedAccessException("Nie jesteś zalogowany do KSeF");

        await _authService.RefreshTokenIfNeededAsync(ct);

        try
        {
            var client = _httpClientFactory.CreateClient("KSeF");
            var endpoints = new[]
            {
                $"online/Invoice/Get/{ksefNumber}",
                $"invoices/{ksefNumber}",
                $"online/invoices/{ksefNumber}"
            };

            HttpResponseMessage? response = null;
            string? content = null;

            foreach (var endpoint in endpoints)
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, endpoint);
                req.Headers.Add("Authorization", $"Bearer {_session.AccessToken}");
                response = await client.SendAsync(req, ct);
                content = await response.Content.ReadAsStringAsync(ct);
                if (response.IsSuccessStatusCode) break;
            }

            if (response == null || !response.IsSuccessStatusCode)
                return new InvoiceDetailsResult { Success = false, Error = $"Nie można pobrać faktury: {response?.StatusCode}" };

            var detailsResponse = JsonSerializer.Deserialize<InvoiceDetailsResponse>(content!, _jsonOptions);
            if (detailsResponse == null)
                return new InvoiceDetailsResult { Success = false, Error = "Pusta odpowiedź z KSeF" };

            var invoiceHash = detailsResponse.InvoiceHash?.HashSHA?.Value ?? "";
            var invoiceXml = DecodeInvoiceBody(detailsResponse.InvoicePayload?.InvoiceBody);

            if (string.IsNullOrEmpty(invoiceXml))
                return new InvoiceDetailsResult { Success = false, Error = "Brak XML faktury w odpowiedzi" };

            var result = ParseInvoiceXml(invoiceXml, ksefNumber, invoiceHash);
            result.Success = true;
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Błąd pobierania szczegółów faktury {KsefNumber}", ksefNumber);
            return new InvoiceDetailsResult { Success = false, Error = ex.Message };
        }
    }

    private static string? DecodeInvoiceBody(string? invoiceBody)
    {
        if (string.IsNullOrEmpty(invoiceBody)) return null;
        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(invoiceBody));
        }
        catch
        {
            return invoiceBody;
        }
    }

    private static InvoiceDetailsResult ParseInvoiceXml(string xml, string ksefNumber, string invoiceHash)
    {
        var doc = XDocument.Parse(xml);
        var ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;

        var result = new InvoiceDetailsResult
        {
            KsefNumber = ksefNumber,
            InvoiceHash = invoiceHash,
            InvoiceXml = xml,
            InvoiceNumber = doc.Descendants(ns + "P_2").FirstOrDefault()?.Value,
            IssueDate = doc.Descendants(ns + "P_1").FirstOrDefault()?.Value,
            Items = new List<InvoiceItemResult>()
        };

        var podmiot1 = doc.Descendants(ns + "Podmiot1").FirstOrDefault();
        if (podmiot1 != null)
        {
            var dane = podmiot1.Descendants(ns + "DaneIdentyfikacyjne").FirstOrDefault();
            var adres = podmiot1.Descendants(ns + "Adres").FirstOrDefault();
            result.SellerNip = dane?.Element(ns + "NIP")?.Value;
            result.SellerName = dane?.Element(ns + "Nazwa")?.Value;
            result.SellerAddress = adres?.Element(ns + "AdresL1")?.Value;
        }

        var podmiot2 = doc.Descendants(ns + "Podmiot2").FirstOrDefault();
        if (podmiot2 != null)
        {
            var dane = podmiot2.Descendants(ns + "DaneIdentyfikacyjne").FirstOrDefault();
            var adres = podmiot2.Descendants(ns + "Adres").FirstOrDefault();
            result.BuyerNip = dane?.Element(ns + "NIP")?.Value;
            result.BuyerName = dane?.Element(ns + "Nazwa")?.Value;
            result.BuyerAddress = adres?.Element(ns + "AdresL1")?.Value;
        }

        foreach (var wiersz in doc.Descendants(ns + "FaWiersz"))
        {
            decimal.TryParse(wiersz.Element(ns + "P_8B")?.Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var qty);
            decimal.TryParse(wiersz.Element(ns + "P_9A")?.Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var price);
            decimal.TryParse(wiersz.Element(ns + "P_11")?.Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var netValue);
            var vatRate = wiersz.Element(ns + "P_12")?.Value ?? "23";
            var vatValue = CalculateVatFromRate(netValue, vatRate);

            result.Items!.Add(new InvoiceItemResult
            {
                Name = wiersz.Element(ns + "P_7")?.Value ?? "",
                Unit = wiersz.Element(ns + "P_8A")?.Value ?? "szt.",
                Quantity = qty > 0 ? qty : 1,
                UnitPriceNet = price,
                VatRate = vatRate,
                NetValue = netValue,
                VatValue = vatValue,
                GrossValue = netValue + vatValue
            });
        }

        var fa = doc.Descendants(ns + "Fa").FirstOrDefault();
        if (fa != null)
        {
            decimal.TryParse(fa.Element(ns + "P_13_1")?.Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var net);
            decimal.TryParse(fa.Element(ns + "P_14_1")?.Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var vat);
            decimal.TryParse(fa.Element(ns + "P_15")?.Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var gross);
            result.NetTotal = net;
            result.VatTotal = vat;
            result.GrossTotal = gross;
        }

        return result;
    }

    private static decimal CalculateVatFromRate(decimal net, string vatRate)
    {
        if (decimal.TryParse(vatRate, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var rate))
            return Math.Round(net * rate / 100, 2);
        return vatRate.ToLower() switch { "zw" or "np" or "oo" => 0, _ => Math.Round(net * 0.23m, 2) };
    }
}