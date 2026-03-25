// Services/ExternalDraftService.cs
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using KSeF.Backend.Models;
using KSeF.Backend.Models.Requests;
using KSeF.Backend.Services.Interfaces;

namespace KSeF.Backend.Services;

public class ExternalDraftService : IExternalDraftService
{
    private readonly ConcurrentDictionary<string, ExternalDraft> _drafts = new();
    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ExternalDraftService> _logger;

    private static readonly HashSet<string> ValidCurrencies = new() { "PLN", "EUR", "USD", "GBP" };

    public ExternalDraftService(
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        ILogger<ExternalDraftService> logger)
    {
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public ExternalDraft? GetById(string id)
    {
        _drafts.TryGetValue(id, out var draft);
        return draft;
    }

    public ExternalDraft? GetBySmartQuoteId(string smartQuoteId)
    {
        return _drafts.Values.FirstOrDefault(d => d.SmartQuoteId == smartQuoteId);
    }

    public List<ExternalDraft> GetAll(string? statusFilter = null)
    {
        var query = _drafts.Values.AsEnumerable();

        if (!string.IsNullOrEmpty(statusFilter) &&
            Enum.TryParse<ExternalDraftStatus>(statusFilter, true, out var status))
        {
            query = query.Where(d => d.Status == status);
        }

        return query.OrderByDescending(d => d.CreatedAt).ToList();
    }

    public bool ExistsBySmartQuoteId(string smartQuoteId)
    {
        return _drafts.Values.Any(d => d.SmartQuoteId == smartQuoteId);
    }

    public List<string> Validate(SmartQuoteImportRequest request)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(request.SmartQuoteId))
            errors.Add("smartQuoteId jest wymagany");

        if (string.IsNullOrWhiteSpace(request.OfferNumber))
            errors.Add("offerNumber jest wymagany");

        DateOnly issueDate = default;
        DateOnly dueDate = default;

        if (string.IsNullOrWhiteSpace(request.IssueDate) || !DateOnly.TryParse(request.IssueDate, out issueDate))
            errors.Add("issueDate jest wymagany w formacie YYYY-MM-DD");

        if (string.IsNullOrWhiteSpace(request.DueDate) || !DateOnly.TryParse(request.DueDate, out dueDate))
            errors.Add("dueDate jest wymagany w formacie YYYY-MM-DD");

        if (issueDate != default && dueDate != default && dueDate < issueDate)
            errors.Add("dueDate nie może być wcześniejszy niż issueDate");

        ValidateParty(request.Seller, "seller", errors);
        ValidateParty(request.Buyer, "buyer", errors);

        if (request.Items == null || request.Items.Count == 0)
        {
            errors.Add("Wymagana minimum 1 pozycja");
        }
        else
        {
            for (int i = 0; i < request.Items.Count; i++)
            {
                var item = request.Items[i];
                if (string.IsNullOrWhiteSpace(item.Name))
                    errors.Add($"Pozycja {i + 1}: nazwa jest wymagana");
                if (item.Quantity <= 0)
                    errors.Add($"Pozycja {i + 1}: ilość musi być większa od 0");
                if (item.UnitPrice < 0)
                    errors.Add($"Pozycja {i + 1}: cena nie może być ujemna");
                if (item.TotalGross < 0)
                    errors.Add($"Pozycja {i + 1}: kwota brutto nie może być ujemna");
            }
        }

        if (request.TotalGross <= 0)
            errors.Add("totalGross musi być większy od 0");

        if (!ValidCurrencies.Contains(request.Currency.ToUpperInvariant()))
            errors.Add($"Nieobsługiwana waluta: {request.Currency}. Dozwolone: {string.Join(", ", ValidCurrencies)}");

