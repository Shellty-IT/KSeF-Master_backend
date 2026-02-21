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
        var hasMore = true;
        var iteration = 0;
        const int maxIterations = 50;
        var maxResults = request.MaxResults ?? int.MaxValue;

        var currentFrom = request.DateRange.From.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        var originalTo = request.DateRange.To.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

        _logger.LogInformation(
            "Pobieranie faktur (SubjectType: {Type}, dateType: {DateType}, od: {From}, do: {To})",
            request.SubjectType, request.DateRange.DateType, currentFrom, originalTo);

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
                    to = originalTo
                }
            });

            _logger.LogDebug("Strona {Page} request body: {Body}", iteration, requestBodyJson);

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "invoices/query/metadata");
            httpRequest.Headers.Add("Authorization", $"Bearer {_session.AccessToken}");
            httpRequest.Content = new StringContent(requestBodyJson, Encoding.UTF8, "application/json");

            var response = await client.SendAsync(httpRequest, ct);
            var content = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"Błąd pobierania faktur: {response.StatusCode} - {content}");

            _logger.LogDebug("Strona {Page} raw response ({Len} chars): {Content}",
                iteration, content.Length, content.Length > 2000 ? content[..2000] + "..." : content);

            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            var pageHasMore = root.TryGetProperty("hasMore", out var hasMoreEl) && hasMoreEl.GetBoolean();

            string? rawHwmDate = null;
            if (root.TryGetProperty("permanentStorageHwmDate", out var hwmEl) && hwmEl.ValueKind == JsonValueKind.String)
                rawHwmDate = hwmEl.GetString();

            var newCount = 0;

            if (root.TryGetProperty("invoices", out var invoicesEl) && invoicesEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var invEl in invoicesEl.EnumerateArray())
                {
                    var invoice = JsonSerializer.Deserialize<InvoiceMetadata>(invEl.GetRawText(), _jsonOptions);
                    if (invoice == null) continue;

                    if (seenIds.Add(invoice.KsefNumber))
                    {
                        allInvoices.Add(invoice);
                        newCount++;

                        if (allInvoices.Count >= maxResults)
                            break;
                    }
                }
            }

            _logger.LogInformation(
                "Strona {Page}: +{New} nowych (łącznie: {Total}, hasMore: {HasMore}, hwm: {Hwm})",
                iteration, newCount, allInvoices.Count, pageHasMore, rawHwmDate ?? "null");

            hasMore = pageHasMore;

            if (hasMore)
            {
                if (!string.IsNullOrEmpty(rawHwmDate))
                {
                    var normalizedHwm = NormalizeToUtcString(rawHwmDate);

                    if (normalizedHwm == currentFrom)
                    {
                        if (newCount == 0)
                        {
                            _logger.LogWarning("Kursor nie postępuje (hwm == from) i 0 nowych, przerywanie");
                            break;
                        }

                        if (DateTimeOffset.TryParse(rawHwmDate, out var parsed))
                        {
                            normalizedHwm = parsed.AddMilliseconds(1).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ");
                            _logger.LogInformation("Kursor == from, przesuwam o 1ms: {NewFrom}", normalizedHwm);
                        }
                        else
                        {
                            _logger.LogWarning("Nie można sparsować HWM, przerywanie");
                            break;
                        }
                    }

                    currentFrom = normalizedHwm;
                    _logger.LogInformation("Następna strona: from = {From}", currentFrom);
                }
                else
                {
                    _logger.LogWarning("Brak permanentStorageHwmDate mimo hasMore=true, przerywanie");
                    break;
                }
            }
        }

        if (iteration >= maxIterations)
            _logger.LogWarning("Osiągnięto limit iteracji ({Max}), pobrano {Count} faktur", maxIterations, allInvoices.Count);

        if (request.SortDescending)
        {
            allInvoices = allInvoices
                .OrderByDescending(i => i.PermanentStorageDate ?? i.InvoicingDate ?? DateTime.MinValue)
                .ToList();
        }
        else
        {
            allInvoices = allInvoices
                .OrderBy(i => i.PermanentStorageDate ?? i.InvoicingDate ?? DateTime.MinValue)
                .ToList();
        }

        _logger.LogInformation(
            "Pobieranie zakończone: {Count} faktur w {Iterations} iteracjach (dateType: {DateType})",
            allInvoices.Count, iteration, request.DateRange.DateType);

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
        var from = now.AddMonths(-months);

        var issuedRequest = new InvoiceQueryRequest
        {
            SubjectType = "Subject1",
            DateRange = new DateRangeFilter { DateType = "InvoicingDate", From = from, To = now },
            SortDescending = true
        };

        var receivedRequest = new InvoiceQueryRequest
        {
            SubjectType = "Subject2",
            DateRange = new DateRangeFilter { DateType = "InvoicingDate", From = from, To = now },
            SortDescending = true
        };

        var issuedTask = GetInvoicesAsync(issuedRequest, ct);
        var receivedTask = GetInvoicesAsync(receivedRequest, ct);

        await Task.WhenAll(issuedTask, receivedTask);

        var issued = issuedTask.Result.Invoices;
        var received = receivedTask.Result.Invoices;

        var stats = new InvoiceStatsResponse
        {
            IssuedCount = issued.Count,
            ReceivedCount = received.Count,
            IssuedNetTotal = issued.Sum(i => i.NetAmount ?? 0),
            IssuedGrossTotal = issued.Sum(i => i.GrossAmount ?? 0),
            ReceivedNetTotal = received.Sum(i => i.NetAmount ?? 0),
            ReceivedGrossTotal = received.Sum(i => i.GrossAmount ?? 0),
            PeriodFrom = from,
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
            var monthIssued = issued.Where(i => GetMonth(i) == month).ToList();
            var monthReceived = received.Where(i => GetMonth(i) == month).ToList();

            stats.Monthly.Add(new MonthlyStats
            {
                Month = month,
                IssuedCount = monthIssued.Count,
                ReceivedCount = monthReceived.Count,
                IssuedGross = monthIssued.Sum(i => i.GrossAmount ?? 0),
                ReceivedGross = monthReceived.Sum(i => i.GrossAmount ?? 0)
            });
        }

        var contractorCounts = received
            .Where(i => i.Seller?.Nip != null)
            .GroupBy(i => i.Seller!.Nip!)
            .OrderByDescending(g => g.Count())
            .Take(10)
            .ToDictionary(g => g.Key, g => g.Count());

        stats.TopContractors = contractorCounts;

        var allInvoices = issued.Concat(received).ToList();
        var currencyGroups = allInvoices
            .GroupBy(i => i.Currency ?? "PLN")
            .ToDictionary(
                g => g.Key,
                g => new CurrencyStats
                {
                    Count = g.Count(),
                    NetTotal = g.Sum(i => i.NetAmount ?? 0),
                    GrossTotal = g.Sum(i => i.GrossAmount ?? 0)
                });

        stats.ByCurrency = currencyGroups;

        return stats;
    }

    private static string GetMonth(InvoiceMetadata invoice)
    {
        var date = invoice.PermanentStorageDate
            ?? invoice.InvoicingDate
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
                using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
                request.Headers.Add("Authorization", $"Bearer {_session.AccessToken}");
                response = await client.SendAsync(request, ct);
                content = await response.Content.ReadAsStringAsync(ct);
                if (response.IsSuccessStatusCode) break;
            }

            if (response == null || !response.IsSuccessStatusCode)
                return new InvoiceDetailsResult { Success = false, Error = $"Nie można pobrać faktury z KSeF: {response?.StatusCode}" };

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
            return new SessionResult { Success = true, SessionReferenceNumber = _session.SessionReferenceNumber, ValidUntil = _session.SessionValidUntil };

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

            return new SessionResult { Success = true, SessionReferenceNumber = sessionResponse.ReferenceNumber, ValidUntil = sessionResponse.ValidUntil };
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

            using var request = new HttpRequestMessage(HttpMethod.Post, $"sessions/online/{_session.SessionReferenceNumber}/invoices");
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
                if (exception.TryGetProperty("exceptionDetailList", out var details) && details.ValueKind == JsonValueKind.Array && details.GetArrayLength() > 0)
                    if (details[0].TryGetProperty("exceptionDescription", out var desc)) return desc.GetString();
                if (exception.TryGetProperty("serviceCtx", out var ctx)) return ctx.GetString();
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
            using var request = new HttpRequestMessage(HttpMethod.Delete, $"sessions/online/{_session.SessionReferenceNumber}");
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
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"Błąd pobierania certyfikatów: {content}");
        var certificates = JsonSerializer.Deserialize<List<CertificateInfo>>(content, _jsonOptions)!;
        _session.SetCertificates(certificates);
        return certificates;
    }
}