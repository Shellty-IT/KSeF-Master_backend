using KSeF.Backend.Models.Responses;

namespace KSeF.Backend.Services;

/// <summary>
/// Przechowuje stan sesji KSeF w pamięci (Singleton)
/// </summary>
public class KSeFSessionManager
{
    private readonly object _lock = new();

    // Auth tokens
    private string? _accessToken;
    private DateTime? _accessTokenValidUntil;
    private string? _refreshToken;
    private DateTime? _refreshTokenValidUntil;
    private string? _nip;

    // Online session (do wysyłki faktur)
    private string? _sessionReferenceNumber;
    private DateTime? _sessionValidUntil;
    private byte[]? _aesKey;
    private byte[]? _iv;

    // Cached certificates
    private List<CertificateInfo>? _certificates;
    private DateTime? _certificatesCachedAt;

    #region Auth Session

    public bool IsAuthenticated
    {
        get
        {
            lock (_lock)
            {
                return !string.IsNullOrEmpty(_accessToken) &&
                       _accessTokenValidUntil.HasValue &&
                       _accessTokenValidUntil.Value > DateTime.UtcNow;
            }
        }
    }

    public string? AccessToken
    {
        get { lock (_lock) return _accessToken; }
    }

    public string? RefreshToken
    {
        get { lock (_lock) return _refreshToken; }
    }

    public string? Nip
    {
        get { lock (_lock) return _nip; }
    }

    public DateTime? AccessTokenValidUntil
    {
        get { lock (_lock) return _accessTokenValidUntil; }
    }

    public DateTime? RefreshTokenValidUntil
    {
        get { lock (_lock) return _refreshTokenValidUntil; }
    }

    public bool NeedsTokenRefresh
    {
        get
        {
            lock (_lock)
            {
                // Odśwież jeśli accessToken wygasa za mniej niż 2 minuty
                return _accessTokenValidUntil.HasValue &&
                       _accessTokenValidUntil.Value < DateTime.UtcNow.AddMinutes(2) &&
                       !string.IsNullOrEmpty(_refreshToken) &&
                       _refreshTokenValidUntil.HasValue &&
                       _refreshTokenValidUntil.Value > DateTime.UtcNow;
            }
        }
    }

    public void SetAuthSession(string nip, TokenRedeemResponse tokens)
    {
        lock (_lock)
        {
            _nip = nip;
            _accessToken = tokens.AccessToken?.Token;
            _accessTokenValidUntil = tokens.AccessToken?.ValidUntil;
            _refreshToken = tokens.RefreshToken?.Token;
            _refreshTokenValidUntil = tokens.RefreshToken?.ValidUntil;
        }
    }

    public void UpdateAccessToken(TokenRefreshResponse tokens)
    {
        lock (_lock)
        {
            _accessToken = tokens.AccessToken?.Token;
            _accessTokenValidUntil = tokens.AccessToken?.ValidUntil;
        }
    }

    public void ClearAuthSession()
    {
        lock (_lock)
        {
            _accessToken = null;
            _accessTokenValidUntil = null;
            _refreshToken = null;
            _refreshTokenValidUntil = null;
            _nip = null;
            ClearOnlineSessionInternal();
        }
    }

    #endregion

    #region Online Session (Invoice Sending)

    public bool HasActiveOnlineSession
    {
        get
        {
            lock (_lock)
            {
                return !string.IsNullOrEmpty(_sessionReferenceNumber) &&
                       _sessionValidUntil.HasValue &&
                       _sessionValidUntil.Value > DateTime.UtcNow &&
                       _aesKey != null &&
                       _iv != null;
            }
        }
    }

    public string? SessionReferenceNumber
    {
        get { lock (_lock) return _sessionReferenceNumber; }
    }

    public DateTime? SessionValidUntil
    {
        get { lock (_lock) return _sessionValidUntil; }
    }

    public byte[]? AesKey
    {
        get { lock (_lock) return _aesKey; }
    }

    public byte[]? Iv
    {
        get { lock (_lock) return _iv; }
    }

    public void SetOnlineSession(string referenceNumber, DateTime validUntil, byte[] aesKey, byte[] iv)
    {
        lock (_lock)
        {
            _sessionReferenceNumber = referenceNumber;
            _sessionValidUntil = validUntil;
            _aesKey = aesKey;
            _iv = iv;
        }
    }

    public void ClearOnlineSession()
    {
        lock (_lock)
        {
            ClearOnlineSessionInternal();
        }
    }

    private void ClearOnlineSessionInternal()
    {
        _sessionReferenceNumber = null;
        _sessionValidUntil = null;
        _aesKey = null;
        _iv = null;
    }

    #endregion

    #region Certificates Cache

    public List<CertificateInfo>? GetCachedCertificates()
    {
        lock (_lock)
        {
            // Cache ważny 1 godzinę
            if (_certificates != null &&
                _certificatesCachedAt.HasValue &&
                _certificatesCachedAt.Value > DateTime.UtcNow.AddHours(-1))
            {
                return _certificates;
            }
            return null;
        }
    }

    public void SetCertificates(List<CertificateInfo> certificates)
    {
        lock (_lock)
        {
            _certificates = certificates;
            _certificatesCachedAt = DateTime.UtcNow;
        }
    }

    #endregion

    #region Session Info

    public object GetSessionInfo()
    {
        lock (_lock)
        {
            return new
            {
                isAuthenticated = IsAuthenticated,
                nip = _nip,
                accessTokenValidUntil = _accessTokenValidUntil,
                refreshTokenValidUntil = _refreshTokenValidUntil,
                hasActiveOnlineSession = HasActiveOnlineSession,
                sessionReferenceNumber = _sessionReferenceNumber,
                sessionValidUntil = _sessionValidUntil
            };
        }
    }

    #endregion
}