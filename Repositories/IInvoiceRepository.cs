// Repositories/IInvoiceRepository.cs
using KSeF.Backend.Models.Data;

namespace KSeF.Backend.Repositories;

public interface IInvoiceRepository
{
    Task<List<Invoice>> GetByCompanyProfileIdAsync(int companyProfileId);
    Task<Invoice?> GetByKsefReferenceNumberAsync(string ksefReferenceNumber);
    Task<DateTime?> GetLatestAcquisitionTimestampAsync(int companyProfileId, string direction);
    Task<List<string>> GetExistingKsefNumbersAsync(int companyProfileId);
    Task UpsertManyAsync(List<Invoice> invoices);
    Task DeleteByCompanyProfileIdAsync(int companyProfileId);
    Task SaveChangesAsync();
}