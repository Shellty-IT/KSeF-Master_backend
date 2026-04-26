using System.Globalization;
using System.Text;
using KSeF.Backend.Models.Requests;

namespace KSeF.Backend.Services.Invoice;

public class InvoiceXmlGenerator
{
    private readonly ILogger<InvoiceXmlGenerator> _logger;

    private const string Namespace = "http://crd.gov.pl/wzor/2025/06/25/13775/";

    public InvoiceXmlGenerator(ILogger<InvoiceXmlGenerator> logger)
    {
        _logger = logger;
    }

    public string Generate(CreateInvoiceRequest invoice)
    {
        return GenerateInvoiceXml(invoice);
    }

    public string GenerateInvoiceXml(CreateInvoiceRequest invoice, string? sessionNip = null)
    {
        _logger.LogInformation("Generowanie XML faktury: {Number}", invoice.InvoiceNumber);

        if (!string.IsNullOrEmpty(sessionNip) && invoice.Seller.Nip != sessionNip)
        {
            _logger.LogWarning(
                "NIP sprzedawcy ({SellerNip}) różni się od NIP sesji ({SessionNip})",
                invoice.Seller.Nip, sessionNip);
        }

        var now = DateTime.UtcNow;
        var dateTime = now.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ");
        var currency = string.IsNullOrWhiteSpace(invoice.Currency) ? "PLN" : invoice.Currency;
        var totals = CalculateTotals(invoice.Items);
        var isCorrection = invoice.CorrectionData != null;

        var xml = new StringBuilder();
        xml.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        xml.AppendLine($"<Faktura xmlns=\"{Namespace}\">");

        AppendNaglowek(xml, dateTime);
        AppendPodmiot1(xml, invoice.Seller);
        AppendPodmiot2(xml, invoice.Buyer);
        AppendFa(xml, invoice, totals, currency, isCorrection);

        xml.AppendLine("</Faktura>");

        return xml.ToString();
    }

    private static void AppendNaglowek(StringBuilder xml, string dateTime)
    {
        xml.AppendLine("  <Naglowek>");
        xml.AppendLine("    <KodFormularza kodSystemowy=\"FA (3)\" wersjaSchemy=\"1-0E\">FA</KodFormularza>");
        xml.AppendLine("    <WariantFormularza>3</WariantFormularza>");
        xml.AppendLine($"    <DataWytworzeniaFa>{dateTime}</DataWytworzeniaFa>");
        xml.AppendLine("    <SystemInfo>KSeF Master v1.0</SystemInfo>");
        xml.AppendLine("  </Naglowek>");
    }

    private static void AppendPodmiot1(StringBuilder xml, PartyData seller)
    {
        xml.AppendLine("  <Podmiot1>");
        xml.AppendLine("    <DaneIdentyfikacyjne>");
        xml.AppendLine($"      <NIP>{seller.Nip}</NIP>");
        xml.AppendLine($"      <Nazwa>{EscapeXml(seller.Name)}</Nazwa>");
        xml.AppendLine("    </DaneIdentyfikacyjne>");
        xml.AppendLine("    <Adres>");
        xml.AppendLine($"      <KodKraju>{seller.CountryCode}</KodKraju>");
        xml.AppendLine($"      <AdresL1>{EscapeXml(seller.AddressLine1)}</AdresL1>");
        if (!string.IsNullOrWhiteSpace(seller.AddressLine2))
            xml.AppendLine($"      <AdresL2>{EscapeXml(seller.AddressLine2)}</AdresL2>");
        xml.AppendLine("    </Adres>");
        xml.AppendLine("  </Podmiot1>");
    }

