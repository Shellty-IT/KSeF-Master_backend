// Services/KSeFSessionManager.cs
using KSeF.Backend.Models.Responses.Auth;
using KSeF.Backend.Models.Responses.Certificate;

namespace KSeF.Backend.Services.KSeF.Session;

public class KSeFSessionManager
{
    private readonly ILogger<KSeFSessionManager> _logger;

    private string? _nip;
    private string? _accessToken;
    private DateTime? _accessTokenExpiry;
    private string? _refreshToken;
    private DateTime? _refreshTokenExpiry;

    private string? _onlineSessionReference;
    private DateTime? _onlineSessionExpiry;
    private byte[]? _aesKey;
    private byte[]? _iv;

    private readonly object _lock = new();
    private List<CertificateInfo>? _cachedCertificates;

    public KSeFSessionManager(ILogger<KSeFSessionManager> logger)
    {
        _logger = logger;
    }

    public bool IsAuthenticated
    {
        get
        {
            lock (_lock)
            {
                return !string.IsNullOrEmpty(_accessToken) &&
                       _accessTokenExpiry.HasValue &&
                       _accessTokenExpiry.Value > DateTime.UtcNow;
            }
        }
    }

    public string? Nip
    {
        get { lock (_lock) { return _nip; } }
    }

    public string? AccessToken
    {
        get { lock (_lock) { return IsAuthenticated ? _accessToken : null; } }
    }

    public DateTime? AccessTokenExpiry
    {
        get { lock (_lock) { return _accessTokenExpiry; } }
    }

    public string? RefreshToken
    {
        get { lock (_lock) { return _refreshToken; } }
    }

    public byte[]? AesKey
    {
        get { lock (_lock) { return _aesKey; } }
    }

    public byte[]? Iv
    {
        get { lock (_lock) { return _iv; } }
    }

    public string? SessionReferenceNumber
    {
        get { lock (_lock) { return HasActiveOnlineSession ? _onlineSessionReference : null; } }
    }

    public DateTime? SessionValidUntil
    {
        get { lock (_lock) { return _onlineSessionExpiry; } }
    }

    public void SetAuthSession(string nip, TokenRedeemResponse tokens)
    {
        lock (_lock)
        {
            _nip = nip;
            _accessToken = tokens.AccessToken?.Token;
            _accessTokenExpiry = tokens.AccessToken?.ValidUntil;
            _refreshToken = tokens.RefreshToken?.Token;
            _refreshTokenExpiry = tokens.RefreshToken?.ValidUntil;

            _logger.LogInformation("Auth session set for NIP: {Nip}, AccessToken valid until: {Expiry}",
                nip, _accessTokenExpiry);
        }
    }

    public void SetAuthSessionFromStatus(string nip, AuthStatusResponse status)
    {
        lock (_lock)
        {
            _nip = nip;
            _accessToken = status.AccessToken?.Token;
            _accessTokenExpiry = status.AccessToken?.ValidUntil;
            _refreshToken = status.RefreshToken?.Token;
            _refreshTokenExpiry = status.RefreshToken?.ValidUntil;

            _logger.LogInformation("Auth session set from status for NIP: {Nip}", nip);
        }
    }

    public void ClearAuthSession()
    {
        lock (_lock)
        {
            var wasAuthenticated = IsAuthenticated;

            _nip = null;
            _accessToken = null;
            _accessTokenExpiry = null;
            _refreshToken = null;
            _refreshTokenExpiry = null;

            _onlineSessionReference = null;
            _onlineSessionExpiry = null;
            _aesKey = null;
            _iv = null;
            _cachedCertificates = null;

            if (wasAuthenticated)
                _logger.LogInformation("Auth session cleared");
        }
    }

    public bool NeedsTokenRefresh
    {
        get
        {
            lock (_lock)
            {
                if (!_accessTokenExpiry.HasValue) return false;
                var timeUntilExpiry = _accessTokenExpiry.Value - DateTime.UtcNow;
                return timeUntilExpiry.TotalMinutes < 10;
            }
        }
    }

    public void UpdateAccessToken(TokenRefreshResponse refreshResponse)
    {
        lock (_lock)
        {
            _accessToken = refreshResponse.AccessToken?.Token;
            _accessTokenExpiry = refreshResponse.AccessToken?.ValidUntil;

            _logger.LogInformation("Access token refreshed, valid until: {Expiry}", _accessTokenExpiry);
        }
    }

    public bool HasActiveOnlineSession
    {
        get
        {
            lock (_lock)
            {
                return !string.IsNullOrEmpty(_onlineSessionReference) &&
                       _onlineSessionExpiry.HasValue &&
                       _onlineSessionExpiry.Value > DateTime.UtcNow;
            }
        }
    }

    public string? OnlineSessionReference
    {
        get { lock (_lock) { return HasActiveOnlineSession ? _onlineSessionReference : null; } }
    }

    public void SetOnlineSession(string referenceNumber, DateTime validUntil, byte[] aesKey, byte[] iv)
    {
        lock (_lock)
        {
            _onlineSessionReference = referenceNumber;
            _onlineSessionExpiry = validUntil;
            _aesKey = aesKey;
            _iv = iv;

            _logger.LogInformation("Online session set: {Ref}, valid until: {Expiry}",
                referenceNumber, validUntil);
        }
    }

    public void ClearOnlineSession()
    {
        lock (_lock)
        {
            var hadSession = HasActiveOnlineSession;
            _onlineSessionReference = null;
            _onlineSessionExpiry = null;
            _aesKey = null;
            _iv = null;

            if (hadSession)
                _logger.LogInformation("Online session cleared");
        }
    }

    public object GetSessionInfo()
    {
        lock (_lock)
        {
            return new
            {
                isAuthenticated = IsAuthenticated,
                nip = _nip,
                accessTokenExpiry = _accessTokenExpiry,
                hasRefreshToken = !string.IsNullOrEmpty(_refreshToken),
                hasOnlineSession = HasActiveOnlineSession,
                onlineSessionExpiry = _onlineSessionExpiry
            };
        }
    }

    public List<CertificateInfo>? GetCachedCertificates()
    {
        lock (_lock) { return _cachedCertificates; }
    }

    public void SetCertificates(List<CertificateInfo> certificates)
    {
        lock (_lock)
        {
            _cachedCertificates = certificates;
            _logger.LogInformation("Cached {Count} certificates", certificates.Count);
        }
    }
}