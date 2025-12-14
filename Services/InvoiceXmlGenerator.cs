using System.Text;
using System.Xml;
using KSeF.Backend.Models.Requests;

namespace KSeF.Backend.Services;

/// <summary>
/// Generuje XML faktury zgodny ze schematem FA(3) 1-0E
/// </summary>
public class InvoiceXmlGenerator
{
    private readonly ILogger<InvoiceXmlGenerator> _logger;

    public InvoiceXmlGenerator(ILogger<InvoiceXmlGenerator> logger)
    {
        _logger = logger;
    }

    public string GenerateInvoiceXml(CreateInvoiceRequest invoice)
    {
        _logger.LogInformation("Generowanie XML faktury: {Number}", invoice.InvoiceNumber);

        var now = DateTime.UtcNow;
        var dateTime = now.ToString("yyyy-MM-ddTHH:mm:ss.ffffffZ");

        // Oblicz sumy
        var totals = CalculateTotals(invoice.Items);

        var sb = new StringBuilder();
        sb.Append(@"<?xml version=""1.0"" encoding=""UTF-8""?>");
        sb.Append(@"<Faktura xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" ");
        sb.Append(@"xmlns:xsd=""http://www.w3.org/2001/XMLSchema"" ");
        sb.Append(@"xmlns=""http://crd.gov.pl/wzor/2025/06/25/13775/"">");

        // === NAGŁÓWEK ===
        sb.Append("<Naglowek>");
        sb.Append(@"<KodFormularza kodSystemowy=""FA (3)"" wersjaSchemy=""1-0E"">FA</KodFormularza>");
        sb.Append("<WariantFormularza>3</WariantFormularza>");
        sb.Append($"<DataWytworzeniaFa>{dateTime}</DataWytworzeniaFa>");
        sb.Append("<SystemInfo>KSeF Backend API</SystemInfo>");
        sb.Append("</Naglowek>");

        // === PODMIOT 1 (SPRZEDAWCA) ===
        sb.Append("<Podmiot1>");
        sb.Append("<DaneIdentyfikacyjne>");
        sb.Append($"<NIP>{EscapeXml(invoice.Seller.Nip)}</NIP>");
        sb.Append($"<Nazwa>{EscapeXml(invoice.Seller.Name)}</Nazwa>");
        sb.Append("</DaneIdentyfikacyjne>");
        sb.Append("<Adres>");
        sb.Append($"<KodKraju>{EscapeXml(invoice.Seller.CountryCode)}</KodKraju>");
        sb.Append($"<AdresL1>{EscapeXml(invoice.Seller.AddressLine1)}</AdresL1>");
        if (!string.IsNullOrEmpty(invoice.Seller.AddressLine2))
            sb.Append($"<AdresL2>{EscapeXml(invoice.Seller.AddressLine2)}</AdresL2>");
        sb.Append("</Adres>");
        sb.Append("<StatusInfoPodatnika>1</StatusInfoPodatnika>");
        sb.Append("</Podmiot1>");

        // === PODMIOT 2 (NABYWCA) ===
        sb.Append("<Podmiot2>");
        sb.Append("<DaneIdentyfikacyjne>");
        sb.Append($"<NIP>{EscapeXml(invoice.Buyer.Nip)}</NIP>");
        sb.Append($"<Nazwa>{EscapeXml(invoice.Buyer.Name)}</Nazwa>");
        sb.Append("</DaneIdentyfikacyjne>");
        sb.Append("<Adres>");
        sb.Append($"<KodKraju>{EscapeXml(invoice.Buyer.CountryCode)}</KodKraju>");
        sb.Append($"<AdresL1>{EscapeXml(invoice.Buyer.AddressLine1)}</AdresL1>");
        if (!string.IsNullOrEmpty(invoice.Buyer.AddressLine2))
            sb.Append($"<AdresL2>{EscapeXml(invoice.Buyer.AddressLine2)}</AdresL2>");
        sb.Append("</Adres>");
        sb.Append("</Podmiot2>");

        // === FA (DANE FAKTURY) ===
        sb.Append("<Fa>");
        sb.Append($"<KodWaluty>{EscapeXml(invoice.Currency)}</KodWaluty>");
        sb.Append($"<P_1>{EscapeXml(invoice.IssueDate)}</P_1>"); // Data wystawienia
        if (!string.IsNullOrEmpty(invoice.IssuePlace))
            sb.Append($"<P_1M>{EscapeXml(invoice.IssuePlace)}</P_1M>");
        sb.Append($"<P_2>{EscapeXml(invoice.InvoiceNumber)}</P_2>"); // Numer faktury
        sb.Append($"<P_6>{EscapeXml(invoice.SaleDate)}</P_6>"); // Data sprzedaży

        // Sumy netto i VAT według stawek
        AppendVatSummary(sb, totals);

        sb.Append($"<P_15>{FormatDecimal(totals.GrossTotal)}</P_15>"); // Suma brutto

        // === ADNOTACJE ===
        sb.Append("<Adnotacje>");
        sb.Append("<P_16>2</P_16>"); // Metoda kasowa: 2 = nie
        sb.Append("<P_17>2</P_17>"); // Samofakturowanie: 2 = nie
        sb.Append("<P_18>1</P_18>"); // Odwrotne obciążenie: 1 = nie dotyczy
        sb.Append("<P_18A>2</P_18A>"); // Procedura marży: 2 = nie
        sb.Append("<Zwolnienie><P_19N>1</P_19N></Zwolnienie>");
        sb.Append("<NoweSrodkiTransportu><P_22N>1</P_22N></NoweSrodkiTransportu>");
        sb.Append("<P_23>1</P_23>"); // Wewnątrzwspólnotowa dostawa: 1 = nie
        sb.Append("<PMarzy><P_PMarzyN>1</P_PMarzyN></PMarzy>");
        sb.Append("</Adnotacje>");

        sb.Append("<RodzajFaktury>VAT</RodzajFaktury>");

        // === WIERSZE FAKTURY ===
        int lineNumber = 1;
        foreach (var item in invoice.Items)
        {
            var netValue = item.Quantity * item.UnitPriceNet;
            var vatRateNumeric = ParseVatRate(item.VatRate);

            sb.Append("<FaWiersz>");
            sb.Append($"<NrWierszaFa>{lineNumber++}</NrWierszaFa>");
            sb.Append($"<P_7>{EscapeXml(item.Name)}</P_7>"); // Nazwa
            sb.Append($"<P_8A>{EscapeXml(item.Unit)}</P_8A>"); // Jednostka
            sb.Append($"<P_8B>{FormatDecimal(item.Quantity)}</P_8B>"); // Ilość
            sb.Append($"<P_9A>{FormatDecimal(item.UnitPriceNet)}</P_9A>"); // Cena netto
            sb.Append($"<P_11>{FormatDecimal(netValue)}</P_11>"); // Wartość netto
            sb.Append($"<P_12>{FormatVatRateForXml(item.VatRate)}</P_12>"); // Stawka VAT
            sb.Append("</FaWiersz>");
        }

        sb.Append("</Fa>");
        sb.Append("</Faktura>");

        var xml = sb.ToString();
        _logger.LogDebug("Wygenerowany XML:\n{Xml}", xml);

        return xml;
    }

    #region Helpers

    private InvoiceTotals CalculateTotals(List<InvoiceItem> items)
    {
        var totals = new InvoiceTotals();

        foreach (var item in items)
        {
            var netValue = item.Quantity * item.UnitPriceNet;
            var vatRate = ParseVatRate(item.VatRate);
            var vatValue = netValue * vatRate / 100m;

            totals.NetTotal += netValue;
            totals.VatTotal += vatValue;
            totals.GrossTotal += netValue + vatValue;

            // Grupuj według stawek
            var key = item.VatRate;
            if (!totals.ByVatRate.ContainsKey(key))
                totals.ByVatRate[key] = (0, 0);

            var (net, vat) = totals.ByVatRate[key];
            totals.ByVatRate[key] = (net + netValue, vat + vatValue);
        }

        return totals;
    }

    private void AppendVatSummary(StringBuilder sb, InvoiceTotals totals)
    {
        // P_13_X - sumy netto według stawek
        // P_14_X - sumy VAT według stawek

        foreach (var (rate, (net, vat)) in totals.ByVatRate)
        {
            switch (rate)
            {
                case "23":
                    sb.Append($"<P_13_1>{FormatDecimal(net)}</P_13_1>");
                    sb.Append($"<P_14_1>{FormatDecimal(vat)}</P_14_1>");
                    break;
                case "22":
                    sb.Append($"<P_13_1>{FormatDecimal(net)}</P_13_1>");
                    sb.Append($"<P_14_1>{FormatDecimal(vat)}</P_14_1>");
                    break;
                case "8":
                    sb.Append($"<P_13_2>{FormatDecimal(net)}</P_13_2>");
                    sb.Append($"<P_14_2>{FormatDecimal(vat)}</P_14_2>");
                    break;
                case "5":
                    sb.Append($"<P_13_3>{FormatDecimal(net)}</P_13_3>");
                    sb.Append($"<P_14_3>{FormatDecimal(vat)}</P_14_3>");
                    break;
                case "0":
                    sb.Append($"<P_13_6>{FormatDecimal(net)}</P_13_6>");
                    break;
                case "zw":
                    sb.Append($"<P_13_7>{FormatDecimal(net)}</P_13_7>");
                    break;
                case "np":
                    sb.Append($"<P_13_10>{FormatDecimal(net)}</P_13_10>");
                    break;
            }
        }
    }

    private decimal ParseVatRate(string vatRate)
    {
        return vatRate.ToLower() switch
        {
            "23" => 23m,
            "22" => 22m,
            "8" => 8m,
            "7" => 7m,
            "5" => 5m,
            "0" => 0m,
            "zw" => 0m,
            "np" => 0m,
            _ => decimal.TryParse(vatRate, out var rate) ? rate : 23m
        };
    }

    private string FormatVatRateForXml(string vatRate)
    {
        // KSeF wymaga konkretnych wartości dla P_12
        return vatRate.ToLower() switch
        {
            "23" => "23",
            "22" => "22",
            "8" => "8",
            "7" => "7",
            "5" => "5",
            "0" => "0",
            "zw" => "zw",
            "np" => "np",
            "oo" => "oo", // odwrotne obciążenie
            _ => vatRate
        };
    }

    private string FormatDecimal(decimal value)
    {
        // KSeF wymaga kropki jako separatora dziesiętnego, max 2 miejsca
        return value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
    }

    private string EscapeXml(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        return value
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");
    }

    private class InvoiceTotals
    {
        public decimal NetTotal { get; set; }
        public decimal VatTotal { get; set; }
        public decimal GrossTotal { get; set; }
        public Dictionary<string, (decimal Net, decimal Vat)> ByVatRate { get; } = new();
    }

    #endregion
}