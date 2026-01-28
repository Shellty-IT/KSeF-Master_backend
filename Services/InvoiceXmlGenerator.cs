using System.Text;
using KSeF.Backend.Models.Requests;

namespace KSeF.Backend.Services;

public class InvoiceXmlGenerator
{
    private readonly ILogger<InvoiceXmlGenerator> _logger;
    private const string Namespace = "http://crd.gov.pl/wzor/2023/06/29/12648/";

    public InvoiceXmlGenerator(ILogger<InvoiceXmlGenerator> logger)
    {
        _logger = logger;
    }

    public string GenerateInvoiceXml(CreateInvoiceRequest invoice, string? sessionNip = null)
    {
        _logger.LogInformation("Generowanie XML faktury: {Number}", invoice.InvoiceNumber);

        var effectiveSellerNip = !string.IsNullOrEmpty(sessionNip) ? sessionNip : invoice.Seller.Nip;
        
        var now = DateTime.UtcNow;
        var dateTime = now.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ");

        var totals = CalculateTotals(invoice.Items);

        var sb = new StringBuilder();

        sb.Append(@"<?xml version=""1.0"" encoding=""UTF-8""?>");
        sb.Append($@"<Faktura xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" ");
        sb.Append($@"xmlns:xsd=""http://www.w3.org/2001/XMLSchema"" ");
        sb.Append($@"xmlns=""{Namespace}"">");

        AppendNaglowek(sb, dateTime);
        AppendPodmiot1(sb, invoice.Seller, effectiveSellerNip);
        AppendPodmiot2(sb, invoice.Buyer);
        AppendFa(sb, invoice, totals);

        sb.Append("</Faktura>");

        var xml = sb.ToString();
        _logger.LogDebug("Wygenerowany XML faktury ({Size} bajtów)", xml.Length);

        return xml;
    }

    private void AppendNaglowek(StringBuilder sb, string dateTime)
    {
        sb.Append("<Naglowek>");
        sb.Append(@"<KodFormularza kodSystemowy=""FA (2)"" wersjaSchemy=""1-0E"">FA</KodFormularza>");
        sb.Append("<WariantFormularza>2</WariantFormularza>");
        sb.Append($"<DataWytworzeniaFa>{dateTime}</DataWytworzeniaFa>");
        sb.Append("<SystemInfo>KSeF Master</SystemInfo>");
        sb.Append("</Naglowek>");
    }

    private void AppendPodmiot1(StringBuilder sb, PartyData seller, string effectiveNip)
    {
        sb.Append("<Podmiot1>");
        
        sb.Append("<DaneIdentyfikacyjne>");
        sb.Append($"<NIP>{EscapeXml(effectiveNip)}</NIP>");
        sb.Append($"<Nazwa>{EscapeXml(seller.Name)}</Nazwa>");
        sb.Append("</DaneIdentyfikacyjne>");
        
        sb.Append("<Adres>");
        sb.Append($"<KodKraju>{EscapeXml(seller.CountryCode)}</KodKraju>");
        sb.Append($"<AdresL1>{EscapeXml(seller.AddressLine1)}</AdresL1>");
        if (!string.IsNullOrEmpty(seller.AddressLine2))
        {
            sb.Append($"<AdresL2>{EscapeXml(seller.AddressLine2)}</AdresL2>");
        }
        sb.Append("</Adres>");
        
        sb.Append("</Podmiot1>");
    }

    private void AppendPodmiot2(StringBuilder sb, PartyData buyer)
    {
        sb.Append("<Podmiot2>");
        
        sb.Append("<DaneIdentyfikacyjne>");
        sb.Append($"<NIP>{EscapeXml(buyer.Nip)}</NIP>");
        sb.Append($"<Nazwa>{EscapeXml(buyer.Name)}</Nazwa>");
        sb.Append("</DaneIdentyfikacyjne>");
        
        sb.Append("<Adres>");
        sb.Append($"<KodKraju>{EscapeXml(buyer.CountryCode)}</KodKraju>");
        sb.Append($"<AdresL1>{EscapeXml(buyer.AddressLine1)}</AdresL1>");
        if (!string.IsNullOrEmpty(buyer.AddressLine2))
        {
            sb.Append($"<AdresL2>{EscapeXml(buyer.AddressLine2)}</AdresL2>");
        }
        sb.Append("</Adres>");
        
        sb.Append("</Podmiot2>");
    }

    private void AppendFa(StringBuilder sb, CreateInvoiceRequest invoice, InvoiceTotals totals)
    {
        sb.Append("<Fa>");

        sb.Append($"<KodWaluty>{EscapeXml(invoice.Currency)}</KodWaluty>");
        sb.Append($"<P_1>{EscapeXml(invoice.IssueDate)}</P_1>");
        sb.Append($"<P_2>{EscapeXml(invoice.InvoiceNumber)}</P_2>");
        sb.Append($"<P_6>{EscapeXml(invoice.SaleDate)}</P_6>");

        AppendVatSummary(sb, totals);

        sb.Append($"<P_15>{FormatDecimal(totals.GrossTotal)}</P_15>");

        AppendAdnotacje(sb);

        sb.Append("<RodzajFaktury>VAT</RodzajFaktury>");

        AppendFaWiersze(sb, invoice.Items);

        AppendPlatnosc(sb, invoice.Payment);

        sb.Append("</Fa>");
    }

    private void AppendVatSummary(StringBuilder sb, InvoiceTotals totals)
    {
        if (totals.ByVatRate.TryGetValue("23", out var vat23) && vat23.Net > 0)
        {
            sb.Append($"<P_13_1>{FormatDecimal(vat23.Net)}</P_13_1>");
            sb.Append($"<P_14_1>{FormatDecimal(vat23.Vat)}</P_14_1>");
        }
        else if (totals.ByVatRate.TryGetValue("22", out var vat22) && vat22.Net > 0)
        {
            sb.Append($"<P_13_1>{FormatDecimal(vat22.Net)}</P_13_1>");
            sb.Append($"<P_14_1>{FormatDecimal(vat22.Vat)}</P_14_1>");
        }

        if (totals.ByVatRate.TryGetValue("8", out var vat8) && vat8.Net > 0)
        {
            sb.Append($"<P_13_2>{FormatDecimal(vat8.Net)}</P_13_2>");
            sb.Append($"<P_14_2>{FormatDecimal(vat8.Vat)}</P_14_2>");
        }
        else if (totals.ByVatRate.TryGetValue("7", out var vat7) && vat7.Net > 0)
        {
            sb.Append($"<P_13_2>{FormatDecimal(vat7.Net)}</P_13_2>");
            sb.Append($"<P_14_2>{FormatDecimal(vat7.Vat)}</P_14_2>");
        }

        if (totals.ByVatRate.TryGetValue("5", out var vat5) && vat5.Net > 0)
        {
            sb.Append($"<P_13_3>{FormatDecimal(vat5.Net)}</P_13_3>");
            sb.Append($"<P_14_3>{FormatDecimal(vat5.Vat)}</P_14_3>");
        }

        if (totals.ByVatRate.TryGetValue("0", out var vat0) && vat0.Net > 0)
        {
            sb.Append($"<P_13_6_1>{FormatDecimal(vat0.Net)}</P_13_6_1>");
        }

        if (totals.ByVatRate.TryGetValue("zw", out var vatZw) && vatZw.Net > 0)
        {
            sb.Append($"<P_13_7>{FormatDecimal(vatZw.Net)}</P_13_7>");
        }

        if (totals.ByVatRate.TryGetValue("np", out var vatNp) && vatNp.Net > 0)
        {
            sb.Append($"<P_13_8>{FormatDecimal(vatNp.Net)}</P_13_8>");
        }

        if (totals.ByVatRate.TryGetValue("oo", out var vatOo) && vatOo.Net > 0)
        {
            sb.Append($"<P_13_10>{FormatDecimal(vatOo.Net)}</P_13_10>");
        }
    }

    private void AppendAdnotacje(StringBuilder sb)
    {
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
    }

    private void AppendFaWiersze(StringBuilder sb, List<InvoiceItem> items)
    {
        int lineNumber = 1;
        foreach (var item in items)
        {
            var netValue = Math.Round(item.Quantity * item.UnitPriceNet, 2);

            sb.Append("<FaWiersz>");
            sb.Append($"<NrWierszaFa>{lineNumber++}</NrWierszaFa>");
            sb.Append($"<P_7>{EscapeXml(item.Name)}</P_7>");
            sb.Append($"<P_8A>{EscapeXml(item.Unit)}</P_8A>");
            sb.Append($"<P_8B>{FormatQuantity(item.Quantity)}</P_8B>");
            sb.Append($"<P_9A>{FormatDecimal(item.UnitPriceNet)}</P_9A>");
            sb.Append($"<P_11>{FormatDecimal(netValue)}</P_11>");
            sb.Append($"<P_12>{NormalizeVatRate(item.VatRate)}</P_12>");
            sb.Append("</FaWiersz>");
        }
    }

    private void AppendPlatnosc(StringBuilder sb, PaymentData? payment)
    {
        if (payment == null || string.IsNullOrEmpty(payment.Method))
            return;

        sb.Append("<Platnosc>");

        if (!string.IsNullOrEmpty(payment.DueDate))
        {
            sb.Append("<TerminPlatnosci>");
            sb.Append($"<Termin>{EscapeXml(payment.DueDate)}</Termin>");
            sb.Append("</TerminPlatnosci>");
        }

        var formaPlatnosci = MapPaymentMethod(payment.Method);
        sb.Append($"<FormaPlatnosci>{formaPlatnosci}</FormaPlatnosci>");

        if (!string.IsNullOrEmpty(payment.BankAccount))
        {
            var cleanAccount = CleanBankAccount(payment.BankAccount);
            if (cleanAccount.Length >= 10 && cleanAccount.Length <= 32)
            {
                sb.Append("<RachunekBankowy>");
                sb.Append($"<NrRB>{cleanAccount}</NrRB>");
                sb.Append("</RachunekBankowy>");
            }
        }

        sb.Append("</Platnosc>");
    }

    private string MapPaymentMethod(string method)
    {
        return method.ToLowerInvariant() switch
        {
            "gotówka" or "gotowka" or "1" => "1",
            "karta" or "2" => "2",
            "bon" or "3" => "3",
            "czek" or "4" => "4",
            "kredyt" or "5" => "5",
            "przelew" or "6" => "6",
            "mobilna" or "7" => "7",
            _ => "6"
        };
    }

    private string CleanBankAccount(string account)
    {
        var sb = new StringBuilder();
        foreach (char c in account)
        {
            if (char.IsLetterOrDigit(c))
                sb.Append(char.ToUpperInvariant(c));
        }
        return sb.ToString();
    }

    private InvoiceTotals CalculateTotals(List<InvoiceItem> items)
    {
        var totals = new InvoiceTotals();

        foreach (var item in items)
        {
            var netValue = Math.Round(item.Quantity * item.UnitPriceNet, 2);
            var vatPercent = GetVatPercent(item.VatRate);
            var vatValue = Math.Round(netValue * vatPercent / 100m, 2);

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

    private decimal GetVatPercent(string vatRate)
    {
        var normalized = vatRate.Trim().ToLowerInvariant();
        return normalized switch
        {
            "23" => 23m,
            "22" => 22m,
            "8" => 8m,
            "7" => 7m,
            "5" => 5m,
            "4" => 4m,
            "3" => 3m,
            "0" => 0m,
            "zw" or "np" or "oo" => 0m,
            _ => decimal.TryParse(vatRate, out var rate) ? rate : 23m
        };
    }

    private string NormalizeVatRateKey(string vatRate)
    {
        var normalized = vatRate.Trim().ToLowerInvariant();
        if (int.TryParse(normalized, out var numRate))
            return numRate.ToString();
        return normalized;
    }

    private string NormalizeVatRate(string vatRate)
    {
        var normalized = vatRate.Trim().ToLowerInvariant();
        return normalized switch
        {
            "23" or "22" or "8" or "7" or "5" or "4" or "3" or "0" => normalized,
            "zw" or "np" or "oo" => normalized,
            _ => int.TryParse(vatRate, out var rate) ? rate.ToString() : "23"
        };
    }

    private string FormatDecimal(decimal value)
    {
        value = Math.Round(value, 2);
        return value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
    }

    private string FormatQuantity(decimal value)
    {
        if (value == Math.Floor(value))
            return ((long)value).ToString();
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

    private class InvoiceTotals
    {
        public decimal NetTotal { get; set; }
        public decimal VatTotal { get; set; }
        public decimal GrossTotal { get; set; }
        public Dictionary<string, (decimal Net, decimal Vat)> ByVatRate { get; } = new();
    }
}