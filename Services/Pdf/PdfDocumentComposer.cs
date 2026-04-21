using KSeF.Backend.Models.Requests;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace KSeF.Backend.Services.Pdf;

public class PdfDocumentComposer
{
    private readonly PdfSectionRenderer _renderer;

    public PdfDocumentComposer(PdfSectionRenderer renderer)
    {
        _renderer = renderer;
    }

    public Document Compose(GeneratePdfRequest request, byte[] qrCodeBytes, string verificationUrl, string qrUrl)
    {
        var hasQrCode = qrCodeBytes.Length > 0 && !string.IsNullOrEmpty(qrUrl);

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

                page.Header().Element(c => _renderer.RenderHeader(c, request));
                page.Content().Element(c => ComposeContent(c, request, qrCodeBytes, verificationUrl, hasQrCode));
                page.Footer().Element(c => _renderer.RenderFooter(c));
            });
        });
    }

    private void ComposeContent(
        IContainer container,
        GeneratePdfRequest request,
        byte[] qrCodeBytes,
        string verificationUrl,
        bool hasQrCode)
    {
        container.Column(col =>
        {
            col.Spacing(10);

            col.Item().LineHorizontal(1).LineColor(Colors.Grey.Lighten2);

            col.Item().Row(row =>
            {
                row.RelativeItem().Element(c => _renderer.RenderParty(c, "Sprzedawca", request.Seller));
                row.ConstantItem(20);
                row.RelativeItem().Element(c => _renderer.RenderParty(c, "Nabywca", request.Buyer));
            });

            col.Item().LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
            col.Item().Element(c => _renderer.RenderDetails(c, request));
            col.Item().LineHorizontal(1).LineColor(Colors.Grey.Lighten2);

            if (request.Items is { Count: > 0 })
                col.Item().Element(c => _renderer.RenderItemsTable(c, request));

            col.Item().AlignRight()
                .Text($"Kwota należności ogółem: {PdfSectionRenderer.FormatMoney(request.Totals?.Gross ?? 0)} PLN")
                .Bold().FontSize(12);

            col.Item().LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
            col.Item().Element(c => _renderer.RenderVatSummary(c, request));

            if (request.Payment != null)
            {
                col.Item().LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
                col.Item().Element(c => _renderer.RenderPayment(c, request));
            }

            if (hasQrCode)
            {
                col.Item().LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
                col.Item().Element(c => _renderer.RenderVerification(c, qrCodeBytes, verificationUrl, request.KsefNumber));
            }
        });
    }
}