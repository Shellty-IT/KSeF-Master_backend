// Services/Pdf/PdfGeneratorService.cs
using KSeF.Backend.Models.Requests;
using KSeF.Backend.Services.Interfaces.Pdf;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace KSeF.Backend.Services.Pdf;

public class PdfGeneratorService : IPdfGeneratorService
{
    private readonly PdfUrlBuilder _urlBuilder;
    private readonly PdfQrCodeGenerator _qrGenerator;
    private readonly PdfDocumentComposer _composer;
    private readonly ILogger<PdfGeneratorService> _logger;

    public PdfGeneratorService(
        PdfUrlBuilder urlBuilder,
        PdfQrCodeGenerator qrGenerator,
        PdfDocumentComposer composer,
        ILogger<PdfGeneratorService> logger)
    {
        _urlBuilder = urlBuilder;
        _qrGenerator = qrGenerator;
        _composer = composer;
        _logger = logger;

        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] GeneratePdf(GeneratePdfRequest request)
    {
        _logger.LogInformation("Generowanie PDF dla faktury: {Number}", request.InvoiceNumber);

        var environment = "Test";
        var sellerNip = request.Seller?.Nip ?? string.Empty;
        var issueDate = request.IssueDate ?? string.Empty;
        var invoiceHash = request.InvoiceHash ?? string.Empty;

        var verificationUrl = _urlBuilder.BuildVerificationUrl(sellerNip, issueDate, invoiceHash, environment);
        var qrCodeBytes = _qrGenerator.Generate(verificationUrl);

        _logger.LogInformation("URL weryfikacyjny: {Url}", verificationUrl);

        var document = _composer.Compose(request, qrCodeBytes, verificationUrl, verificationUrl);

        using var stream = new MemoryStream();
        document.GeneratePdf(stream);
        return stream.ToArray();
    }

    public Task<byte[]> GeneratePdfFromKsefAsync(string ksefNumber, CancellationToken ct = default)
    {
        throw new NotSupportedException("Pobieranie szczegółów z KSeF nie jest obsługiwane bezpośrednio.");
    }
}