using KSeF.Backend.Models.Requests;
using KSeF.Backend.Services.Interfaces;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using QRCoder;

namespace KSeF.Backend.Services;

public class PdfGeneratorService : IPdfGeneratorService
{
    private readonly ILogger<PdfGeneratorService> _logger;

    private const string KSEF_TEST_URL = "https://ksef-test.mf.gov.pl";
    private const string APP_URL = "https://ksef-master.netlify.app/";

    public PdfGeneratorService(ILogger<PdfGeneratorService> logger)
    {
        _logger = logger;
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] GeneratePdf(GeneratePdfRequest request)
    {
        _logger.LogInformation("Generowanie PDF dla faktury: {Number}", request.InvoiceNumber);

        var verificationUrl = BuildVerificationUrl(
            request.Seller?.Nip ?? "",
            request.IssueDate ?? "",
            request.InvoiceHash ?? ""
        );

        _logger.LogInformation("Wygenerowany URL weryfikacyjny: {Url}", verificationUrl);

        var document = CreateInvoiceDocument(request, verificationUrl);
        return document.GeneratePdf();
    }

    public Task<byte[]> GeneratePdfFromKsefAsync(string ksefNumber, CancellationToken ct = default)
    {
        throw new NotSupportedException("Pobieranie szczegółów z KSeF nie jest obsługiwane.");
    }

    private string BuildVerificationUrl(string sellerNip, string issueDate, string invoiceHash)
    {
        if (string.IsNullOrEmpty(sellerNip) || string.IsNullOrEmpty(issueDate) || string.IsNullOrEmpty(invoiceHash))
        {
            return "";
        }

        var dateParts = issueDate.Split('-');
        var formattedDate = dateParts.Length == 3
            ? $"{dateParts[2]}-{dateParts[1]}-{dateParts[0]}"
            : issueDate;

        var hashBase64Url = ConvertToBase64Url(invoiceHash);
        return $"{KSEF_TEST_URL}/client-app/invoice/{sellerNip}/{formattedDate}/{hashBase64Url}";
    }

    private string ConvertToBase64Url(string base64)
    {
        if (string.IsNullOrEmpty(base64)) return "";
        return base64.Replace("+", "-").Replace("/", "_").TrimEnd('=');
    }

    private byte[] GenerateQrCode(string url)
    {
        if (string.IsNullOrEmpty(url)) return Array.Empty<byte>();

        using var qrGenerator = new QRCodeGenerator();
        using var qrCodeData = qrGenerator.CreateQrCode(url, QRCodeGenerator.ECCLevel.M);
        using var qrCode = new PngByteQRCode(qrCodeData);
        return qrCode.GetGraphic(8);
    }

