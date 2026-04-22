// Models/Responses/Invoice/InvoiceSyncResult.cs
namespace KSeF.Backend.Models.Responses.Invoice;

public class InvoiceSyncResult
{
    public int NewCount { get; set; }
    public int TotalFetched { get; set; }
    public DateTime SyncedAt { get; set; }
    public string Direction { get; set; } = string.Empty;
}