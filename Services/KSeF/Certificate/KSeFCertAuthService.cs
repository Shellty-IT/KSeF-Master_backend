// Services/KSeF/Certificate/KSeFCertAuthService.cs
using System.Numerics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Text.Json;
using System.Xml;
using KSeF.Backend.Infrastructure.KSeF;
using KSeF.Backend.Models.Responses.Auth;
using KSeF.Backend.Models.Responses.Common;
using KSeF.Backend.Services.Interfaces.KSeF;
using KSeF.Backend.Services.KSeF.Session;

namespace KSeF.Backend.Services.KSeF.Certificate;

public class KSeFCertAuthService : IKSeFCertAuthService
{
    private const string XmlDsigNamespace = "http://www.w3.org/2000/09/xmldsig#";
    private const string XadesNamespace = "http://uri.etsi.org/01903/v1.3.2#";
    private const string ExcC14NAlgorithm = "http://www.w3.org/2001/10/xml-exc-c14n#";
    private const string Sha256Algorithm = "http://www.w3.org/2001/04/xmlenc#sha256";
    private const string EnvelopedAlgorithm = "http://www.w3.org/2000/09/xmldsig#enveloped-signature";
    private const string EcdsaSha256Algorithm = "http://www.w3.org/2001/04/xmldsig-more#ecdsa-sha256";
    private const string RsaSha256Algorithm = "http://www.w3.org/2001/04/xmldsig-more#rsa-sha256";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IKSeFEnvironmentService _environmentService;
    private readonly IKSeFChallengeService _challengeService;
    private readonly IKSeFAuthPollingService _pollingService;
    private readonly IKSeFAuthRedeemService _redeemService;
    private readonly KSeFSessionManager _session;
    private readonly ILogger<KSeFCertAuthService> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public KSeFCertAuthService(
        IHttpClientFactory httpClientFactory,
        IKSeFEnvironmentService environmentService,
        IKSeFChallengeService challengeService,
        IKSeFAuthPollingService pollingService,
        IKSeFAuthRedeemService redeemService,
        KSeFSessionManager session,
        ILogger<KSeFCertAuthService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _environmentService = environmentService;
        _challengeService = challengeService;
        _pollingService = pollingService;
        _redeemService = redeemService;
        _session = session;
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
    }

    public async Task<AuthResult> AuthenticateWithCertificateAsync(
        string nip,
        byte[] certificateBytes,
        byte[] privateKeyBytes,
        string? password,
        string environment = "Test",
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("═══════════════════════════════════════════════════════════════");
            _logger.LogInformation("  LOGOWANIE CERTYFIKATEM DO KSeF API v2 - NIP: {Nip}, ENV: {Env}", nip, environment);
            _logger.LogInformation("═══════════════════════════════════════════════════════════════");

            var apiBaseUrl = _environmentService.GetApiBaseUrl(environment);
            var client = CreateClient(apiBaseUrl);

            _logger.LogInformation("--- Krok 1: Ładowanie certyfikatu ---");
            var certificate = LoadCertificate(certificateBytes, privateKeyBytes, password);
            _logger.LogInformation("  ✓ Subject: {Subject}", certificate.Subject);

            if (!certificate.HasPrivateKey)
                return Fail("Certyfikat nie zawiera klucza prywatnego");

            var ecdsaKey = certificate.GetECDsaPrivateKey();
            var rsaKey = certificate.GetRSAPrivateKey();

            _logger.LogInformation("  ECDSA: {E}, RSA: {R}",
                ecdsaKey != null ? "YES" : "NO",
                rsaKey != null ? "YES" : "NO");

            if (ecdsaKey == null && rsaKey == null)
                return Fail("Brak klucza RSA/ECDSA");

            _logger.LogInformation("--- Krok 2: POST auth/challenge ---");
            var (challenge, _) = await _challengeService.GetChallengeAsync(client, cancellationToken);
            _logger.LogInformation("  ✓ Challenge: {Ch}", challenge);

            _logger.LogInformation("--- Krok 3: Budowanie AuthTokenRequest ---");
            var authRequestXml = BuildAuthTokenRequestXml(challenge, nip);

            _logger.LogInformation("--- Krok 4: Podpisywanie XAdES-BES ---");
            string signedXmlStr;
            if (ecdsaKey != null)
            {
                _logger.LogInformation("  Algorytm: ECDSA-SHA256");
                signedXmlStr = SignXmlEcdsa(authRequestXml, certificate, ecdsaKey);
            }
            else
            {
                _logger.LogInformation("  Algorytm: RSA-SHA256");
                signedXmlStr = SignXmlRsa(authRequestXml, certificate, rsaKey!);
            }

            _logger.LogInformation("  ✓ XML podpisany ({Len} bytes)", signedXmlStr.Length);

            _logger.LogInformation("--- Krok 5: POST auth/xades-signature ---");
            var (authenticationToken, referenceNumber) = await SendXadesSignatureAsync(
                client, signedXmlStr, cancellationToken);

            _logger.LogInformation("  ✓ ReferenceNumber: {Ref}", referenceNumber);

            _logger.LogInformation("--- Krok 6: Polling GET auth/{Ref} ---", referenceNumber);
            var finalToken = await _pollingService.PollAuthStatusAsync(
                client, referenceNumber, authenticationToken, cancellationToken);

            if (finalToken == null)
                return Fail("Timeout autoryzacji - użytkownik nie zatwierdził w aplikacji KSeF");

            _logger.LogInformation("--- Krok 7: POST auth/token/redeem ---");
            var tokens = await _redeemService.RedeemTokenAsync(client, finalToken, cancellationToken);

            if (tokens?.AccessToken == null)
                return Fail("Brak accessToken");

            _session.SetAuthSession(nip, tokens);

            _logger.LogInformation("═══════════════════════════════════════════════════════════════");
            _logger.LogInformation("  ✅ ZALOGOWANO CERTYFIKATEM POMYŚLNIE!");
            _logger.LogInformation("  AccessToken ważny do: {Until}", tokens.AccessToken.ValidUntil);
            _logger.LogInformation("═══════════════════════════════════════════════════════════════");

            return new AuthResult
            {
                Success = true,
                SessionToken = tokens.AccessToken.Token,
                ReferenceNumber = referenceNumber,
                AccessTokenValidUntil = tokens.AccessToken.ValidUntil,
                RefreshTokenValidUntil = tokens.RefreshToken?.ValidUntil
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Certificate authentication failed for NIP: {Nip}", nip);
            return Fail(ex.Message);
        }
    }

