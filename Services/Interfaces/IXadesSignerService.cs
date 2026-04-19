// Services/Interfaces/IXadesSignerService.cs
using System.Security.Cryptography.X509Certificates;

namespace KSeF.Backend.Services.Interfaces;

public interface IXadesSignerService
{
    string SignXmlWithXades(string xmlContent, X509Certificate2 certificate);
}