    private static void AppendPodmiot2(StringBuilder xml, PartyData buyer)
    {
        xml.AppendLine("  <Podmiot2>");
        xml.AppendLine("    <DaneIdentyfikacyjne>");
        xml.AppendLine($"      <NIP>{buyer.Nip}</NIP>");
        xml.AppendLine($"      <Nazwa>{EscapeXml(buyer.Name)}</Nazwa>");
        xml.AppendLine("    </DaneIdentyfikacyjne>");
        if (!string.IsNullOrWhiteSpace(buyer.AddressLine1))
        {
            xml.AppendLine("    <Adres>");
            xml.AppendLine($"      <KodKraju>{buyer.CountryCode}</KodKraju>");
            xml.AppendLine($"      <AdresL1>{EscapeXml(buyer.AddressLine1)}</AdresL1>");
            if (!string.IsNullOrWhiteSpace(buyer.AddressLine2))
                xml.AppendLine($"      <AdresL2>{EscapeXml(buyer.AddressLine2)}</AdresL2>");
            xml.AppendLine("    </Adres>");
        }
        // FA(3) — JST i GV są wymagane w Podmiot2
        xml.AppendLine("    <JST>2</JST>");
        xml.AppendLine("    <GV>2</GV>");
        xml.AppendLine("  </Podmiot2>");
    }

    private static void AppendFa(
        StringBuilder xml,
        CreateInvoiceRequest invoice,
        (decimal Net, decimal Vat, decimal Gross) totals,
        string currency,
        bool isCorrection)
    {
        xml.AppendLine("  <Fa>");
        xml.AppendLine($"    <KodWaluty>{currency}</KodWaluty>");
        xml.AppendLine($"    <P_1>{invoice.IssueDate}</P_1>");

        if (!string.IsNullOrWhiteSpace(invoice.IssuePlace))
            xml.AppendLine($"    <P_1M>{EscapeXml(invoice.IssuePlace)}</P_1M>");

        xml.AppendLine($"    <P_2>{EscapeXml(invoice.InvoiceNumber)}</P_2>");

        if (!string.IsNullOrWhiteSpace(invoice.SaleDate))
            xml.AppendLine($"    <P_6>{invoice.SaleDate}</P_6>");

        AppendVatTotals(xml, invoice.Items);

        xml.AppendLine($"    <P_15>{FormatDecimal(totals.Gross)}</P_15>");

        AppendAdnotacje(xml);

        xml.AppendLine($"    <RodzajFaktury>{(isCorrection ? "KOR" : "VAT")}</RodzajFaktury>");

        if (isCorrection && invoice.CorrectionData != null)
            AppendDaneFaKorygowanej(xml, invoice.CorrectionData);

        AppendFaWiersze(xml, invoice.Items);

        if (invoice.Payment != null)
            AppendPayment(xml, invoice.Payment);

        xml.AppendLine("  </Fa>");
    }

    private static void AppendVatTotals(StringBuilder xml, List<InvoiceItem> items)
    {
        var groups = items.GroupBy(i => NormalizeVatRateKey(i.VatRate)).ToDictionary(g => g.Key, g => g.ToList());

        if (groups.TryGetValue("23", out var g23))
        {
            var net = SumNet(g23);
            var vat = net * 0.23m;
            xml.AppendLine($"    <P_13_1>{FormatDecimal(net)}</P_13_1>");
            xml.AppendLine($"    <P_14_1>{FormatDecimal(vat)}</P_14_1>");
        }

        if (groups.TryGetValue("22", out var g22))
        {
            var net = SumNet(g22);
            var vat = net * 0.22m;
            xml.AppendLine($"    <P_13_1>{FormatDecimal(net)}</P_13_1>");
            xml.AppendLine($"    <P_14_1>{FormatDecimal(vat)}</P_14_1>");
        }

        if (groups.TryGetValue("8", out var g8))
        {
            var net = SumNet(g8);
            var vat = net * 0.08m;
            xml.AppendLine($"    <P_13_2>{FormatDecimal(net)}</P_13_2>");
            xml.AppendLine($"    <P_14_2>{FormatDecimal(vat)}</P_14_2>");
        }

        if (groups.TryGetValue("7", out var g7))
        {
            var net = SumNet(g7);
            var vat = net * 0.07m;
            xml.AppendLine($"    <P_13_2>{FormatDecimal(net)}</P_13_2>");
            xml.AppendLine($"    <P_14_2>{FormatDecimal(vat)}</P_14_2>");
        }

        if (groups.TryGetValue("5", out var g5))
        {
            var net = SumNet(g5);
            var vat = net * 0.05m;
            xml.AppendLine($"    <P_13_3>{FormatDecimal(net)}</P_13_3>");
            xml.AppendLine($"    <P_14_3>{FormatDecimal(vat)}</P_14_3>");
        }

        if (groups.TryGetValue("0", out var g0))
        {
            var net = SumNet(g0);
            xml.AppendLine($"    <P_13_6_1>{FormatDecimal(net)}</P_13_6_1>");
        }

        if (groups.TryGetValue("zw", out var gZw))
        {
            var net = SumNet(gZw);
            xml.AppendLine($"    <P_13_7>{FormatDecimal(net)}</P_13_7>");
        }

        if (groups.TryGetValue("np", out var gNp))
        {
            var net = SumNet(gNp);
            xml.AppendLine($"    <P_13_8>{FormatDecimal(net)}</P_13_8>");
        }
    }