    private HttpClient CreateClient(string baseUrl)
    {
        var client = _httpClientFactory.CreateClient("KSeF");
        client.BaseAddress = new Uri(baseUrl);
        return client;
    }

    private async Task<(string authenticationToken, string referenceNumber)> SendXadesSignatureAsync(
        HttpClient client,
        string signedXml,
        CancellationToken cancellationToken)
    {
        var content = new StringContent(signedXml, Encoding.UTF8, "application/xml");
        var response = await client.PostAsync("auth/xades-signature", content, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        _logger.LogInformation("  Response: {Status}", response.StatusCode);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("  Error: {Body}", KSeFResponseLogger.Sanitize(responseBody));
            throw new HttpRequestException(
                $"auth/xades-signature failed ({response.StatusCode}): {responseBody}");
        }

        var parsed = JsonSerializer.Deserialize<AuthTokenResponse>(responseBody, _jsonOptions);
        var authToken = parsed?.AuthenticationToken?.Token;
        var refNumber = parsed?.ReferenceNumber;

        if (string.IsNullOrEmpty(authToken) || string.IsNullOrEmpty(refNumber))
            throw new InvalidOperationException("Missing authenticationToken or referenceNumber");

        return (authToken, refNumber);
    }

    private X509Certificate2 LoadCertificate(byte[] certificateBytes, byte[] privateKeyBytes, string? password)
    {
        var certPem = Encoding.UTF8.GetString(certificateBytes);
        var keyPem = Encoding.UTF8.GetString(privateKeyBytes);

        var isEncrypted = keyPem.Contains("BEGIN ENCRYPTED PRIVATE KEY");
        var isPlain = keyPem.Contains("BEGIN PRIVATE KEY") && !isEncrypted;
        var isTraditionalEc = keyPem.Contains("BEGIN EC PRIVATE KEY");

        _logger.LogInformation("  Key: Encrypted={E}, PlainPKCS8={P}, TraditionalEC={T}",
            isEncrypted, isPlain, isTraditionalEc);

        if (isEncrypted)
        {
            if (string.IsNullOrWhiteSpace(password))
                throw new ArgumentException("Klucz zaszyfrowany - wymagane hasło");

            try
            {
                return X509Certificate2.CreateFromEncryptedPem(certPem, keyPem, password);
            }
            catch (CryptographicException ex)
            {
                throw new ArgumentException("Nieprawidłowe hasło", ex);
            }
        }

        if (isPlain || isTraditionalEc)
            return X509Certificate2.CreateFromPem(certPem, keyPem);

        throw new ArgumentException("Nierozpoznany format klucza");
    }

