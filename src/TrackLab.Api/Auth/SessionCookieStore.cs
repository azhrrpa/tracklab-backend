using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using TrackLab.Api.Configuration;
using TrackLab.Api.Upstream;

namespace TrackLab.Api.Auth;

/// <summary>The logged-in user's session. Lives only in encrypted, HttpOnly cookies.</summary>
/// <param name="RefreshToken">From data.token.refresh in the login response.</param>
/// <param name="UpstreamCookies">Cookies the assessment API set at login, sent back when refreshing.</param>
public sealed record UserSession(
    string Username,
    string DisplayName,
    string AccessToken,
    string? RefreshToken,
    string? UpstreamCookies);

/// <summary>
/// Stores the session in three cookies, each encrypted and signed with ASP.NET Core Data Protection:
/// the browser can't read them (HttpOnly) and can't tamper with them (any change fails to decrypt).
/// The cookies expire together with the refresh token, so a dead session doesn't linger.
/// </summary>
public sealed class SessionCookieStore(
    IDataProtectionProvider dataProtection,
    IOptions<SessionCookieOptions> options,
    TimeProvider time)
{
    public const string AccessTokenCookie = "tl_at";
    public const string RefreshCookie = "tl_rt";
    public const string UserCookie = "tl_user";

    /// <summary>Used when the refresh token has no readable expiry.</summary>
    private static readonly TimeSpan DefaultLifetime = TimeSpan.FromHours(12);

    private const string CachedSessionKey = "TrackLab.Session";

    private readonly IDataProtector _protector = dataProtection.CreateProtector("TrackLab.Session.v2");

    public UserSession? Read(HttpContext context)
    {
        if (context.Items.TryGetValue(CachedSessionKey, out var cached))
        {
            return cached as UserSession;
        }

        var session = ReadFromCookies(context.Request.Cookies);
        context.Items[CachedSessionKey] = session;
        return session;
    }

    public void Write(HttpContext context, UserSession session)
    {
        var expires = TokenExtractor.ReadExpiry(session.RefreshToken) ?? time.GetUtcNow().Add(DefaultLifetime);
        var cookieOptions = BuildCookieOptions(expires);

        var user = JsonSerializer.Serialize(new StoredUser(session.Username, session.DisplayName));
        var refresh = JsonSerializer.Serialize(new StoredRefresh(session.RefreshToken, session.UpstreamCookies));

        context.Response.Cookies.Append(UserCookie, _protector.Protect(user), cookieOptions);
        context.Response.Cookies.Append(AccessTokenCookie, _protector.Protect(session.AccessToken), cookieOptions);
        context.Response.Cookies.Append(RefreshCookie, _protector.Protect(refresh), cookieOptions);

        context.Items[CachedSessionKey] = session;
    }

    public void Clear(HttpContext context)
    {
        var cookieOptions = BuildCookieOptions(expires: null);
        context.Response.Cookies.Delete(UserCookie, cookieOptions);
        context.Response.Cookies.Delete(AccessTokenCookie, cookieOptions);
        context.Response.Cookies.Delete(RefreshCookie, cookieOptions);
        context.Items[CachedSessionKey] = null;
    }

    private UserSession? ReadFromCookies(IRequestCookieCollection cookies)
    {
        if (!cookies.TryGetValue(UserCookie, out var protectedUser)
            || !cookies.TryGetValue(AccessTokenCookie, out var protectedToken))
        {
            return null;
        }

        try
        {
            var user = JsonSerializer.Deserialize<StoredUser>(_protector.Unprotect(protectedUser));
            if (user is null || string.IsNullOrWhiteSpace(user.Username))
            {
                return null;
            }

            var accessToken = _protector.Unprotect(protectedToken);
            var refresh = cookies.TryGetValue(RefreshCookie, out var protectedRefresh)
                ? JsonSerializer.Deserialize<StoredRefresh>(_protector.Unprotect(protectedRefresh))
                : null;

            return new UserSession(user.Username, user.DisplayName, accessToken, refresh?.RefreshToken, refresh?.Cookies);
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException)
        {
            // Tampered, rotated keys, or cookies from another app: treat as logged out.
            return null;
        }
    }

    private CookieOptions BuildCookieOptions(DateTimeOffset? expires) => new()
    {
        HttpOnly = true,
        Secure = options.Value.Secure,
        SameSite = SameSiteMode.Strict,
        Path = "/",
        IsEssential = true,
        Expires = expires,
    };

    private sealed record StoredUser(string Username, string DisplayName);

    private sealed record StoredRefresh(string? RefreshToken, string? Cookies);
}
