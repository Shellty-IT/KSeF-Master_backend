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
        var currentFrom = request.DateRange.From;
        var hasMore = true;
        var iteration = 0;
        const int maxIterations = 100;
        var maxResults = request.MaxResults ?? int.MaxValue;

        _logger.LogInformation(
            "Pobieranie faktur (SubjectType: {Type}, od: {From}, do: {To})",
            request.SubjectType,
            request.DateRange.From.ToString("yyyy-MM-dd"),
            request.DateRange.To.ToString("yyyy-MM-dd"));

        while (hasMore && iteration < maxIterations && allInvoices.Count < maxResults)
        {
            iteration++;

            var pageRequest = new InvoiceQueryRequest
            {
                SubjectType = request.SubjectType,
                DateRange = new DateRangeFilter
                {
                    DateType = request.DateRange.DateType,
                    From = currentFrom,
                    To = request.DateRange.To
                },
                AmountFrom = request.AmountFrom,
                AmountTo = request.AmountTo,
                ContractorNip = request.ContractorNip,
                ContractorName = request.ContractorName,
                InvoiceNumber = request.InvoiceNumber,
                Currency = request.Currency
            };

            using (var httpRequest = new HttpRequestMessage(HttpMethod.Post, "invoices/query/metadata"))
            {
                httpRequest.Headers.Add("Authorization", $"Bearer {_session.AccessToken}");
                httpRequest.Content = new StringContent(
                    JsonSerializer.Serialize(pageRequest, _jsonOptions),
                    Encoding.UTF8,
                    "application/json");

                var response = await client.SendAsync(httpRequest, ct);
                var content = await response.Content.ReadAsStringAsync(ct);

                if (!response.IsSuccessStatusCode)
                    throw new HttpRequestException($"Błąd pobierania faktur: {response.StatusCode} - {content}");

                var result = JsonSerializer.Deserialize<InvoiceQueryResponse>(content, _jsonOptions)!;

                var newCount = 0;
                foreach (var invoice in result.Invoices)
                {
                    if (seenIds.Add(invoice.KsefNumber))
                    {
                        allInvoices.Add(invoice);
                        newCount++;

                        if (allInvoices.Count >= maxResults)
                            break;
                    }
                }

                _logger.LogInformation(
                    "Strona {Page}: +{New} nowych (łącznie: {Total}, hasMore: {HasMore})",
                    iteration, newCount, allInvoices.Count, result.HasMore);

                hasMore = result.HasMore;

                if (hasMore)
                {
                    if (result.PermanentStorageHwmDate.HasValue)
                    {
                        var newFrom = result.PermanentStorageHwmDate.Value;
                        if (newFrom <= currentFrom && newCount == 0)
                        {
                            _logger.LogWarning("Kursor nie postępuje, przerywanie pętli paginacji");
                            break;
                        }
                        currentFrom = newFrom;
                    }
                    else
                    {
                        _logger.LogWarning("Brak daty kursora mimo hasMore=true, przerywanie");
                        break;
                    }
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

    public async Task<InvoiceStatsResponse> GetInvoiceStatsAsync(int months = 3, CancellationToken ct = default)
    {
        EnsureAuthenticated();

        var now = DateTime.UtcNow;
        var from = now.AddMonths(-months);

        var issuedRequest = new InvoiceQueryRequest
        {
            SubjectType = "Subject1",
            DateRange = new DateRangeFilter { DateType = "PermanentStorage", From = from, To = now },
            SortDescending = true
        };

        var receivedRequest = new InvoiceQueryRequest
        {
            SubjectType = "Subject2",
            DateRange = new DateRangeFilter { DateType = "PermanentStorage", From = from, To = now },
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

        _logger.LogInformation("Pobieranie szczegółów faktury: {KsefNumber}", ksefNumber);

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
                _logger.LogDebug("Próbuję endpoint: {Endpoint}", endpoint);

                using (var request = new HttpRequestMessage(HttpMethod.Get, endpoint))
                {
                    request.Headers.Add("Authorization", $"Bearer {_session.AccessToken}");
                    response = await client.SendAsync(request, ct);
                    content = await response.Content.ReadAsStringAsync(ct);
                }

                _logger.LogDebug("Endpoint {Endpoint} - Status: {Status}", endpoint, response.StatusCode);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Sukces z endpoint: {Endpoint}", endpoint);
                    break;
                }
            }

            if (response == null || !response.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "Błąd pobierania faktury ze wszystkich endpointów. Ostatni status: {Status}",
                    response?.StatusCode);
                return new InvoiceDetailsResult
                {
                    Success = false,
                    Error = $"Nie można pobrać faktury z KSeF: {response?.StatusCode}"
                };
            }

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
        if (string.IsNullOrEmpty(invoiceBody))
            return null;

        try
        {
            var bytes = Convert.FromBase64String(invoiceBody);
            return Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            return invoiceBody;
        }
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

        var wiersze = doc.Descendants(ns + "FaWiersz");
        foreach (var wiersz in wiersze)
        {
            decimal.TryParse(wiersz.Element(ns + "P_8B")?.Value, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var qty);
            decimal.TryParse(wiersz.Element(ns + "P_9A")?.Value, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var price);
            decimal.TryParse(wiersz.Element(ns + "P_11")?.Value, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var netValue);
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
            decimal.TryParse(fa.Element(ns + "P_13_1")?.Value, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var net);
            decimal.TryParse(fa.Element(ns + "P_14_1")?.Value, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var vat);
            decimal.TryParse(fa.Element(ns + "P_15")?.Value, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var gross);

            result.NetTotal = net;
            result.VatTotal = vat;
            result.GrossTotal = gross;
        }

        return result;
    }

    private decimal CalculateVatFromRate(decimal net, string vatRate)
    {
        if (decimal.TryParse(vatRate, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var rate))
            return Math.Round(net * rate / 100, 2);

        return vatRate.ToLower() switch
        {
            "zw" or "np" or "oo" => 0,
            _ => Math.Round(net * 0.23m, 2)
        };
    }

    public async Task<SessionResult> OpenOnlineSessionAsync(CancellationToken ct = default)
    {
        EnsureAuthenticated();
        await _authService.RefreshTokenIfNeededAsync(ct);

        if (_session.HasActiveOnlineSession)
        {
            _logger.LogInformation("Używam istniejącej sesji: {Ref}", _session.SessionReferenceNumber);
            return new SessionResult
            {
                Success = true,
                SessionReferenceNumber = _session.SessionReferenceNumber,
                ValidUntil = _session.SessionValidUntil
            };
        }

        try
        {
            _logger.LogInformation("═══════════════════════════════════════════════════════════════");
            _logger.LogInformation("  OTWIERANIE SESJI INTERAKTYWNEJ");
            _logger.LogInformation("═══════════════════════════════════════════════════════════════");

            var client = CreateClient();

            _logger.LogInformation("Krok 1: Pobieranie certyfikatu SymmetricKeyEncryption...");
            var certificates = _session.GetCachedCertificates()
                ?? await GetCertificatesAsync(client, ct);

            var symCert = certificates.First(c => c.Usage?.Contains("SymmetricKeyEncryption") == true);
            _logger.LogInformation("  ✓ Certyfikat pobrany");

            _logger.LogInformation("Krok 2: Generowanie klucza AES-256 i IV...");
            var (aesKey, iv) = _cryptoService.GenerateAesKeyAndIv();
            _logger.LogInformation("  ✓ Klucz AES: {KeyLen} bajtów, IV: {IvLen} bajtów", aesKey.Length, iv.Length);

            _logger.LogInformation("Krok 3: Szyfrowanie klucza AES (RSA-OAEP SHA-256)...");
            var encryptedSymmetricKey = _cryptoService.EncryptAesKey(aesKey, symCert.Certificate);
            var ivBase64 = Convert.ToBase64String(iv);
            _logger.LogInformation("  ✓ Klucz zaszyfrowany");

            _logger.LogInformation("Krok 4: POST /sessions/online");
            var requestBody = new
            {
                formCode = new
                {
                    systemCode = "FA (3)",
                    schemaVersion = "1-0E",
                    value = "FA"
                },
                encryption = new
                {
                    encryptedSymmetricKey,
                    initializationVector = ivBase64
                }
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, "sessions/online");
            request.Headers.Add("Authorization", $"Bearer {_session.AccessToken}");
            request.Content = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json");

            var response = await client.SendAsync(request, ct);
            var content = await response.Content.ReadAsStringAsync(ct);

            _logger.LogInformation("  Response: {Status}", response.StatusCode);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("  ✗ Błąd: {Content}", content);
                return new SessionResult { Success = false, Error = content };
            }

            var sessionResponse = JsonSerializer.Deserialize<OpenSessionResponse>(content, _jsonOptions)!;

            _session.SetOnlineSession(
                sessionResponse.ReferenceNumber,
                sessionResponse.ValidUntil,
                aesKey,
                iv);

            _logger.LogInformation("═══════════════════════════════════════════════════════════════");
            _logger.LogInformation("  ✅ SESJA OTWARTA!");
            _logger.LogInformation("  ReferenceNumber: {Ref}", sessionResponse.ReferenceNumber);
            _logger.LogInformation("  ValidUntil: {Until}", sessionResponse.ValidUntil);
            _logger.LogInformation("═══════════════════════════════════════════════════════════════");

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
        {
            _logger.LogWarning(
                "⚠️ NIP sprzedawcy ({SellerNip}) różni się od NIP sesji ({SessionNip}). Automatycznie poprawiam!",
                invoiceData.Seller.Nip, sessionNip);
            invoiceData.Seller.Nip = sessionNip;
        }

        if (!_session.HasActiveOnlineSession)
        {
            var sessionResult = await OpenOnlineSessionAsync(ct);
            if (!sessionResult.Success)
                return new SendInvoiceResult { Success = false, Error = $"Nie można otworzyć sesji: {sessionResult.Error}" };
        }

        try
        {
            _logger.LogInformation("═══════════════════════════════════════════════════════════════");
            _logger.LogInformation("  WYSYŁANIE FAKTURY: {Number}", invoiceData.InvoiceNumber);
            _logger.LogInformation("  NIP Sprzedawcy: {SellerNip}", invoiceData.Seller.Nip);
            _logger.LogInformation("  NIP Nabywcy: {BuyerNip}", invoiceData.Buyer.Nip);
            _logger.LogInformation("  NIP Sesji: {SessionNip}", sessionNip);
            _logger.LogInformation("═══════════════════════════════════════════════════════════════");

            var client = CreateClient();
            var aesKey = _session.AesKey!;
            var iv = _session.Iv!;
            var sessionRef = _session.SessionReferenceNumber!;

            _logger.LogInformation("Krok 1: Generowanie XML faktury...");
            var invoiceXml = _xmlGenerator.GenerateInvoiceXml(invoiceData, sessionNip);

            _logger.LogDebug("════════════════════════════════════════════════════════════════");
            _logger.LogDebug("WYGENEROWANY XML:");
            _logger.LogDebug("{Xml}", invoiceXml);
            _logger.LogDebug("════════════════════════════════════════════════════════════════");

            var invoiceBytes = new UTF8Encoding(false).GetBytes(invoiceXml);
            _logger.LogInformation("  ✓ XML wygenerowany: {Size} bajtów", invoiceBytes.Length);

            _logger.LogInformation("Krok 2: Obliczanie hash SHA-256 faktury...");
            var invoiceHash = _cryptoService.ComputeSha256Base64(invoiceBytes);
            _logger.LogInformation("  ✓ Hash: {Hash}", invoiceHash);

            _logger.LogInformation("Krok 3: Szyfrowanie faktury (AES-256-CBC)...");
            var encryptedInvoice = _cryptoService.EncryptInvoiceXml(invoiceXml, aesKey, iv);
            var encryptedInvoiceHash = _cryptoService.ComputeSha256Base64(encryptedInvoice);
            var encryptedInvoiceBase64 = Convert.ToBase64String(encryptedInvoice);
            _logger.LogInformation("  ✓ Zaszyfrowano: {OrigSize} -> {EncSize} bajtów",
                invoiceBytes.Length, encryptedInvoice.Length);

            _logger.LogInformation("Krok 4: POST /sessions/online/{Ref}/invoices", sessionRef);
            var requestBody = new
            {
                invoiceHash,
                invoiceSize = invoiceBytes.Length,
                encryptedInvoiceHash,
                encryptedInvoiceSize = encryptedInvoice.Length,
                encryptedInvoiceContent = encryptedInvoiceBase64,
                offlineMode = false
            };

            _logger.LogDebug("Request body: {Body}", JsonSerializer.Serialize(requestBody, _jsonOptions));

            var url = $"sessions/online/{sessionRef}/invoices";

            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Add("Authorization", $"Bearer {_session.AccessToken}");
            request.Content = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json");

            var response = await client.SendAsync(request, ct);
            var content = await response.Content.ReadAsStringAsync(ct);

            _logger.LogInformation("  Response: {Status}", response.StatusCode);
            _logger.LogDebug("  Response Content: {Content}", content);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("════════════════════════════════════════════════════════════════");
                _logger.LogError("  ✗ BŁĄD WYSYŁKI FAKTURY!");
                _logger.LogError("  Status: {Status}", response.StatusCode);
                _logger.LogError("  Response: {Content}", content);
                _logger.LogError("════════════════════════════════════════════════════════════════");

                var errorMessage = ParseKsefError(content) ?? content;
                return new SendInvoiceResult { Success = false, Error = errorMessage };
            }

            var sendResponse = JsonSerializer.Deserialize<SendInvoiceApiResponse>(content, _jsonOptions);

            _logger.LogInformation("═══════════════════════════════════════════════════════════════");
            _logger.LogInformation("  ✅ FAKTURA WYSŁANA!");
            _logger.LogInformation("  ElementReferenceNumber: {Ref}", sendResponse?.ElementReferenceNumber);
            _logger.LogInformation("  ProcessingCode: {Code}", sendResponse?.ProcessingCode);
            _logger.LogInformation("  ProcessingDescription: {Desc}", sendResponse?.ProcessingDescription);
            _logger.LogInformation("═══════════════════════════════════════════════════════════════");

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
            using var doc = JsonDocument.Parse(responseContent);
            var root = doc.RootElement;

            if (root.TryGetProperty("exception", out var exception))
            {
                if (exception.TryGetProperty("exceptionDetailList", out var details) &&
                    details.ValueKind == JsonValueKind.Array &&
                    details.GetArrayLength() > 0)
                {
                    var firstDetail = details[0];
                    if (firstDetail.TryGetProperty("exceptionDescription", out var desc))
                        return desc.GetString();
                }

                if (exception.TryGetProperty("serviceCtx", out var ctx))
                    return ctx.GetString();
            }

            if (root.TryGetProperty("message", out var message))
                return message.GetString();

            if (root.TryGetProperty("error", out var error))
                return error.GetString();
        }
        catch
        {
        }

        return null;
    }

    public async Task<bool> CloseOnlineSessionAsync(CancellationToken ct = default)
    {
        if (!_session.HasActiveOnlineSession)
        {
            _logger.LogInformation("Brak aktywnej sesji do zamknięcia");
            return true;
        }

        try
        {
            var client = CreateClient();
            var sessionRef = _session.SessionReferenceNumber!;

            _logger.LogInformation("Zamykanie sesji: {Ref}", sessionRef);

            using var request = new HttpRequestMessage(HttpMethod.Delete, $"sessions/online/{sessionRef}");
            request.Headers.Add("Authorization", $"Bearer {_session.AccessToken}");

            var response = await client.SendAsync(request, ct);

            _session.ClearOnlineSession();

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("  ✓ Sesja zamknięta");
                return true;
            }
            else
            {
                var content = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("  ⚠ Błąd zamykania sesji: {Status} - {Content}", response.StatusCode, content);
                return false;
            }
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