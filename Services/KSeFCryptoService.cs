// Services/KSeFCryptoService.cs
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using KSeF.Backend.Services.Interfaces;

namespace KSeF.Backend.Services;

public class KSeFCryptoService : IKSeFCryptoService
{
    public string SignChallenge(string challenge, string ksefToken)
    {
        var data = $"{challenge}|{ksefToken}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(data));
        return Convert.ToBase64String(hash);
    }

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

    public byte[] EncryptDataWithAes(byte[] data, byte[] key, byte[] iv)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var encryptor = aes.CreateEncryptor();
        return encryptor.TransformFinalBlock(data, 0, data.Length);
    }

    public string EncryptAesKeyWithCertificate(byte[] aesKey, string certificateBase64)
    {
        var certBytes = Convert.FromBase64String(certificateBase64);
        using var cert = new X509Certificate2(certBytes);

        var rsa = cert.GetRSAPublicKey()
            ?? throw new InvalidOperationException("Certyfikat nie zawiera klucza publicznego RSA");

        var encryptedKey = rsa.Encrypt(aesKey, RSAEncryptionPadding.OaepSHA256);
        return Convert.ToBase64String(encryptedKey);
    }

    public string EncryptAesKey(byte[] aesKey, string certificateBase64)
        => EncryptAesKeyWithCertificate(aesKey, certificateBase64);

    public string ComputeSha256Base64(byte[] data)
    {
        var hash = SHA256.HashData(data);
        return Convert.ToBase64String(hash);
    }

    public byte[] EncryptInvoiceXml(string invoiceXml, byte[] aesKey, byte[] iv)
    {
        var invoiceBytes = new UTF8Encoding(false).GetBytes(invoiceXml);
        return EncryptDataWithAes(invoiceBytes, aesKey, iv);
    }
}