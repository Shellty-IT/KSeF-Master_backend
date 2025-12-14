using System.Text;
using KSeF.Backend.Models.Requests;

namespace KSeF.Backend.Services;

/// <summary>
/// Generuje XML faktury zgodny ze schematem FA(3) 1-0E
/// Namespace: http://crd.gov.pl/wzor/2025/06/25/13775/
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
        
        // === DEKLARACJA XML ===
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
        sb.Append($"<KodKraju>{EscapeXml(invoice.Seller.CountryCode ?? "PL")}</KodKraju>");
        sb.Append($"<AdresL1>{EscapeXml(invoice.Seller.AddressLine1)}</AdresL1>");
        // AdresL2 - zawsze dodajemy, nawet jeśli pusty (niektóre walidatory tego wymagają)
        sb.Append($"<AdresL2>{EscapeXml(invoice.Seller.AddressLine2 ?? "")}</AdresL2>");
        sb.Append("</Adres>");
        sb.Append("<StatusInfoPodatnika>1</StatusInfoPodatnika>"); // 1 = podatnik VAT czynny
        sb.Append("</Podmiot1>");

        // === PODMIOT 2 (NABYWCA) ===
        sb.Append("<Podmiot2>");
        sb.Append("<DaneIdentyfikacyjne>");
        sb.Append($"<NIP>{EscapeXml(invoice.Buyer.Nip)}</NIP>");
        sb.Append($"<Nazwa>{EscapeXml(invoice.Buyer.Name)}</Nazwa>");
        sb.Append("</DaneIdentyfikacyjne>");
        sb.Append("<Adres>");
        sb.Append($"<KodKraju>{EscapeXml(invoice.Buyer.CountryCode ?? "PL")}</KodKraju>");
        sb.Append($"<AdresL1>{EscapeXml(invoice.Buyer.AddressLine1)}</AdresL1>");
        sb.Append($"<AdresL2>{EscapeXml(invoice.Buyer.AddressLine2 ?? "")}</AdresL2>");
        sb.Append("</Adres>");
        sb.Append("</Podmiot2>");

        // === FA (DANE FAKTURY) ===
        sb.Append("<Fa>");
        
        // Waluta
        sb.Append($"<KodWaluty>{EscapeXml(invoice.Currency ?? "PLN")}</KodWaluty>");
        
        // P_1 - Data wystawienia (YYYY-MM-DD)
        sb.Append($"<P_1>{EscapeXml(invoice.IssueDate)}</P_1>");
        
        // P_1M - Miejsce wystawienia (opcjonalne, ale zalecane)
        sb.Append($"<P_1M>{EscapeXml(invoice.IssuePlace ?? "Polska")}</P_1M>");
        
        // P_2 - Numer faktury
        sb.Append($"<P_2>{EscapeXml(invoice.InvoiceNumber)}</P_2>");
        
        // P_6 - Data sprzedaży/wykonania usługi
        sb.Append($"<P_6>{EscapeXml(invoice.SaleDate)}</P_6>");

        // === SUMY WEDŁUG STAWEK VAT ===
        // WAŻNE: Muszą być w kolejności P_13_1, P_14_1, P_13_2, P_14_2, itd.
        AppendVatSummary(sb, totals);

        // P_15 - Suma brutto (musi być PO wszystkich P_13/P_14)
        sb.Append($"<P_15>{FormatDecimal(totals.GrossTotal)}</P_15>");

        // === ADNOTACJE (wymagane) ===
        sb.Append("<Adnotacje>");
        sb.Append("<P_16>2</P_16>");  // Metoda kasowa: 2 = NIE
        sb.Append("<P_17>2</P_17>");  // Samofakturowanie: 2 = NIE
        sb.Append("<P_18>1</P_18>");  // Odwrotne obciążenie: 1 = NIE (lub 2 = TAK stosuje się)
        sb.Append("<P_18A>2</P_18A>"); // Mechanizm podzielonej płatności: 2 = NIE
        sb.Append("<Zwolnienie>");
        sb.Append("<P_19N>1</P_19N>"); // Zwolnienie z VAT: 1 = NIE DOTYCZY
        sb.Append("</Zwolnienie>");
        sb.Append("<NoweSrodkiTransportu>");
        sb.Append("<P_22N>1</P_22N>"); // Nowe środki transportu: 1 = NIE DOTYCZY
        sb.Append("</NoweSrodkiTransportu>");
        sb.Append("<P_23>1</P_23>");  // WDT: 1 = NIE (wewnątrzwspólnotowa dostawa)
        sb.Append("<PMarzy>");
        sb.Append("<P_PMarzyN>1</P_PMarzyN>"); // Procedura marży: 1 = NIE DOTYCZY
        sb.Append("</PMarzy>");
        sb.Append("</Adnotacje>");

        // Rodzaj faktury
        sb.Append("<RodzajFaktury>VAT</RodzajFaktury>");

        // === WIERSZE FAKTURY ===
        int lineNumber = 1;
        foreach (var item in invoice.Items)
        {
            var netValue = Math.Round(item.Quantity * item.UnitPriceNet, 2);

            sb.Append("<FaWiersz>");
            sb.Append($"<NrWierszaFa>{lineNumber++}</NrWierszaFa>");
            sb.Append($"<P_7>{EscapeXml(item.Name)}</P_7>");           // Nazwa towaru/usługi
            sb.Append($"<P_8A>{EscapeXml(item.Unit)}</P_8A>");         // Jednostka miary
            sb.Append($"<P_8B>{FormatQuantity(item.Quantity)}</P_8B>"); // Ilość
            sb.Append($"<P_9A>{FormatDecimal(item.UnitPriceNet)}</P_9A>"); // Cena jednostkowa netto
            sb.Append($"<P_11>{FormatDecimal(netValue)}</P_11>");      // Wartość netto
            sb.Append($"<P_12>{FormatVatRateForXml(item.VatRate)}</P_12>"); // Stawka VAT
            sb.Append("</FaWiersz>");
        }

        sb.Append("</Fa>");
        sb.Append("</Faktura>");

        var xml = sb.ToString();
        
        _logger.LogDebug("Wygenerowany XML faktury:\n{Xml}", PrettifyXml(xml));

        return xml;
    }

    #region VAT Summary

    private void AppendVatSummary(StringBuilder sb, InvoiceTotals totals)
    {
        // Kolejność jest WAŻNA! Zgodnie ze schematem:
        // P_13_1, P_14_1 (stawka 23%)
        // P_13_2, P_14_2 (stawka 8%)
        // P_13_3, P_14_3 (stawka 5%)
        // P_13_6 (stawka 0%)
        // P_13_7 (zwolnione)
        // P_13_10 (niepodlegające)

        // 23%
        if (totals.ByVatRate.TryGetValue("23", out var vat23))
        {
            sb.Append($"<P_13_1>{FormatDecimal(vat23.Net)}</P_13_1>");
            sb.Append($"<P_14_1>{FormatDecimal(vat23.Vat)}</P_14_1>");
        }

        // 22% (stara stawka, mapujemy na 23%)
        if (totals.ByVatRate.TryGetValue("22", out var vat22))
        {
            // Jeśli już był 23%, dodaj do niego
            if (!totals.ByVatRate.ContainsKey("23"))
            {
                sb.Append($"<P_13_1>{FormatDecimal(vat22.Net)}</P_13_1>");
                sb.Append($"<P_14_1>{FormatDecimal(vat22.Vat)}</P_14_1>");
            }
        }

        // 8%
        if (totals.ByVatRate.TryGetValue("8", out var vat8))
        {
            sb.Append($"<P_13_2>{FormatDecimal(vat8.Net)}</P_13_2>");
            sb.Append($"<P_14_2>{FormatDecimal(vat8.Vat)}</P_14_2>");
        }

        // 7% (stara stawka)
        if (totals.ByVatRate.TryGetValue("7", out var vat7))
        {
            if (!totals.ByVatRate.ContainsKey("8"))
            {
                sb.Append($"<P_13_2>{FormatDecimal(vat7.Net)}</P_13_2>");
                sb.Append($"<P_14_2>{FormatDecimal(vat7.Vat)}</P_14_2>");
            }
        }

        // 5%
        if (totals.ByVatRate.TryGetValue("5", out var vat5))
        {
            sb.Append($"<P_13_3>{FormatDecimal(vat5.Net)}</P_13_3>");
            sb.Append($"<P_14_3>{FormatDecimal(vat5.Vat)}</P_14_3>");
        }

        // 0%
        if (totals.ByVatRate.TryGetValue("0", out var vat0))
        {
            sb.Append($"<P_13_6>{FormatDecimal(vat0.Net)}</P_13_6>");
            // Brak P_14_6 bo VAT = 0
        }

        // ZW (zwolnione)
        if (totals.ByVatRate.TryGetValue("zw", out var vatZw))
        {
            sb.Append($"<P_13_7>{FormatDecimal(vatZw.Net)}</P_13_7>");
        }

        // NP (niepodlegające)
        if (totals.ByVatRate.TryGetValue("np", out var vatNp))
        {
            sb.Append($"<P_13_10>{FormatDecimal(vatNp.Net)}</P_13_10>");
        }
    }

    #endregion

    #region Calculations

    private InvoiceTotals CalculateTotals(List<InvoiceItem> items)
    {
        var totals = new InvoiceTotals();

        foreach (var item in items)
        {
            var netValue = Math.Round(item.Quantity * item.UnitPriceNet, 2);
            var vatRate = ParseVatRate(item.VatRate);
            var vatValue = Math.Round(netValue * vatRate / 100m, 2);

            totals.NetTotal += netValue;
            totals.VatTotal += vatValue;
            totals.GrossTotal += netValue + vatValue;

            // Grupuj według stawek (lowercase dla spójności)
            var key = item.VatRate.ToLowerInvariant();
            if (!totals.ByVatRate.ContainsKey(key))
                totals.ByVatRate[key] = (0, 0);

            var (net, vat) = totals.ByVatRate[key];
            totals.ByVatRate[key] = (net + netValue, vat + vatValue);
        }

        // Zaokrąglij sumy końcowe
        totals.NetTotal = Math.Round(totals.NetTotal, 2);
        totals.VatTotal = Math.Round(totals.VatTotal, 2);
        totals.GrossTotal = Math.Round(totals.GrossTotal, 2);

        return totals;
    }

    private decimal ParseVatRate(string vatRate)
    {
        return vatRate.ToLowerInvariant() switch
        {
            "23" => 23m,
            "22" => 22m,
            "8" => 8m,
            "7" => 7m,
            "5" => 5m,
            "0" => 0m,
            "zw" => 0m,
            "np" => 0m,
            "oo" => 0m, // odwrotne obciążenie
            _ => decimal.TryParse(vatRate, out var rate) ? rate : 23m
        };
    }

    #endregion

    #region Formatting

    private string FormatVatRateForXml(string vatRate)
    {
        // KSeF wymaga konkretnych wartości dla P_12
        return vatRate.ToLowerInvariant() switch
        {
            "23" => "23",
            "22" => "22",
            "8" => "8",
            "7" => "7",
            "5" => "5",
            "0" => "0",
            "zw" => "zw",
            "np" => "np",
            "oo" => "oo",
            _ => vatRate
        };
    }

    private string FormatDecimal(decimal value)
    {
        // KSeF wymaga kropki jako separatora, dokładnie 2 miejsca po przecinku
        return value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
    }

    private string FormatQuantity(decimal value)
    {
        // Ilość może mieć więcej miejsc dziesiętnych
        // Usuń zbędne zera na końcu
        var formatted = value.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
        return formatted;
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

    private string PrettifyXml(string xml)
    {
        try
        {
            var doc = new System.Xml.XmlDocument();
            doc.LoadXml(xml);
            
            using var sw = new StringWriter();
            using var xw = new System.Xml.XmlTextWriter(sw);
            xw.Formatting = System.Xml.Formatting.Indented;
            xw.Indentation = 2;
            doc.WriteTo(xw);
            return sw.ToString();
        }
        catch
        {
            return xml;
        }
    }

    #endregion

    #region Helper Classes

    private class InvoiceTotals
    {
        public decimal NetTotal { get; set; }
        public decimal VatTotal { get; set; }
        public decimal GrossTotal { get; set; }
        public Dictionary<string, (decimal Net, decimal Vat)> ByVatRate { get; } = new();
    }

    #endregion
}