    private static void AppendAdnotacje(StringBuilder xml)
    {
        xml.AppendLine("    <Adnotacje>");
        xml.AppendLine("      <P_16>2</P_16>");
        xml.AppendLine("      <P_17>2</P_17>");
        xml.AppendLine("      <P_18>2</P_18>");
        xml.AppendLine("      <P_18A>2</P_18A>");
        xml.AppendLine("      <Zwolnienie>");
        xml.AppendLine("        <P_19N>1</P_19N>");
        xml.AppendLine("      </Zwolnienie>");
        xml.AppendLine("      <NoweSrodkiTransportu>");
        xml.AppendLine("        <P_22N>1</P_22N>");
        xml.AppendLine("      </NoweSrodkiTransportu>");
        xml.AppendLine("      <P_23>2</P_23>");
        xml.AppendLine("      <PMarzy>");
        xml.AppendLine("        <P_PMarzyN>1</P_PMarzyN>");
        xml.AppendLine("      </PMarzy>");
        xml.AppendLine("    </Adnotacje>");
    }

    private static void AppendDaneFaKorygowanej(StringBuilder xml, CorrectionData correction)
    {
        if (!string.IsNullOrWhiteSpace(correction.Reason))
            xml.AppendLine($"    <PrzyczynaKorekty>{EscapeXml(correction.Reason)}</PrzyczynaKorekty>");

        if (correction.CorrectionType.HasValue)
            xml.AppendLine($"    <TypKorekty>{correction.CorrectionType.Value}</TypKorekty>");

        xml.AppendLine("    <DaneFaKorygowanej>");
        xml.AppendLine($"      <DataWystFaKorygowanej>{correction.OriginalIssueDate}</DataWystFaKorygowanej>");
        xml.AppendLine($"      <NrFaKorygowanej>{EscapeXml(correction.OriginalInvoiceNumber)}</NrFaKorygowanej>");

        if (!string.IsNullOrWhiteSpace(correction.OriginalKsefNumber))
        {
            xml.AppendLine("      <NrKSeF>1</NrKSeF>");
            xml.AppendLine($"      <NrKSeFFaKorygowanej>{correction.OriginalKsefNumber}</NrKSeFFaKorygowanej>");
        }
        else
        {
            xml.AppendLine("      <NrKSeFN>1</NrKSeFN>");
        }

        xml.AppendLine("    </DaneFaKorygowanej>");
    }

