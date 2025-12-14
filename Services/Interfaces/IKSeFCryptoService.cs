namespace KSeF.Backend.Services.Interfaces;

public interface IKSeFCryptoService
{
    /// <summary>
    /// Szyfruje token KSeF certyfikatem (RSA-OAEP SHA-256)
    /// Format: TOKEN|TIMESTAMP_MS
    /// </summary>
    string EncryptToken(string ksefToken, long timestampMs, string certificateBase64);

    /// <summary>
    /// Generuje klucz symetryczny AES-256 (32 bajty) i IV (16 bajtów)
    /// </summary>
    (byte[] Key, byte[] Iv) GenerateAesKeyAndIv();

    /// <summary>
    /// Szyfruje klucz AES certyfikatem RSA (RSA-OAEP SHA-256)
    /// </summary>
    string EncryptAesKey(byte[] aesKey, string certificateBase64);

    /// <summary>
    /// Szyfruje fakturę XML algorytmem AES-256-CBC z PKCS7 padding
    /// </summary>
    byte[] EncryptInvoiceXml(string invoiceXml, byte[] aesKey, byte[] iv);

    /// <summary>
    /// Oblicza SHA-256 i zwraca Base64
    /// </summary>
    string ComputeSha256Base64(byte[] data);
}