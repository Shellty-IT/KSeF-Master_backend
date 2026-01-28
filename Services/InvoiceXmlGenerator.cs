using System.Text;
using KSeF.Backend.Models.Requests;

namespace KSeF.Backend.Services;

public class InvoiceXmlGenerator
{
    private readonly ILogger<InvoiceXmlGenerator> _logger;

    public InvoiceXmlGenerator(ILogger<InvoiceXmlGenerator> logger)
    {
        _logger = logger;
    }

    public string GenerateInvoiceXml(CreateInvoiceRequest invoice, string? sessionNip = null)
    {
        _logger.LogInformation("Generowanie XML faktury: {Number}", invoice.InvoiceNumber);

        if (!string.IsNullOrEmpty(sessionNip) && invoice.Seller.Nip != sessionNip)
        {
            _logger.LogWarning(
                "NIP sprzedawcy ({SellerNip}) różni się od NIP sesji ({SessionNip}). " +
                "KSeF może odrzucić fakturę!",
                invoice.Seller.Nip, sessionNip);
        }

        var now = DateTime.UtcNow;
        var dateTime = now.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ");

        var totals = CalculateTotals(invoice.Items);

        var sb = new StringBuilder();

        // Deklaracja XML + namespace FA(3) 2025/06/25/13775
        sb.Append(@"<?xml version=""1.0"" encoding=""utf-8""?>");
        sb.Append(@"<Faktura xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" ");
        sb.Append(@"xmlns:xsd=""http://www.w3.org/2001/XMLSchema"" ");
        sb.Append(@"xmlns=""http://crd.gov.pl/wzor/2025/06/25/13775/"">");

        // Nagłówek
        sb.Append("<Naglowek>");
        sb.Append(@"<KodFormularza kodSystemowy=""FA (3)"" wersjaSchemy=""1-0E"">FA</KodFormularza>");
        sb.Append("<WariantFormularza>3</WariantFormularza>");
        sb.Append($"<DataWytworzeniaFa>{dateTime}</DataWytworzeniaFa>");
        sb.Append("<SystemInfo>Aplikacja Podatnika KSeF</SystemInfo>");
        sb.Append("</Naglowek>");

        // Podmiot1 – sprzedawca
        sb.Append("<Podmiot1>");
        sb.Append("<DaneIdentyfikacyjne>");
        sb.Append($"<NIP>{EscapeXml(invoice.Seller.Nip)}</NIP>");
        sb.Append($"<Nazwa>{EscapeXml(invoice.Seller.Name)}</Nazwa>");
        sb.Append("</DaneIdentyfikacyjne>");
        sb.Append("<Adres>");
        sb.Append($"<KodKraju>{EscapeXml(invoice.Seller.CountryCode)}</KodKraju>");
        sb.Append($"<AdresL1>{EscapeXml(invoice.Seller.AddressLine1)}</AdresL1>");
        if (!string.IsNullOrEmpty(invoice.Seller.AddressLine2))
        {
            sb.Append($"<AdresL2>{EscapeXml(invoice.Seller.AddressLine2)}</AdresL2>");
        }
        sb.Append("</Adres>");
        sb.Append("</Podmiot1>");

        // Podmiot2 – nabywca + JST + GV (tak jak w Twoim przykładzie)
        sb.Append("<Podmiot2>");
        sb.Append("<DaneIdentyfikacyjne>");
        sb.Append($"<NIP>{EscapeXml(invoice.Buyer.Nip)}</NIP>");
        sb.Append($"<Nazwa>{EscapeXml(invoice.Buyer.Name)}</Nazwa>");
        sb.Append("</DaneIdentyfikacyjne>");
        sb.Append("<Adres>");
        sb.Append($"<KodKraju>{EscapeXml(invoice.Buyer.CountryCode)}</KodKraju>");
        sb.Append($"<AdresL1>{EscapeXml(invoice.Buyer.AddressLine1)}</AdresL1>");
        if (!string.IsNullOrEmpty(invoice.Buyer.AddressLine2))
        {
            sb.Append($"<AdresL2>{EscapeXml(invoice.Buyer.AddressLine2)}</AdresL2>");
        }
        sb.Append("</Adres>");
        sb.Append("<JST>2</JST>");
        sb.Append("<GV>2</GV>");
        sb.Append("</Podmiot2>");

        // Fa – dane faktury
        sb.Append("<Fa>");

        sb.Append($"<KodWaluty>{EscapeXml(invoice.Currency)}</KodWaluty>");
        sb.Append($"<P_1>{EscapeXml(invoice.IssueDate)}</P_1>");
        sb.Append($"<P_2>{EscapeXml(invoice.InvoiceNumber)}</P_2>");
        sb.Append($"<P_6>{EscapeXml(invoice.SaleDate)}</P_6>");

        // Sumy VAT (P_13_x / P_14_x)
        AppendVatSummary(sb, totals);

        // P_15 – kwota należności ogółem (brutto)
        sb.Append($"<P_15>{FormatAmount(totals.GrossTotal)}</P_15>");

        // Adnotacje – stałe wartości jak w przykładzie
        sb.Append("<Adnotacje>");
        sb.Append("<P_16>2</P_16>");
        sb.Append("<P_17>2</P_17>");
        sb.Append("<P_18>2</P_18>");
        sb.Append("<P_18A>2</P_18A>");
        sb.Append("<Zwolnienie><P_19N>1</P_19N></Zwolnienie>");
        sb.Append("<NoweSrodkiTransportu><P_22N>1</P_22N></NoweSrodkiTransportu>");
        sb.Append("<P_23>2</P_23>");
        sb.Append("<PMarzy><P_PMarzyN>1</P_PMarzyN></PMarzy>");
        sb.Append("</Adnotacje>");

        // Rodzaj faktury
        sb.Append("<RodzajFaktury>VAT</RodzajFaktury>");

        // Pozycje – FaWiersz
        int lineNumber = 1;
        foreach (var item in invoice.Items)
        {
            var netValue = Math.Round(item.Quantity * item.UnitPriceNet, 2);

            sb.Append("<FaWiersz>");
            sb.Append($"<NrWierszaFa>{lineNumber++}</NrWierszaFa>");
            sb.Append($"<P_7>{EscapeXml(item.Name)}</P_7>");
            sb.Append($"<P_8A>{EscapeXml(item.Unit)}</P_8A>");
            sb.Append($"<P_8B>{FormatQuantity(item.Quantity)}</P_8B>");
            sb.Append($"<P_9A>{FormatAmount(item.UnitPriceNet)}</P_9A>");
            sb.Append($"<P_11>{FormatAmount(netValue)}</P_11>");
            sb.Append($"<P_12>{FormatVatRate(item.VatRate)}</P_12>");
            sb.Append("</FaWiersz>");
        }

        // (Opcjonalnie moglibyśmy dodać Platnosc, ale na razie trzymamy się minimalnego, działającego wzorca)

        sb.Append("</Fa>");
        sb.Append("</Faktura>");

        var xml = sb.ToString();
        _logger.LogDebug("Wygenerowany XML faktury ({Size} bajtów)", xml.Length);

        return xml;
    }

    #region VAT Summary / Totals

    private void AppendVatSummary(StringBuilder sb, InvoiceTotals totals)
    {
        // 23% / 22% -> P_13_1 / P_14_1
        if (totals.ByVatRate.TryGetValue("23", out var vat23))
        {
            sb.Append($"<P_13_1>{FormatAmount(vat23.Net)}</P_13_1>");
            sb.Append($"<P_14_1>{FormatAmount(vat23.Vat)}</P_14_1>");
        }
        else if (totals.ByVatRate.TryGetValue("22", out var vat22))
        {
            sb.Append($"<P_13_1>{FormatAmount(vat22.Net)}</P_13_1>");
            sb.Append($"<P_14_1>{FormatAmount(vat22.Vat)}</P_14_1>");
        }

        // 8% / 7% -> P_13_2 / P_14_2
        if (totals.ByVatRate.TryGetValue("8", out var vat8))
        {
            sb.Append($"<P_13_2>{FormatAmount(vat8.Net)}</P_13_2>");
            sb.Append($"<P_14_2>{FormatAmount(vat8.Vat)}</P_14_2>");
        }
        else if (totals.ByVatRate.TryGetValue("7", out var vat7))
        {
            sb.Append($"<P_13_2>{FormatAmount(vat7.Net)}</P_13_2>");
            sb.Append($"<P_14_2>{FormatAmount(vat7.Vat)}</P_14_2>");
        }

        // 5% -> P_13_3 / P_14_3
        if (totals.ByVatRate.TryGetValue("5", out var vat5))
        {
            sb.Append($"<P_13_3>{FormatAmount(vat5.Net)}</P_13_3>");
            sb.Append($"<P_14_3>{FormatAmount(vat5.Vat)}</P_14_3>");
        }

        // 0% -> P_13_6 (tak jak w Twoim pierwotnym generatorze FA(3))
        if (totals.ByVatRate.TryGetValue("0", out var vat0))
        {
            sb.Append($"<P_13_6>{FormatAmount(vat0.Net)}</P_13_6>");
        }

        // zw -> P_13_7
        if (totals.ByVatRate.TryGetValue("zw", out var vatZw))
        {
            sb.Append($"<P_13_7>{FormatAmount(vatZw.Net)}</P_13_7>");
        }

        // np -> P_13_10 (odwrotne obciążenie / inne przypadki – uproszczone mapowanie)
        if (totals.ByVatRate.TryGetValue("np", out var vatNp))
        {
            sb.Append($"<P_13_10>{FormatAmount(vatNp.Net)}</P_13_10>");
        }
    }

    private InvoiceTotals CalculateTotals(List<InvoiceItem> items)
    {
        var totals = new InvoiceTotals();

        foreach (var item in items)
        {
            var netValue = Math.Round(item.Quantity * item.UnitPriceNet, 2);
            var vatRatePercent = ParseVatRateToPercent(item.VatRate);
            var vatValue = Math.Round(netValue * vatRatePercent / 100m, 2);

            totals.NetTotal += netValue;
            totals.VatTotal += vatValue;
            totals.GrossTotal += netValue + vatValue;

            var key = NormalizeVatRateKey(item.VatRate);
            if (!totals.ByVatRate.ContainsKey(key))
                totals.ByVatRate[key] = (0, 0);

            var (net, vat) = totals.ByVatRate[key];
            totals.ByVatRate[key] = (net + netValue, vat + vatValue);
        }

        totals.NetTotal = Math.Round(totals.NetTotal, 2);
        totals.VatTotal = Math.Round(totals.VatTotal, 2);
        totals.GrossTotal = Math.Round(totals.GrossTotal, 2);

        return totals;
    }

    private decimal ParseVatRateToPercent(string vatRate)
    {
        var normalized = vatRate.Trim().ToLowerInvariant();
        return normalized switch
        {
            "23" => 23m,
            "22" => 22m,
            "8" => 8m,
            "7" => 7m,
            "5" => 5m,
            "0" => 0m,
            "zw" or "np" or "oo" => 0m,
            _ => decimal.TryParse(vatRate, out var rate) ? rate : 23m
        };
    }

    private string NormalizeVatRateKey(string vatRate)
    {
        var normalized = vatRate.Trim().ToLowerInvariant();
        if (decimal.TryParse(normalized, out var numRate))
            return ((int)numRate).ToString();
        return normalized;
    }

    #endregion

    #region Formatowanie / Escape

    private string FormatVatRate(string vatRate)
    {
        var normalized = vatRate.Trim().ToLowerInvariant();
        return normalized switch
        {
            "23" or "22" or "8" or "7" or "5" or "0" => normalized,
            "zw" or "np" or "oo" => normalized,
            _ => vatRate.Trim()
        };
    }

    private string FormatAmount(decimal value)
    {
        value = Math.Round(value, 2);
        if (value == Math.Floor(value))
            return ((long)value).ToString(System.Globalization.CultureInfo.InvariantCulture);
        return value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
    }

    private string FormatQuantity(decimal value)
    {
        if (value == Math.Floor(value))
            return ((long)value).ToString(System.Globalization.CultureInfo.InvariantCulture);
        return value.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
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

    #endregion

    #region Helper class

    private class InvoiceTotals
    {
        public decimal NetTotal { get; set; }
        public decimal VatTotal { get; set; }
        public decimal GrossTotal { get; set; }
        public Dictionary<string, (decimal Net, decimal Vat)> ByVatRate { get; } = new();
    }

    #endregion
}