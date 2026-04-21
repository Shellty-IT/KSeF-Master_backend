// Services/Pdf/PdfSectionRenderer.cs
using KSeF.Backend.Models.Requests;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace KSeF.Backend.Services.Pdf;

public class PdfSectionRenderer
{
    private const string AppUrl = "https://ksef-master.netlify.app/";

    public void RenderHeader(IContainer container, GeneratePdfRequest request)
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
                    col.Item().Text($"Numer KSeF: {request.KsefNumber}").FontSize(7).FontColor(Colors.Grey.Darken1);
            });
        });
    }

    public void RenderParty(IContainer container, string title, PdfPartyData? party)
    {
        container.Column(col =>
        {
            col.Item().Text(title).Bold().FontSize(10);
            col.Item().PaddingTop(5);

            if (party == null)
                return;

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
        });
    }

    public void RenderDetails(IContainer container, GeneratePdfRequest request)
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

    public void RenderItemsTable(IContainer container, GeneratePdfRequest request)
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

                if (request.Items == null)
                    return;

                for (var i = 0; i < request.Items.Count; i++)
                {
                    var item = request.Items[i];
                    var bg = i % 2 == 0 ? Colors.White : Colors.Grey.Lighten4;

                    table.Cell().Background(bg).Padding(4).Text((i + 1).ToString()).FontSize(8);
                    table.Cell().Background(bg).Padding(4).Text(item.Name).FontSize(8);
                    table.Cell().Background(bg).Padding(4).AlignRight().Text(FormatMoney(item.UnitPriceNet)).FontSize(8);
                    table.Cell().Background(bg).Padding(4).AlignRight().Text(item.Quantity.ToString("0.##")).FontSize(8);
                    table.Cell().Background(bg).Padding(4).Text(item.Unit).FontSize(8);
                    table.Cell().Background(bg).Padding(4).Text(FormatVatRate(item.VatRate)).FontSize(8);
                    table.Cell().Background(bg).Padding(4).AlignRight().Text(FormatMoney(item.NetValue)).FontSize(8);
                }
            });
        });
    }

    public void RenderVatSummary(IContainer container, GeneratePdfRequest request)
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

                if (request.Totals?.PerRate is { Count: > 0 })
                {
                    var i = 1;
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

    public void RenderPayment(IContainer container, GeneratePdfRequest request)
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

    public void RenderVerification(IContainer container, byte[] qrCodeBytes, string verificationUrl, string? ksefNumber)
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
                        qrCol.Item().Width(100).Height(100).Image(qrCodeBytes);

                    if (!string.IsNullOrEmpty(ksefNumber))
                        qrCol.Item().PaddingTop(5).Text(ksefNumber).FontSize(6).FontColor(Colors.Grey.Darken1);
                });

                row.ConstantItem(10);

                row.RelativeItem().Column(textCol =>
                {
                    textCol.Item().PaddingTop(20);
                    textCol.Item().Text("Nie możesz zeskanować kodu z obrazka?").FontSize(9).Bold();
                    textCol.Item().Text("Skopiuj poniższy link i wklej w przeglądarkę:").FontSize(8);
                    textCol.Item().PaddingTop(5);
                    textCol.Item()
                        .Hyperlink(verificationUrl)
                        .Text(verificationUrl)
                        .FontSize(8)
                        .FontColor(Colors.Blue.Darken2);
                });
            });
        });
    }

    public void RenderFooter(IContainer container)
    {
        container.AlignCenter().Row(row =>
        {
            row.AutoItem().AlignMiddle().Text("Wytworzona w ").FontSize(7).FontColor(Colors.Grey.Medium);
            row.AutoItem().AlignMiddle()
                .Hyperlink(AppUrl)
                .Text(" KSeF Master")
                .FontSize(7)
                .FontColor(Colors.Blue.Medium)
                .Bold();
        });
    }

    public static string FormatMoney(decimal value) =>
        value.ToString("N2", new System.Globalization.CultureInfo("pl-PL"));

    public static string FormatVatRate(string rate) =>
        decimal.TryParse(rate, out _) ? $"{rate}%" : rate.ToUpper();
}