    private Document CreateInvoiceDocument(GeneratePdfRequest request, string verificationUrl)
    {
        var qrCodeBytes = GenerateQrCode(verificationUrl);
        var hasQrCode = qrCodeBytes.Length > 0 && !string.IsNullOrEmpty(verificationUrl);

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.MarginTop(1.5f, Unit.Centimetre);
                page.MarginBottom(1.5f, Unit.Centimetre);
                page.MarginLeft(2, Unit.Centimetre);
                page.MarginRight(2, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(9));

                page.Header().Element(c => ComposeHeader(c, request));
                page.Content().Element(c => ComposeContent(c, request, qrCodeBytes, verificationUrl, hasQrCode));
                page.Footer().Element(ComposeFooter);
            });
        });
    }

    private void ComposeHeader(IContainer container, GeneratePdfRequest request)
    {
        container.Row(row =>
        {
            row.RelativeItem().Column(col =>
            {
                col.Item().Text("Krajowy System e-Faktur").Bold().FontSize(11);
                col.Item().Text("KSeF").FontColor(Colors.Red.Medium).Bold().FontSize(14);
            });

            row.RelativeItem().AlignRight().Column(col =>
            {
                col.Item().Text("Numer faktury").FontSize(8).FontColor(Colors.Grey.Medium);
                col.Item().Text(request.InvoiceNumber ?? "-").Bold().FontSize(14);
                col.Item().Text("Faktura VAT").FontSize(8);
                if (!string.IsNullOrEmpty(request.KsefNumber))
                {
                    col.Item().Text($"Numer KSeF: {request.KsefNumber}").FontSize(7).FontColor(Colors.Grey.Darken1);
                }
            });
        });
    }

    private void ComposeContent(IContainer container, GeneratePdfRequest request, byte[] qrCodeBytes, string verificationUrl, bool hasQrCode)
    {
        container.Column(col =>
        {
            col.Spacing(10);

            col.Item().LineHorizontal(1).LineColor(Colors.Grey.Lighten2);

            col.Item().Row(row =>
            {
                row.RelativeItem().Element(c => ComposePartySection(c, "Sprzedawca", request.Seller));
                row.ConstantItem(20);
                row.RelativeItem().Element(c => ComposePartySection(c, "Nabywca", request.Buyer));
            });

            col.Item().LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
            col.Item().Element(c => ComposeDetails(c, request));
            col.Item().LineHorizontal(1).LineColor(Colors.Grey.Lighten2);

            if (request.Items != null && request.Items.Count > 0)
            {
                col.Item().Element(c => ComposeItemsTable(c, request));
            }

            col.Item().AlignRight().Text($"Kwota należności ogółem: {FormatMoney(request.Totals?.Gross ?? 0)} PLN")
                .Bold().FontSize(12);

            col.Item().LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
            col.Item().Element(c => ComposeVatSummary(c, request));

            if (request.Payment != null)
            {
                col.Item().LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
                col.Item().Element(c => ComposePaymentSection(c, request));
            }

            if (hasQrCode)
            {
                col.Item().LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
                col.Item().Element(c => ComposeVerificationSection(c, qrCodeBytes, verificationUrl, request.KsefNumber));
            }
        });
    }

    private void ComposePartySection(IContainer container, string title, PdfPartyData? party)
    {
        container.Column(col =>
        {
            col.Item().Text(title).Bold().FontSize(10);
            col.Item().PaddingTop(5);

            if (party != null)
            {
                if (!string.IsNullOrEmpty(party.Nip))
                    col.Item().Text($"NIP: {party.Nip}").FontSize(9);
                if (!string.IsNullOrEmpty(party.Name))
                    col.Item().Text($"Nazwa: {party.Name}").FontSize(9);
                
                if (!string.IsNullOrEmpty(party.Address))
                {
                    col.Item().PaddingTop(3);
                    col.Item().Text("Adres").Bold().FontSize(8);
                    col.Item().Text(party.Address).FontSize(9);
                    col.Item().Text("Polska").FontSize(9);
                }

                if (!string.IsNullOrEmpty(party.BankAccount))
                {
                    col.Item().PaddingTop(3);
                    col.Item().Text($"Rachunek: {party.BankAccount}").FontSize(8);
                }
            }
        });
    }

    private void ComposeDetails(IContainer container, GeneratePdfRequest request)
    {
        container.Column(col =>
        {
            col.Item().Text("Szczegóły").Bold().FontSize(10);
            col.Item().PaddingTop(5);

            col.Item().Row(row =>
            {
                row.RelativeItem().Text(t =>
                {
                    t.Span("Data wystawienia: ").FontSize(8);
                    t.Span(request.IssueDate ?? "-").FontSize(9);
                });

                if (!string.IsNullOrEmpty(request.IssuePlace))
                {
                    row.RelativeItem().Text(t =>
                    {
                        t.Span("Miejsce wystawienia: ").FontSize(8);
                        t.Span(request.IssuePlace).FontSize(9);
                    });
                }
            });

            if (!string.IsNullOrEmpty(request.SaleDate))
            {
                col.Item().Text(t =>
                {
                    t.Span("Data sprzedaży: ").FontSize(8);
                    t.Span(request.SaleDate).FontSize(9);
                });
            }
        });
    }

    private void ComposeItemsTable(IContainer container, GeneratePdfRequest request)
    {
        container.Column(col =>
        {
            col.Item().Text("Pozycje").Bold().FontSize(10);
            col.Item().Text("Faktura wystawiona w cenach netto w walucie PLN").FontSize(8).FontColor(Colors.Grey.Medium);
            col.Item().PaddingTop(5);

            col.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(25);
                    columns.RelativeColumn(3);
                    columns.ConstantColumn(55);
                    columns.ConstantColumn(40);
                    columns.ConstantColumn(35);
                    columns.ConstantColumn(40);
                    columns.ConstantColumn(60);
                });

                table.Header(header =>
                {
                    header.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("Lp.").Bold().FontSize(8);
                    header.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("Nazwa towaru lub usługi").Bold().FontSize(8);
                    header.Cell().Background(Colors.Grey.Lighten3).Padding(4).AlignRight().Text("Cena netto").Bold().FontSize(8);
                    header.Cell().Background(Colors.Grey.Lighten3).Padding(4).AlignRight().Text("Ilość").Bold().FontSize(8);
                    header.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("J.m.").Bold().FontSize(8);
                    header.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("Stawka").Bold().FontSize(8);
                    header.Cell().Background(Colors.Grey.Lighten3).Padding(4).AlignRight().Text("Wartość netto").Bold().FontSize(8);
                });

                if (request.Items != null)
                {
                    for (int i = 0; i < request.Items.Count; i++)
                    {
                        var item = request.Items[i];
                        var bgColor = i % 2 == 0 ? Colors.White : Colors.Grey.Lighten4;

                        table.Cell().Background(bgColor).Padding(4).Text((i + 1).ToString()).FontSize(8);
                        table.Cell().Background(bgColor).Padding(4).Text(item.Name).FontSize(8);
                        table.Cell().Background(bgColor).Padding(4).AlignRight().Text(FormatMoney(item.UnitPriceNet)).FontSize(8);
                        table.Cell().Background(bgColor).Padding(4).AlignRight().Text(item.Quantity.ToString("0.##")).FontSize(8);
                        table.Cell().Background(bgColor).Padding(4).Text(item.Unit).FontSize(8);
                        table.Cell().Background(bgColor).Padding(4).Text(FormatVatRate(item.VatRate)).FontSize(8);
                        table.Cell().Background(bgColor).Padding(4).AlignRight().Text(FormatMoney(item.NetValue)).FontSize(8);
                    }
                }
            });
        });
    }

    private void ComposeVatSummary(IContainer container, GeneratePdfRequest request)
    {
        container.Column(col =>
        {
            col.Item().Text("Podsumowanie stawek podatku").Bold().FontSize(10);
            col.Item().PaddingTop(5);

            col.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(25);
                    columns.RelativeColumn();
                    columns.ConstantColumn(80);
                    columns.ConstantColumn(80);
                    columns.ConstantColumn(80);
                });

                table.Header(header =>
                {
                    header.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("Lp.").Bold().FontSize(8);
                    header.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("Stawka podatku").Bold().FontSize(8);
                    header.Cell().Background(Colors.Grey.Lighten3).Padding(4).AlignRight().Text("Kwota netto").Bold().FontSize(8);
                    header.Cell().Background(Colors.Grey.Lighten3).Padding(4).AlignRight().Text("Kwota podatku").Bold().FontSize(8);
                    header.Cell().Background(Colors.Grey.Lighten3).Padding(4).AlignRight().Text("Kwota brutto").Bold().FontSize(8);
                });

                if (request.Totals?.PerRate != null && request.Totals.PerRate.Count > 0)
                {
                    int i = 1;
                    foreach (var (rate, summary) in request.Totals.PerRate)
                    {
                        table.Cell().Padding(4).Text(i.ToString()).FontSize(8);
                        table.Cell().Padding(4).Text(rate).FontSize(8);
                        table.Cell().Padding(4).AlignRight().Text(FormatMoney(summary.Net)).FontSize(8);
                        table.Cell().Padding(4).AlignRight().Text(FormatMoney(summary.Vat)).FontSize(8);
                        table.Cell().Padding(4).AlignRight().Text(FormatMoney(summary.Gross)).FontSize(8);
                        i++;
                    }
                }
                else
                {
                    table.Cell().Padding(4).Text("1").FontSize(8);
                    table.Cell().Padding(4).Text("23%").FontSize(8);
                    table.Cell().Padding(4).AlignRight().Text(FormatMoney(request.Totals?.Net ?? 0)).FontSize(8);
                    table.Cell().Padding(4).AlignRight().Text(FormatMoney(request.Totals?.Vat ?? 0)).FontSize(8);
                    table.Cell().Padding(4).AlignRight().Text(FormatMoney(request.Totals?.Gross ?? 0)).FontSize(8);
                }
            });
        });
    }

    private void ComposePaymentSection(IContainer container, GeneratePdfRequest request)
    {
        container.Column(col =>
        {
            col.Item().Text("Płatność").Bold().FontSize(10);
            col.Item().PaddingTop(5);

            col.Item().Row(row =>
            {
                row.RelativeItem().Text(t =>
                {
                    t.Span("Metoda płatności: ").FontSize(8);
                    t.Span(request.Payment?.Method ?? "-").FontSize(9);
                });

                if (!string.IsNullOrEmpty(request.Payment?.DueDate))
                {
                    row.RelativeItem().Text(t =>
                    {
                        t.Span("Termin płatności: ").FontSize(8);
                        t.Span(request.Payment.DueDate).FontSize(9);
                    });
                }
            });

            if (!string.IsNullOrEmpty(request.Payment?.BankAccount))
            {
                col.Item().Text(t =>
                {
                    t.Span("Rachunek bankowy: ").FontSize(8);
                    t.Span(request.Payment.BankAccount).FontSize(9);
                });
            }
        });
    }

    private void ComposeVerificationSection(IContainer container, byte[] qrCodeBytes, string verificationUrl, string? ksefNumber)
    {
        container.Column(col =>
        {
            col.Item().Text("Sprawdź, czy Twoja faktura znajduje się w KSeF!").Bold().FontSize(10);
            col.Item().PaddingTop(10);

            col.Item().Row(row =>
            {
                row.ConstantItem(120).Column(qrCol =>
                {
                    if (qrCodeBytes.Length > 0)
                    {
                        qrCol.Item().Width(100).Height(100).Image(qrCodeBytes);
                    }
                    if (!string.IsNullOrEmpty(ksefNumber))
                    {
                        qrCol.Item().PaddingTop(5).Text(ksefNumber).FontSize(6).FontColor(Colors.Grey.Darken1);
                    }
                });

                row.ConstantItem(20);

                row.RelativeItem().Column(textCol =>
                {
                    textCol.Item().PaddingTop(20);
                    textCol.Item().Text("Nie możesz zeskanować kodu z obrazka?").FontSize(9).Bold();
                    textCol.Item().Text("Skopiuj poniższy link i wklej w przeglądarkę:").FontSize(8);
                    textCol.Item().PaddingTop(10);
                    
                    // Klikalny hyperlink
                    textCol.Item()
                        .Hyperlink(verificationUrl)
                        .Text(verificationUrl)
                        .FontSize(7)
                        .FontColor(Colors.Blue.Darken2)
                        .Underline();
                });
            });
        });
    }

    private void ComposeFooter(IContainer container)
    {
        container.AlignCenter().Row(row =>
        {
            row.AutoItem().AlignMiddle().Text("Wytworzona w ").FontSize(7).FontColor(Colors.Grey.Medium);
            row.AutoItem().AlignMiddle()
                .Hyperlink(APP_URL)
                .Text(" KSeF Master")
                .FontSize(7)
                .FontColor(Colors.Blue.Medium)
                .Bold()
                .Underline();
        });
    }

    private string FormatMoney(decimal value)
    {
        return value.ToString("N2", new System.Globalization.CultureInfo("pl-PL"));
    }

    private string FormatVatRate(string rate)
    {
        if (decimal.TryParse(rate, out _))
            return $"{rate}%";
        return rate.ToUpper();
    }
}