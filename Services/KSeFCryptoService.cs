using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using KSeF.Backend.Services.Interfaces;

namespace KSeF.Backend.Services;

public class KSeFCryptoService : IKSeFCryptoService
{
    public string EncryptToken(string ksefToken, long timestampMs, string certificateBase64)
    {
        var dataToEncrypt = $"{ksefToken}|{timestampMs}";
        var dataBytes = Encoding.UTF8.GetBytes(dataToEncrypt);

        var certBytes = Convert.FromBase64String(certificateBase64);
        using var cert = new X509Certificate2(certBytes);

        var rsa = cert.GetRSAPublicKey()
            ?? throw new InvalidOperationException("Certyfikat nie zawiera klucza publicznego RSA");

        var encryptedBytes = rsa.Encrypt(dataBytes, RSAEncryptionPadding.OaepSHA256);
        return Convert.ToBase64String(encryptedBytes);
    }

    public (byte[] Key, byte[] Iv) GenerateAesKeyAndIv()
    {
        var key = new byte[32]; 
        var iv = new byte[16];  

        RandomNumberGenerator.Fill(key);
        RandomNumberGenerator.Fill(iv);

        return (key, iv);
    }

    public string EncryptAesKey(byte[] aesKey, string certificateBase64)
    {
        var certBytes = Convert.FromBase64String(certificateBase64);
        using var cert = new X509Certificate2(certBytes);

        var rsa = cert.GetRSAPublicKey()
            ?? throw new InvalidOperationException("Certyfikat nie zawiera klucza publicznego RSA");

        // RSA-OAEP z SHA-256
        var encryptedBytes = rsa.Encrypt(aesKey, RSAEncryptionPadding.OaepSHA256);
        return Convert.ToBase64String(encryptedBytes);
    }

    public byte[] EncryptInvoiceXml(string invoiceXml, byte[] aesKey, byte[] iv)
    {
        var invoiceBytes = new UTF8Encoding(false).GetBytes(invoiceXml);

        using var aes = Aes.Create();
        aes.Key = aesKey;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var encryptor = aes.CreateEncryptor();
        return encryptor.TransformFinalBlock(invoiceBytes, 0, invoiceBytes.Length);
    }

    public string ComputeSha256Base64(byte[] data)
    {
        var hash = SHA256.HashData(data);
        return Convert.ToBase64String(hash);
    }
}