        return errors;
    }

    public ExternalDraft Import(SmartQuoteImportRequest request)
    {
        var draft = new ExternalDraft
        {
            SmartQuoteId = request.SmartQuoteId,
            OfferNumber = request.OfferNumber,
            IssueDate = request.IssueDate,
            DueDate = request.DueDate,
            SellerName = request.Seller.Name,
            SellerNip = request.Seller.Nip,
            SellerAddress = request.Seller.Address,
            SellerCity = request.Seller.City,
            SellerPostalCode = request.Seller.PostalCode,
            BuyerName = request.Buyer.Name,
            BuyerNip = request.Buyer.Nip,
            BuyerAddress = request.Buyer.Address,
            BuyerCity = request.Buyer.City,
            BuyerPostalCode = request.Buyer.PostalCode,
            Items = request.Items.Select(i => new ExternalDraftItem
            {
                Name = i.Name,
                Description = i.Description,
                Quantity = i.Quantity,
                Unit = i.Unit,
                UnitPrice = i.UnitPrice,
                VatRate = i.VatRate,
                Discount = i.Discount,
                TotalNet = i.TotalNet,
                TotalVat = i.TotalVat,
                TotalGross = i.TotalGross
            }).ToList(),
            TotalNet = request.TotalNet,
            TotalVat = request.TotalVat,
            TotalGross = request.TotalGross,
            Currency = request.Currency.ToUpperInvariant(),
            PaymentDays = request.PaymentDays
        };

        _drafts[draft.Id] = draft;
        _logger.LogInformation("Imported SmartQuote draft: {SmartQuoteId} → {DraftId}", request.SmartQuoteId, draft.Id);

        return draft;
    }

    public ExternalDraft? Approve(string id, string processedBy)
    {
        if (!_drafts.TryGetValue(id, out var draft) || draft.Status != ExternalDraftStatus.PENDING)
            return null;

        draft.Status = ExternalDraftStatus.APPROVED;
        draft.ProcessedAt = DateTime.UtcNow;
        draft.ProcessedBy = processedBy;

        _logger.LogInformation("Approved draft: {DraftId} (SmartQuoteId: {SmartQuoteId})", id, draft.SmartQuoteId);

        SendWebhookAsync(draft.SmartQuoteId, "approved", draft.Id, "Faktura zatwierdzona")
            .ContinueWith(t =>
            {
                if (t.IsFaulted) _logger.LogError(t.Exception, "Webhook send failed for draft {DraftId}", id);
            });

        return draft;
    }

    public ExternalDraft? Reject(string id, string processedBy, string reason)
    {
        if (!_drafts.TryGetValue(id, out var draft) || draft.Status != ExternalDraftStatus.PENDING)
            return null;

        draft.Status = ExternalDraftStatus.REJECTED;
        draft.ProcessedAt = DateTime.UtcNow;
        draft.ProcessedBy = processedBy;
        draft.RejectionReason = reason;

        _logger.LogInformation("Rejected draft: {DraftId} (SmartQuoteId: {SmartQuoteId}), reason: {Reason}", id, draft.SmartQuoteId, reason);

        SendWebhookAsync(draft.SmartQuoteId, "rejected", null, reason)
            .ContinueWith(t =>
            {
                if (t.IsFaulted) _logger.LogError(t.Exception, "Webhook send failed for draft {DraftId}", id);
            });

        return draft;
    }

    private void ValidateParty(SmartQuoteParty party, string prefix, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(party.Name))
            errors.Add($"{prefix}.name jest wymagany");

        if (string.IsNullOrWhiteSpace(party.Nip))
        {
            errors.Add($"{prefix}.nip jest wymagany");
        }
        else if (party.Nip.Length != 10 || !party.Nip.All(char.IsDigit))
        {
            errors.Add($"{prefix}.nip musi mieć dokładnie 10 cyfr");
        }

        if (string.IsNullOrWhiteSpace(party.Address))
            errors.Add($"{prefix}.address jest wymagany");

        if (string.IsNullOrWhiteSpace(party.City))
            errors.Add($"{prefix}.city jest wymagany");

        if (string.IsNullOrWhiteSpace(party.PostalCode))
            errors.Add($"{prefix}.postalCode jest wymagany");
    }

    private async Task SendWebhookAsync(string smartQuoteId, string action, string? externalId, string message)
    {
        var webhookUrl = _configuration.GetValue<string>("SmartQuote:WebhookUrl");
        var apiKey = _configuration.GetValue<string>("SmartQuote:ApiKey");

        if (string.IsNullOrEmpty(webhookUrl) || string.IsNullOrEmpty(apiKey))
        {
            _logger.LogWarning("SmartQuote webhook not configured, skipping callback");
            return;
        }

        var payload = new
        {
            smartQuoteId,
            action,
            externalId,
            message
        };

        var client = _httpClientFactory.CreateClient("SmartQuoteWebhook");
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        client.DefaultRequestHeaders.Add("X-API-Key", apiKey);

        var fullUrl = $"{webhookUrl.TrimEnd('/')}/api/ksef/webhook";
        _logger.LogInformation("Sending webhook to {Url}: {Action} for {SmartQuoteId}", fullUrl, action, smartQuoteId);

        try
        {
            var response = await client.PostAsync(fullUrl, content);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Webhook sent successfully for {SmartQuoteId}", smartQuoteId);
            }
            else
            {
                var body = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Webhook failed ({StatusCode}) for {SmartQuoteId}: {Body}", response.StatusCode, smartQuoteId, body);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Webhook request failed for {SmartQuoteId}", smartQuoteId);
        }
    }
}