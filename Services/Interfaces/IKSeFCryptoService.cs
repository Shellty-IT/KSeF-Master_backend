// Services/Interfaces/IKSeFCryptoService.cs
namespace KSeF.Backend.Services.Interfaces;

public interface IKSeFCryptoService
{
    string EncryptToken(string ksefToken, long timestampMs, string certificateBase64);
    string SignChallenge(string challenge, string ksefToken);
    (byte[] Key, byte[] Iv) GenerateAesKeyAndIv();
    byte[] EncryptDataWithAes(byte[] data, byte[] key, byte[] iv);
    string EncryptAesKeyWithCertificate(byte[] aesKey, string certificateBase64);
    string ComputeSha256Base64(byte[] data);
    byte[] EncryptInvoiceXml(string invoiceXml, byte[] aesKey, byte[] iv);
    string EncryptAesKey(byte[] aesKey, string certificateBase64);
}