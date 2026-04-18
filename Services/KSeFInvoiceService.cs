// Services/KSeFInvoiceService.cs
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using KSeF.Backend.Models.Requests;
using KSeF.Backend.Models.Responses;
using KSeF.Backend.Services.Interfaces;

namespace KSeF.Backend.Services;

public class KSeFInvoiceService : IKSeFInvoiceService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IKSeFAuthService _authService;
    private readonly IKSeFCryptoService _cryptoService;
    private readonly KSeFSessionManager _session;
    private readonly InvoiceXmlGenerator _xmlGenerator;
    private readonly ILogger<KSeFInvoiceService> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public KSeFInvoiceService(
        IHttpClientFactory httpClientFactory,
        IKSeFAuthService authService,
        IKSeFCryptoService cryptoService,
        KSeFSessionManager session,
        InvoiceXmlGenerator xmlGenerator,
        ILogger<KSeFInvoiceService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _authService = authService;
        _cryptoService = cryptoService;
        _session = session;
        _xmlGenerator = xmlGenerator;
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };
    }

    private HttpClient CreateClient() => _httpClientFactory.CreateClient("KSeF");

    public async Task<InvoiceQueryResponse> GetInvoicesAsync(InvoiceQueryRequest request, CancellationToken ct = default)
    {
        EnsureAuthenticated();
        await _authService.RefreshTokenIfNeededAsync(ct);

        var client = CreateClient();
        var allInvoices = new List<InvoiceMetadata>();
        var seenIds = new HashSet<string>();
        var maxResults = request.MaxResults ?? int.MaxValue;

        var sortOrder = request.SortDescending ? "Desc" : "Asc";
        var pageOffset = 0;
        const int pageSize = 100;
        var hasMore = true;
        var iteration = 0;
        const int maxIterations = 200;

        var currentFrom = request.DateRange.From.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        var currentTo = request.DateRange.To.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        var originalFrom = currentFrom;

        _logger.LogInformation(
            "Pobieranie faktur (SubjectType: {Type}, dateType: {DateType}, od: {From}, do: {To}, sortOrder: {Sort})",
            request.SubjectType, request.DateRange.DateType, currentFrom, currentTo, sortOrder);

        while (hasMore && iteration < maxIterations && allInvoices.Count < maxResults)
        {
            iteration++;

            var requestBodyJson = JsonSerializer.Serialize(new
            {
                subjectType = request.SubjectType,
                dateRange = new
                {
                    dateType = request.DateRange.DateType,
                    from = currentFrom,
                    to = currentTo
                }
            });

            var url = $"invoices/query/metadata?sortOrder={sortOrder}&pageOffset={pageOffset}&pageSize={pageSize}";

            _logger.LogDebug("Iter {Iter}: pageOffset={Offset}, from={From}, to={To}", iteration, pageOffset, currentFrom, currentTo);

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url);
            httpRequest.Headers.Add("Authorization", $"Bearer {_session.AccessToken}");
            httpRequest.Content = new StringContent(requestBodyJson, Encoding.UTF8, "application/json");

            var response = await client.SendAsync(httpRequest, ct);
            var content = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"Błąd pobierania faktur: {response.StatusCode} - {content}");

            _logger.LogDebug("Iter {Iter} raw response ({Len} chars): {Content}",
                iteration, content.Length, content.Length > 2000 ? content[..2000] + "..." : content);

            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            var pageHasMore = root.TryGetProperty("hasMore", out var hasMoreEl) && hasMoreEl.GetBoolean();
            var pageIsTruncated = root.TryGetProperty("isTruncated", out var truncEl) && truncEl.GetBoolean();

            string? permanentStorageHwmDate = null;
            if (root.TryGetProperty("permanentStorageHwmDate", out var hwmEl) && hwmEl.ValueKind == JsonValueKind.String)
                permanentStorageHwmDate = hwmEl.GetString();

            var newCount = 0;
            var lastInvoiceDate = string.Empty;

            if (root.TryGetProperty("invoices", out var invoicesEl) && invoicesEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var invEl in invoicesEl.EnumerateArray())
                {
                    var invoice = JsonSerializer.Deserialize<InvoiceMetadata>(invEl.GetRawText(), _jsonOptions);
                    if (invoice == null) continue;

                    if (invEl.TryGetProperty("invoicingMode", out var modeEl))
                        _logger.LogDebug("Faktura {KSeF}: invoicingMode={Mode}", invoice.KsefNumber, modeEl.GetString());

                    if (seenIds.Add(invoice.KsefNumber))
                    {
                        allInvoices.Add(invoice);
                        newCount++;
                        lastInvoiceDate = GetInvoiceDateForCursor(invoice, request.DateRange.DateType);

                        if (allInvoices.Count >= maxResults)
                            break;
                    }
                }
            }

            _logger.LogInformation(
                "Iter {Iter}: +{New} faktur (łącznie: {Total}, hasMore: {HasMore}, isTruncated: {Truncated}, pageOffset: {Offset}, hwm: {Hwm})",
                iteration, newCount, allInvoices.Count, pageHasMore, pageIsTruncated, pageOffset, permanentStorageHwmDate ?? "null");

            if (!pageHasMore)
            {
                hasMore = false;
                break;
            }

            if (allInvoices.Count >= maxResults)
                break;

            if (pageIsTruncated)
            {
                if (string.IsNullOrEmpty(lastInvoiceDate))
                {
                    _logger.LogWarning("isTruncated=true ale brak daty ostatniej faktury, przerywanie");
                    break;
                }

                var newFrom = NormalizeToUtcString(lastInvoiceDate);
                if (newFrom == currentFrom)
                {
                    if (DateTimeOffset.TryParse(lastInvoiceDate, out var parsedDate))
                        newFrom = parsedDate.AddMilliseconds(1).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ");
                    else
                    {
                        _logger.LogWarning("Nie można przesunąć kursora, przerywanie");
                        break;
                    }
                }

                _logger.LogInformation("isTruncated=true: zawężam zakres from: {OldFrom} → {NewFrom}", currentFrom, newFrom);
                currentFrom = newFrom;
                pageOffset = 0;
            }
            else
            {
                pageOffset += pageSize;
                _logger.LogInformation("hasMore=true, isTruncated=false: następna strona pageOffset={Offset}", pageOffset);
            }
        }

        if (iteration >= maxIterations)
            _logger.LogWarning("Osiągnięto limit iteracji ({Max}), pobrano {Count} faktur", maxIterations, allInvoices.Count);

        if (request.SortDescending)
        {
            allInvoices = allInvoices
                .OrderByDescending(i => i.InvoicingDate ?? i.PermanentStorageDate ?? DateTime.MinValue)
                .ToList();
        }
        else
        {
            allInvoices = allInvoices
                .OrderBy(i => i.InvoicingDate ?? i.PermanentStorageDate ?? DateTime.MinValue)
                .ToList();
        }

        _logger.LogInformation(
            "Pobieranie zakończone: {Count} faktur w {Iterations} iteracjach",
            allInvoices.Count, iteration);

        return new InvoiceQueryResponse
        {
            HasMore = false,
            IsTruncated = allInvoices.Count >= maxResults,
            Invoices = allInvoices,
            TotalCount = allInvoices.Count,
            FetchedAt = DateTime.UtcNow,
            PagesProcessed = iteration
        };
    }

    private string GetInvoiceDateForCursor(InvoiceMetadata invoice, string dateType)
    {
        return dateType switch
        {
            "Issue" => invoice.IssueDate ?? string.Empty,
            "Invoicing" => invoice.InvoicingDate?.ToString("O") ?? string.Empty,
            _ => invoice.PermanentStorageDate?.ToString("O") ?? string.Empty
        };
    }

    private static string NormalizeToUtcString(string dateString)
    {
        if (DateTimeOffset.TryParse(dateString, out var dto))
            return dto.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ");
        return dateString;
    }

    public async Task<InvoiceStatsResponse> GetInvoiceStatsAsync(int months = 3, CancellationToken ct = default)
    {
        EnsureAuthenticated();

        var now = DateTime.UtcNow;
        var allIssued = new List<InvoiceMetadata>();
        var allReceived = new List<InvoiceMetadata>();

        var windowCount = Math.Max(1, (int)Math.Ceiling(months / 3.0));

        var issuedTasks = new List<Task<InvoiceQueryResponse>>();
        var receivedTasks = new List<Task<InvoiceQueryResponse>>();

        for (var i = 0; i < windowCount; i++)
        {
            var windowTo = now.AddMonths(-i * 3);
            var windowFrom = now.AddMonths(-(i + 1) * 3);

            if (windowFrom < now.AddMonths(-months))
                windowFrom = now.AddMonths(-months);

            var issuedReq = new InvoiceQueryRequest
            {
                SubjectType = "Subject1",
                DateRange = new DateRangeFilter
                {
                    DateType = "PermanentStorage",
                    From = windowFrom,
                    To = windowTo
                },
                SortDescending = false
            };

            var receivedReq = new InvoiceQueryRequest
            {
                SubjectType = "Subject2",
                DateRange = new DateRangeFilter
                {
                    DateType = "PermanentStorage",
                    From = windowFrom,
                    To = windowTo
                },
                SortDescending = false
            };

            issuedTasks.Add(GetInvoicesAsync(issuedReq, ct));
            receivedTasks.Add(GetInvoicesAsync(receivedReq, ct));
        }

        await Task.WhenAll(issuedTasks.Concat(receivedTasks));

        var seenIssued = new HashSet<string>();
        var seenReceived = new HashSet<string>();

        foreach (var task in issuedTasks)
        {
            foreach (var inv in task.Result.Invoices)
                if (seenIssued.Add(inv.KsefNumber))
                    allIssued.Add(inv);
        }

        foreach (var task in receivedTasks)
        {
            foreach (var inv in task.Result.Invoices)
                if (seenReceived.Add(inv.KsefNumber))
                    allReceived.Add(inv);
        }

        var stats = new InvoiceStatsResponse
        {
            IssuedCount = allIssued.Count,
            ReceivedCount = allReceived.Count,
            IssuedNetTotal = allIssued.Sum(i => i.NetAmount ?? 0),
            IssuedGrossTotal = allIssued.Sum(i => i.GrossAmount ?? 0),
            ReceivedNetTotal = allReceived.Sum(i => i.NetAmount ?? 0),
            ReceivedGrossTotal = allReceived.Sum(i => i.GrossAmount ?? 0),
            PeriodFrom = now.AddMonths(-months),
            PeriodTo = now,
            FetchedAt = DateTime.UtcNow
        };

        var allMonths = Enumerable.Range(0, months)
            .Select(i => now.AddMonths(-i))
            .Select(d => d.ToString("yyyy-MM"))
            .Reverse()
            .ToList();

        foreach (var month in allMonths)
        {
            var monthIssued = allIssued.Where(i => GetMonth(i) == month).ToList();
            var monthReceived = allReceived.Where(i => GetMonth(i) == month).ToList();

            stats.Monthly.Add(new MonthlyStats
            {
                Month = month,
                IssuedCount = monthIssued.Count,
                ReceivedCount = monthReceived.Count,
                IssuedGross = monthIssued.Sum(i => i.GrossAmount ?? 0),
                ReceivedGross = monthReceived.Sum(i => i.GrossAmount ?? 0)
            });
        }

        var contractorCounts = allReceived
            .Where(i => i.Seller?.Nip != null)
            .GroupBy(i => i.Seller!.Nip!)
            .OrderByDescending(g => g.Count())
            .Take(10)
            .ToDictionary(g => g.Key, g => g.Count());

        stats.TopContractors = contractorCounts;

        var combined = allIssued.Concat(allReceived).ToList();
        stats.ByCurrency = combined
            .GroupBy(i => i.Currency ?? "PLN")
            .ToDictionary(
                g => g.Key,
                g => new CurrencyStats
                {
                    Count = g.Count(),
                    NetTotal = g.Sum(i => i.NetAmount ?? 0),
                    GrossTotal = g.Sum(i => i.GrossAmount ?? 0)
                });

        return stats;
    }

    private static string GetMonth(InvoiceMetadata invoice)
    {
        var date = invoice.InvoicingDate
            ?? invoice.PermanentStorageDate
            ?? (DateTime.TryParse(invoice.IssueDate, out var parsed) ? parsed : DateTime.MinValue);
        return date.ToString("yyyy-MM");
    }

    public async Task<InvoiceDetailsResult> GetInvoiceDetailsAsync(string ksefNumber, CancellationToken ct = default)
    {
        EnsureAuthenticated();
        await _authService.RefreshTokenIfNeededAsync(ct);

        try
        {
            var client = CreateClient();
            var endpoints = new[]
            {
                $"online/Invoice/Get/{ksefNumber}",
                $"invoices/{ksefNumber}",
                $"online/invoices/{ksefNumber}"
            };

            HttpResponseMessage? response = null;
            string? content = null;

            foreach (var endpoint in endpoints)
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, endpoint);
                req.Headers.Add("Authorization", $"Bearer {_session.AccessToken}");
                response = await client.SendAsync(req, ct);
                content = await response.Content.ReadAsStringAsync(ct);
                if (response.IsSuccessStatusCode) break;
            }

            if (response == null || !response.IsSuccessStatusCode)
                return new InvoiceDetailsResult { Success = false, Error = $"Nie można pobrać faktury: {response?.StatusCode}" };

            var detailsResponse = JsonSerializer.Deserialize<InvoiceDetailsResponse>(content!, _jsonOptions);
            if (detailsResponse == null)
                return new InvoiceDetailsResult { Success = false, Error = "Pusta odpowiedź z KSeF" };

            var invoiceHash = detailsResponse.InvoiceHash?.HashSHA?.Value ?? "";
            var invoiceXml = DecodeInvoiceBody(detailsResponse.InvoicePayload?.InvoiceBody);

            if (string.IsNullOrEmpty(invoiceXml))
                return new InvoiceDetailsResult { Success = false, Error = "Brak XML faktury w odpowiedzi" };

            var result = ParseInvoiceXml(invoiceXml, ksefNumber, invoiceHash);
            result.Success = true;
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Błąd pobierania szczegółów faktury");
            return new InvoiceDetailsResult { Success = false, Error = ex.Message };
        }
    }

    private string? DecodeInvoiceBody(string? invoiceBody)
    {
        if (string.IsNullOrEmpty(invoiceBody)) return null;
        try
        {
            var bytes = Convert.FromBase64String(invoiceBody);
            return Encoding.UTF8.GetString(bytes);
        }
        catch { return invoiceBody; }
    }

    private InvoiceDetailsResult ParseInvoiceXml(string xml, string ksefNumber, string invoiceHash)
    {
        var doc = XDocument.Parse(xml);
        var ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;

        var result = new InvoiceDetailsResult
        {
            KsefNumber = ksefNumber,
            InvoiceHash = invoiceHash,
            InvoiceXml = xml,
            InvoiceNumber = doc.Descendants(ns + "P_2").FirstOrDefault()?.Value,
            IssueDate = doc.Descendants(ns + "P_1").FirstOrDefault()?.Value,
            Items = new List<InvoiceItemResult>()
        };

        var podmiot1 = doc.Descendants(ns + "Podmiot1").FirstOrDefault();
        if (podmiot1 != null)
        {
            var dane1 = podmiot1.Descendants(ns + "DaneIdentyfikacyjne").FirstOrDefault();
            var adres1 = podmiot1.Descendants(ns + "Adres").FirstOrDefault();
            result.SellerNip = dane1?.Element(ns + "NIP")?.Value;
            result.SellerName = dane1?.Element(ns + "Nazwa")?.Value;
            result.SellerAddress = adres1?.Element(ns + "AdresL1")?.Value;
        }

        var podmiot2 = doc.Descendants(ns + "Podmiot2").FirstOrDefault();
        if (podmiot2 != null)
        {
            var dane2 = podmiot2.Descendants(ns + "DaneIdentyfikacyjne").FirstOrDefault();
            var adres2 = podmiot2.Descendants(ns + "Adres").FirstOrDefault();
            result.BuyerNip = dane2?.Element(ns + "NIP")?.Value;
            result.BuyerName = dane2?.Element(ns + "Nazwa")?.Value;
            result.BuyerAddress = adres2?.Element(ns + "AdresL1")?.Value;
        }

        foreach (var wiersz in doc.Descendants(ns + "FaWiersz"))
        {
            decimal.TryParse(wiersz.Element(ns + "P_8B")?.Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var qty);
            decimal.TryParse(wiersz.Element(ns + "P_9A")?.Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var price);
            decimal.TryParse(wiersz.Element(ns + "P_11")?.Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var netValue);
            var vatRate = wiersz.Element(ns + "P_12")?.Value ?? "23";
            var vatValue = CalculateVatFromRate(netValue, vatRate);

            result.Items.Add(new InvoiceItemResult
            {
                Name = wiersz.Element(ns + "P_7")?.Value ?? "",
                Unit = wiersz.Element(ns + "P_8A")?.Value ?? "szt.",
                Quantity = qty > 0 ? qty : 1,
                UnitPriceNet = price,
                VatRate = vatRate,
                NetValue = netValue,
                VatValue = vatValue,
                GrossValue = netValue + vatValue
            });
        }

        var fa = doc.Descendants(ns + "Fa").FirstOrDefault();
        if (fa != null)
        {
            decimal.TryParse(fa.Element(ns + "P_13_1")?.Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var net);
            decimal.TryParse(fa.Element(ns + "P_14_1")?.Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var vat);
            decimal.TryParse(fa.Element(ns + "P_15")?.Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var gross);
            result.NetTotal = net;
            result.VatTotal = vat;
            result.GrossTotal = gross;
        }

        return result;
    }

    private decimal CalculateVatFromRate(decimal net, string vatRate)
    {
        if (decimal.TryParse(vatRate, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var rate))
            return Math.Round(net * rate / 100, 2);
        return vatRate.ToLower() switch { "zw" or "np" or "oo" => 0, _ => Math.Round(net * 0.23m, 2) };
    }

    public async Task<SessionResult> OpenOnlineSessionAsync(CancellationToken ct = default)
    {
        EnsureAuthenticated();
        await _authService.RefreshTokenIfNeededAsync(ct);

        if (_session.HasActiveOnlineSession)
            return new SessionResult
            {
                Success = true,
                SessionReferenceNumber = _session.SessionReferenceNumber,
                ValidUntil = _session.SessionValidUntil
            };

        try
        {
            var client = CreateClient();
            var certificates = _session.GetCachedCertificates() ?? await GetCertificatesAsync(client, ct);
            var symCert = certificates.First(c => c.Usage?.Contains("SymmetricKeyEncryption") == true);
            var (aesKey, iv) = _cryptoService.GenerateAesKeyAndIv();
            var encryptedSymmetricKey = _cryptoService.EncryptAesKey(aesKey, symCert.Certificate);

            var requestBody = new
            {
                formCode = new { systemCode = "FA (3)", schemaVersion = "1-0E", value = "FA" },
                encryption = new { encryptedSymmetricKey, initializationVector = Convert.ToBase64String(iv) }
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, "sessions/online");
            request.Headers.Add("Authorization", $"Bearer {_session.AccessToken}");
            request.Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

            var response = await client.SendAsync(request, ct);
            var content = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
                return new SessionResult { Success = false, Error = content };

            var sessionResponse = JsonSerializer.Deserialize<OpenSessionResponse>(content, _jsonOptions)!;
            _session.SetOnlineSession(sessionResponse.ReferenceNumber, sessionResponse.ValidUntil, aesKey, iv);

            return new SessionResult
            {
                Success = true,
                SessionReferenceNumber = sessionResponse.ReferenceNumber,
                ValidUntil = sessionResponse.ValidUntil
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Błąd otwierania sesji");
            return new SessionResult { Success = false, Error = ex.Message };
        }
    }

    public async Task<SendInvoiceResult> SendInvoiceAsync(CreateInvoiceRequest invoiceData, CancellationToken ct = default)
    {
        EnsureAuthenticated();
        await _authService.RefreshTokenIfNeededAsync(ct);

        var sessionNip = _session.Nip;
        if (!string.IsNullOrEmpty(sessionNip) && invoiceData.Seller.Nip != sessionNip)
            invoiceData.Seller.Nip = sessionNip;

        if (!_session.HasActiveOnlineSession)
        {
            var sessionResult = await OpenOnlineSessionAsync(ct);
            if (!sessionResult.Success)
                return new SendInvoiceResult { Success = false, Error = $"Nie można otworzyć sesji: {sessionResult.Error}" };
        }

        try
        {
            var client = CreateClient();
            var invoiceXml = _xmlGenerator.GenerateInvoiceXml(invoiceData, sessionNip);
            var invoiceBytes = new UTF8Encoding(false).GetBytes(invoiceXml);
            var invoiceHash = _cryptoService.ComputeSha256Base64(invoiceBytes);
            var encryptedInvoice = _cryptoService.EncryptInvoiceXml(invoiceXml, _session.AesKey!, _session.Iv!);
            var encryptedInvoiceHash = _cryptoService.ComputeSha256Base64(encryptedInvoice);

            var requestBody = new
            {
                invoiceHash,
                invoiceSize = invoiceBytes.Length,
                encryptedInvoiceHash,
                encryptedInvoiceSize = encryptedInvoice.Length,
                encryptedInvoiceContent = Convert.ToBase64String(encryptedInvoice),
                offlineMode = false
            };

            using var request = new HttpRequestMessage(HttpMethod.Post,
                $"sessions/online/{_session.SessionReferenceNumber}/invoices");
            request.Headers.Add("Authorization", $"Bearer {_session.AccessToken}");
            request.Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

            var response = await client.SendAsync(request, ct);
            var content = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
                return new SendInvoiceResult { Success = false, Error = ParseKsefError(content) ?? content };

            var sendResponse = JsonSerializer.Deserialize<SendInvoiceApiResponse>(content, _jsonOptions);

            return new SendInvoiceResult
            {
                Success = true,
                ElementReferenceNumber = sendResponse?.ElementReferenceNumber,
                ProcessingCode = sendResponse?.ProcessingCode,
                ProcessingDescription = sendResponse?.ProcessingDescription,
                InvoiceHash = invoiceHash
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Błąd wysyłania faktury");
            return new SendInvoiceResult { Success = false, Error = ex.Message };
        }
    }

    private string? ParseKsefError(string responseContent)
    {
        try
        {
            using var jsonDoc = JsonDocument.Parse(responseContent);
            var root = jsonDoc.RootElement;
            if (root.TryGetProperty("exception", out var exception))
            {
                if (exception.TryGetProperty("exceptionDetailList", out var details)
                    && details.ValueKind == JsonValueKind.Array
                    && details.GetArrayLength() > 0)
                    if (details[0].TryGetProperty("exceptionDescription", out var desc))
                        return desc.GetString();
                if (exception.TryGetProperty("serviceCtx", out var ctx))
                    return ctx.GetString();
            }
            if (root.TryGetProperty("message", out var message)) return message.GetString();
            if (root.TryGetProperty("error", out var error)) return error.GetString();
        }
        catch { }
        return null;
    }

    public async Task<bool> CloseOnlineSessionAsync(CancellationToken ct = default)
    {
        if (!_session.HasActiveOnlineSession) return true;
        try
        {
            var client = CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Delete,
                $"sessions/online/{_session.SessionReferenceNumber}");
            request.Headers.Add("Authorization", $"Bearer {_session.AccessToken}");
            await client.SendAsync(request, ct);
            _session.ClearOnlineSession();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Błąd zamykania sesji");
            _session.ClearOnlineSession();
            return false;
        }
    }

    private void EnsureAuthenticated()
    {
        if (!_session.IsAuthenticated)
            throw new UnauthorizedAccessException("Nie jesteś zalogowany. Użyj POST /api/ksef/login");
    }

    private async Task<List<CertificateInfo>> GetCertificatesAsync(HttpClient client, CancellationToken ct)
    {
        var response = await client.GetAsync("security/public-key-certificates", ct);
        var content = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Błąd pobierania certyfikatów: {content}");
        var certificates = JsonSerializer.Deserialize<List<CertificateInfo>>(content, _jsonOptions)!;
        _session.SetCertificates(certificates);
        return certificates;
    }
}