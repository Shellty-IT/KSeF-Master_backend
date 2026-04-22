// Repositories/InvoiceRepository.cs
using Microsoft.EntityFrameworkCore;
using KSeF.Backend.Models.Data;

namespace KSeF.Backend.Repositories;

public class InvoiceRepository : IInvoiceRepository
{
    private readonly AppDbContext _db;

    public InvoiceRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<List<Invoice>> GetByCompanyProfileIdAsync(int companyProfileId)
    {
        return await _db.Invoices
            .AsNoTracking()
            .Where(i => i.CompanyProfileId == companyProfileId)
            .OrderByDescending(i => i.InvoiceDate ?? i.AcquisitionTimestamp)
            .ToListAsync();
    }

    public async Task<Invoice?> GetByKsefReferenceNumberAsync(string ksefReferenceNumber)
    {
        return await _db.Invoices
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.KsefReferenceNumber == ksefReferenceNumber);
    }

    public async Task<DateTime?> GetLatestAcquisitionTimestampAsync(int companyProfileId, string direction)
    {
        return await _db.Invoices
            .Where(i => i.CompanyProfileId == companyProfileId && i.Direction == direction)
            .MaxAsync(i => (DateTime?)i.AcquisitionTimestamp);
    }

    public async Task<List<string>> GetExistingKsefNumbersAsync(int companyProfileId)
    {
        return await _db.Invoices
            .Where(i => i.CompanyProfileId == companyProfileId)
            .Select(i => i.KsefReferenceNumber)
            .ToListAsync();
    }

    public async Task UpsertManyAsync(List<Invoice> invoices)
    {
        if (invoices.Count == 0)
            return;

        foreach (var invoice in invoices)
        {
            var existing = await _db.Invoices
                .FirstOrDefaultAsync(i => i.KsefReferenceNumber == invoice.KsefReferenceNumber);

            if (existing == null)
            {
                invoice.CompanyProfile = null!;
                _db.Invoices.Add(invoice);
            }
            else
            {
                existing.InvoiceNumber = invoice.InvoiceNumber;
                existing.SellerNip = invoice.SellerNip;
                existing.SellerName = invoice.SellerName;
                existing.BuyerNip = invoice.BuyerNip;
                existing.BuyerName = invoice.BuyerName;
                existing.NetAmount = invoice.NetAmount;
                existing.VatAmount = invoice.VatAmount;
                existing.GrossAmount = invoice.GrossAmount;
                existing.Currency = invoice.Currency;
                existing.InvoiceDate = invoice.InvoiceDate;
                existing.XmlContent = invoice.XmlContent;
                existing.SyncedAt = DateTime.UtcNow;
            }
        }

        await _db.SaveChangesAsync();
    }

    public async Task DeleteByCompanyProfileIdAsync(int companyProfileId)
    {
        await _db.Invoices
            .Where(i => i.CompanyProfileId == companyProfileId)
            .ExecuteDeleteAsync();
    }

    public async Task SaveChangesAsync()
    {
        await _db.SaveChangesAsync();
    }
}