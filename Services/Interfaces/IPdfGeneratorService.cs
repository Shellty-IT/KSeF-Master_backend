using KSeF.Backend.Models.Requests;

namespace KSeF.Backend.Services.Interfaces;

public interface IPdfGeneratorService
{
    /// <summary>
    /// Generuje PDF faktury na podstawie danych lokalnych
    /// </summary>
    byte[] GeneratePdf(GeneratePdfRequest request);

    /// <summary>
    /// Generuje PDF faktury pobierając dane z KSeF
    /// </summary>
    Task<byte[]> GeneratePdfFromKsefAsync(string ksefNumber, CancellationToken ct = default);
}