    private static string BuildAuthTokenRequestXml(string challenge, string nip)
    {
        return $@"<?xml version=""1.0"" encoding=""UTF-8""?><AuthTokenRequest xmlns=""http://ksef.mf.gov.pl/auth/token/2.0""><Challenge>{challenge}</Challenge><ContextIdentifier><Nip>{nip}</Nip></ContextIdentifier><SubjectIdentifierType>certificateSubject</SubjectIdentifierType></AuthTokenRequest>";
    }

    private string SignXmlEcdsa(string xmlContent, X509Certificate2 cert, ECDsa privateKey)
    {
        var doc = new XmlDocument { PreserveWhitespace = false };
        doc.LoadXml(xmlContent);

        var signatureId = "Signature-" + Guid.NewGuid().ToString("N");
        var signedPropsId = "SignedProperties-" + Guid.NewGuid().ToString("N");
        var signingTime = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

        var certDer = cert.Export(X509ContentType.Cert);
        var certBase64 = Convert.ToBase64String(certDer);
        var certDigest = Convert.ToBase64String(SHA256.HashData(certDer));
        var serialNumber = GetSerialNumberAsDecimal(cert);

        var docHash = ComputeDocumentHash(doc);
        var signedPropsXml = BuildSignedPropertiesXml(signedPropsId, signingTime, certDigest, cert.Issuer, serialNumber);
        var signedPropsDoc = new XmlDocument { PreserveWhitespace = false };
        signedPropsDoc.LoadXml(signedPropsXml);
        var signedPropsHash = ComputeC14NHash(signedPropsDoc);
        var signedInfoXml = BuildSignedInfoXml(docHash, signedPropsHash, signedPropsId, EcdsaSha256Algorithm);
        var signedInfoDoc = new XmlDocument { PreserveWhitespace = false };
        signedInfoDoc.LoadXml(signedInfoXml);
        var signedInfoCanonical = GetC14N(signedInfoDoc);
        var signatureBytes = privateKey.SignData(Encoding.UTF8.GetBytes(signedInfoCanonical), HashAlgorithmName.SHA256);
        var signatureValue = Convert.ToBase64String(signatureBytes);

        return AssembleSignedXml(doc, signatureId, signedInfoXml, signatureValue, certBase64, signedPropsXml);
    }

    private string SignXmlRsa(string xmlContent, X509Certificate2 cert, RSA privateKey)
    {
        var doc = new XmlDocument { PreserveWhitespace = false };
        doc.LoadXml(xmlContent);

        var signatureId = "Signature-" + Guid.NewGuid().ToString("N");
        var signedPropsId = "SignedProperties-" + Guid.NewGuid().ToString("N");
        var signingTime = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

        var certDer = cert.Export(X509ContentType.Cert);
        var certBase64 = Convert.ToBase64String(certDer);
        var certDigest = Convert.ToBase64String(SHA256.HashData(certDer));
        var serialNumber = GetSerialNumberAsDecimal(cert);

        var docHash = ComputeDocumentHash(doc);
        var signedPropsXml = BuildSignedPropertiesXml(signedPropsId, signingTime, certDigest, cert.Issuer, serialNumber);
        var signedPropsDoc = new XmlDocument { PreserveWhitespace = false };
        signedPropsDoc.LoadXml(signedPropsXml);
        var signedPropsHash = ComputeC14NHash(signedPropsDoc);
        var signedInfoXml = BuildSignedInfoXml(docHash, signedPropsHash, signedPropsId, RsaSha256Algorithm);
        var signedInfoDoc = new XmlDocument { PreserveWhitespace = false };
        signedInfoDoc.LoadXml(signedInfoXml);
        var signedInfoCanonical = GetC14N(signedInfoDoc);
        var signatureBytes = privateKey.SignData(Encoding.UTF8.GetBytes(signedInfoCanonical), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var signatureValue = Convert.ToBase64String(signatureBytes);

        return AssembleSignedXml(doc, signatureId, signedInfoXml, signatureValue, certBase64, signedPropsXml);
    }

    private string ComputeDocumentHash(XmlDocument doc)
    {
        var copy = new XmlDocument { PreserveWhitespace = false };
        copy.LoadXml(doc.OuterXml);
        var c14n = GetC14N(copy);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(c14n));
        return Convert.ToBase64String(hash);
    }