    private static void AppendFaWiersze(StringBuilder xml, List<InvoiceItem> items)
    {
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var net = item.Quantity * item.UnitPriceNet;

            xml.AppendLine("    <FaWiersz>");
            xml.AppendLine($"      <NrWierszaFa>{i + 1}</NrWierszaFa>");
            xml.AppendLine($"      <P_7>{EscapeXml(item.Name)}</P_7>");
            xml.AppendLine($"      <P_8A>{EscapeXml(item.Unit)}</P_8A>");
            xml.AppendLine($"      <P_8B>{FormatQuantity(item.Quantity)}</P_8B>");
            xml.AppendLine($"      <P_9A>{FormatDecimal8(item.UnitPriceNet)}</P_9A>");
            xml.AppendLine($"      <P_11>{FormatDecimal(net)}</P_11>");
            xml.AppendLine($"      <P_12>{MapVatRate(item.VatRate)}</P_12>");
            xml.AppendLine("    </FaWiersz>");
        }
    }

    private static void AppendPayment(StringBuilder xml, PaymentData payment)
    {
        xml.AppendLine("    <Platnosc>");

        if (payment.Paid == true)
        {
            xml.AppendLine("      <Zaplacono>1</Zaplacono>");
            if (!string.IsNullOrWhiteSpace(payment.PaidDate))
                xml.AppendLine($"      <DataZaplaty>{payment.PaidDate}</DataZaplaty>");
        }

        if (!string.IsNullOrWhiteSpace(payment.DueDate))
        {
            xml.AppendLine("      <TerminPlatnosci>");
            xml.AppendLine($"        <Termin>{payment.DueDate}</Termin>");
            xml.AppendLine("      </TerminPlatnosci>");
        }

        var formaPlatnosci = payment.Method?.ToLowerInvariant() switch
        {
            "gotówka" or "gotowka" or "cash" => "1",
            "karta" or "card" => "2",
            "przelew" or "transfer" => "6",
            _ => null
        };

        if (formaPlatnosci != null)
            xml.AppendLine($"      <FormaPlatnosci>{formaPlatnosci}</FormaPlatnosci>");

        if (!string.IsNullOrWhiteSpace(payment.BankAccount))
        {
            xml.AppendLine("      <RachunekBankowy>");
            xml.AppendLine($"        <NrRB>{payment.BankAccount}</NrRB>");
            xml.AppendLine("      </RachunekBankowy>");
        }

        xml.AppendLine("    </Platnosc>");
    }

    private static (decimal Net, decimal Vat, decimal Gross) CalculateTotals(List<InvoiceItem> items)
    {
        var net = 0m;
        var vat = 0m;

        foreach (var item in items)
        {
            var itemNet = item.Quantity * item.UnitPriceNet;
            var itemVat = itemNet * ParseVatRateDecimal(item.VatRate);
            net += itemNet;
            vat += itemVat;
        }

        return (net, vat, net + vat);
    }

    private static decimal ParseVatRateDecimal(string vatRate)
    {
        if (decimal.TryParse(vatRate, NumberStyles.Any, CultureInfo.InvariantCulture, out var rate))
            return rate / 100m;
        return 0m;
    }

    private static decimal SumNet(List<InvoiceItem> items)
        => items.Sum(i => i.Quantity * i.UnitPriceNet);

    private static string NormalizeVatRateKey(string vatRate)
        => vatRate.Trim().ToLowerInvariant() switch
        {
            "zw" => "zw",
            "oo" => "oo",
            "np" or "np i" or "np ii" => "np",
            _ => vatRate.Trim()
        };

    private static string MapVatRate(string vatRate) => vatRate.Trim() switch
    {
        "23" => "23",
        "22" => "22",
        "8" => "8",
        "7" => "7",
        "5" => "5",
        "4" => "4",
        "3" => "3",
        "0" => "0",
        "zw" => "zw",
        "oo" => "oo",
        "np" => "np I",
        _ => vatRate.Trim()
    };

    private static string FormatDecimal(decimal value)
        => value.ToString("F2", CultureInfo.InvariantCulture);

    private static string FormatDecimal8(decimal value)
    {
        var s = value.ToString("F8", CultureInfo.InvariantCulture).TrimEnd('0');
        if (s.EndsWith('.')) s += "00";
        return s;
    }

    private static string FormatQuantity(decimal value)
    {
        var s = value.ToString("F6", CultureInfo.InvariantCulture).TrimEnd('0');
        if (s.EndsWith('.')) s += "0";
        return s;
    }

    private static string EscapeXml(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");
    }
}