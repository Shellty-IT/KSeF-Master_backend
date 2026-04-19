// Services/KSeFSessionManager.cs
using KSeF.Backend.Models.Responses;

namespace KSeF.Backend.Services;

public class KSeFSessionManager
{
    private readonly object _lock = new();

    private string? _accessToken;
    private DateTime? _accessTokenValidUntil;
    private string? _refreshToken;
    private DateTime? _refreshTokenValidUntil;
    private string? _nip;
    private string? _authMethod;

    private string? _sessionReferenceNumber;
    private DateTime? _sessionValidUntil;
    private byte[]? _aesKey;
    private byte[]? _iv;

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

    public string? AuthMethod
    {
        get { lock (_lock) return _authMethod; }
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
            _authMethod = "token";
            _accessToken = tokens.AccessToken?.Token;
            _accessTokenValidUntil = tokens.AccessToken?.ValidUntil;
            _refreshToken = tokens.RefreshToken?.Token;
            _refreshTokenValidUntil = tokens.RefreshToken?.ValidUntil;
        }
    }

    public void SetAuthSessionFromStatus(string nip, AuthStatusResponse status)
    {
        lock (_lock)
        {
            _nip = nip;
            _authMethod = "token";
            _accessToken = status.AccessToken?.Token;
            _accessTokenValidUntil = status.AccessToken?.ValidUntil;
            _refreshToken = status.RefreshToken?.Token;
            _refreshTokenValidUntil = status.RefreshToken?.ValidUntil;
        }
    }

    public void SetAuthSessionDirect(
        string nip,
        string accessToken,
        DateTime? accessTokenValidUntil,
        DateTime? refreshTokenValidUntil)
    {
        lock (_lock)
        {
            _nip = nip;
            _authMethod = "token";
            _accessToken = accessToken;
            _accessTokenValidUntil = accessTokenValidUntil ?? DateTime.UtcNow.AddMinutes(15);
            _refreshToken = null;
            _refreshTokenValidUntil = refreshTokenValidUntil;
        }
    }

    public void SetCertificateSession(string nip, string sessionToken, DateTime expiresAt)
    {
        lock (_lock)
        {
            _nip = nip;
            _authMethod = "certificate";
            _accessToken = sessionToken;
            _accessTokenValidUntil = expiresAt;
            _refreshToken = null;
            _refreshTokenValidUntil = null;
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
            _authMethod = null;
            ClearOnlineSessionInternal();
        }
    }

    #endregion

    #region Online Session

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
                authMethod = _authMethod,
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