    private string ComputeC14NHash(XmlDocument doc)
    {
        var c14n = GetC14N(doc);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(c14n));
        return Convert.ToBase64String(hash);
    }

    private string GetC14N(XmlDocument doc)
    {
        var transform = new XmlDsigExcC14NTransform();
        transform.LoadInput(doc);
        var stream = (Stream)transform.GetOutput(typeof(Stream));
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private string BuildSignedPropertiesXml(string signedPropsId, string signingTime, string certDigest, string issuer, string serialNumber)
    {
        return $@"<xades:SignedProperties xmlns:xades=""{XadesNamespace}"" xmlns:ds=""{XmlDsigNamespace}"" Id=""{signedPropsId}""><xades:SignedSignatureProperties><xades:SigningTime>{signingTime}</xades:SigningTime><xades:SigningCertificate><xades:Cert><xades:CertDigest><ds:DigestMethod Algorithm=""{Sha256Algorithm}""/><ds:DigestValue>{certDigest}</ds:DigestValue></xades:CertDigest><xades:IssuerSerial><ds:X509IssuerName>{issuer}</ds:X509IssuerName><ds:X509SerialNumber>{serialNumber}</ds:X509SerialNumber></xades:IssuerSerial></xades:Cert></xades:SigningCertificate></xades:SignedSignatureProperties></xades:SignedProperties>";
    }

    private string BuildSignedInfoXml(string docDigest, string signedPropsDigest, string signedPropsId, string signatureAlgorithm)
    {
        return $@"<ds:SignedInfo xmlns:ds=""{XmlDsigNamespace}""><ds:CanonicalizationMethod Algorithm=""{ExcC14NAlgorithm}""/><ds:SignatureMethod Algorithm=""{signatureAlgorithm}""/><ds:Reference URI=""""><ds:Transforms><ds:Transform Algorithm=""{EnvelopedAlgorithm}""/><ds:Transform Algorithm=""{ExcC14NAlgorithm}""/></ds:Transforms><ds:DigestMethod Algorithm=""{Sha256Algorithm}""/><ds:DigestValue>{docDigest}</ds:DigestValue></ds:Reference><ds:Reference Type=""http://uri.etsi.org/01903#SignedProperties"" URI=""#{signedPropsId}""><ds:Transforms><ds:Transform Algorithm=""{ExcC14NAlgorithm}""/></ds:Transforms><ds:DigestMethod Algorithm=""{Sha256Algorithm}""/><ds:DigestValue>{signedPropsDigest}</ds:DigestValue></ds:Reference></ds:SignedInfo>";
    }

    private string AssembleSignedXml(XmlDocument doc, string signatureId, string signedInfoXml, string signatureValue, string certBase64, string signedPropsXml)
    {
        var signatureXml =
            $@"<ds:Signature xmlns:ds=""{XmlDsigNamespace}"" Id=""{signatureId}"">" +
            signedInfoXml +
            $@"<ds:SignatureValue>{signatureValue}</ds:SignatureValue>" +
            $@"<ds:KeyInfo><ds:X509Data><ds:X509Certificate>{certBase64}</ds:X509Certificate></ds:X509Data></ds:KeyInfo>" +
            $@"<ds:Object><xades:QualifyingProperties xmlns:xades=""{XadesNamespace}"" Target=""#{signatureId}"">" +
            signedPropsXml +
            $@"</xades:QualifyingProperties></ds:Object>" +
            $@"</ds:Signature>";

        var signatureDoc = new XmlDocument();
        signatureDoc.LoadXml(signatureXml);
        doc.DocumentElement!.AppendChild(doc.ImportNode(signatureDoc.DocumentElement!, true));

        using var ms = new MemoryStream();
        using var writer = XmlWriter.Create(ms, new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            Indent = false,
            OmitXmlDeclaration = false
        });
        doc.WriteTo(writer);
        writer.Flush();
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static string GetSerialNumberAsDecimal(X509Certificate2 certificate)
    {
        var serialBytes = certificate.SerialNumberBytes.ToArray();
        Array.Reverse(serialBytes);
        return new BigInteger(serialBytes).ToString();
    }

    private static AuthResult Fail(string error) => new() { Success = false, Error = error };
}