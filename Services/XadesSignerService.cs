// Services/XadesSignerService.cs
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Xml;
using KSeF.Backend.Services.Interfaces;

namespace KSeF.Backend.Services;

public class XadesSignerService : IXadesSignerService
{
    public string SignXmlWithXades(string xmlContent, X509Certificate2 certificate)
    {
        var doc = new XmlDocument { PreserveWhitespace = true };
        doc.LoadXml(xmlContent);

        if (!certificate.HasPrivateKey)
        {
            throw new InvalidOperationException("Certificate does not contain a private key");
        }

        var privateKey = certificate.GetRSAPrivateKey();
        if (privateKey == null)
        {
            throw new InvalidOperationException("Certificate does not contain an RSA private key");
        }

        var signedXml = new SignedXml(doc)
        {
            SigningKey = privateKey
        };

        signedXml.SignedInfo.CanonicalizationMethod = SignedXml.XmlDsigExcC14NTransformUrl;
        signedXml.SignedInfo.SignatureMethod = SignedXml.XmlDsigRSASHA256Url;

        var reference = new Reference { Uri = "" };
        reference.DigestMethod = SignedXml.XmlDsigSHA256Url;
        
        var envTransform = new XmlDsigEnvelopedSignatureTransform();
        reference.AddTransform(envTransform);
        
        var c14nTransform = new XmlDsigExcC14NTransform();
        reference.AddTransform(c14nTransform);
        
        signedXml.AddReference(reference);

        var keyInfo = new KeyInfo();
        var x509Data = new KeyInfoX509Data(certificate);
        x509Data.AddSubjectName(certificate.Subject);
        x509Data.AddIssuerSerial(certificate.Issuer, certificate.GetSerialNumberString());
        keyInfo.AddClause(x509Data);
        signedXml.KeyInfo = keyInfo;

        signedXml.ComputeSignature();

        var signatureElement = signedXml.GetXml();
        
        if (doc.DocumentElement == null)
        {
            throw new InvalidOperationException("Document element is null");
        }

        doc.DocumentElement.AppendChild(doc.ImportNode(signatureElement, true));

        return doc.OuterXml;
    }
}