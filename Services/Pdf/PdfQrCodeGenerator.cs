using QRCoder;

namespace KSeF.Backend.Services.Pdf;

public class PdfQrCodeGenerator
{
    public byte[] Generate(string url)
    {
        if (string.IsNullOrEmpty(url))
            return Array.Empty<byte>();

        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(url, QRCodeGenerator.ECCLevel.M);
        using var code = new PngByteQRCode(data);

        return code.GetGraphic(8);
    }
}