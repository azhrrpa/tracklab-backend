using Microsoft.Extensions.Options;
using TrackLab.Api.Auth;
using TrackLab.Api.Configuration;

namespace TrackLab.Api.Upstream;

/// <summary>
/// Sends requests to the assessment API on behalf of the logged-in user.
///
/// Two behaviours of the API shape this class (both confirmed by hand against the real API):
/// 1. A token only works on the feature endpoints after GET /auth/authorization has been called with it
///    ("activation": the API loads the user's roles). Until then every call answers 401.
/// 2. Access tokens last one hour; a new one comes from /auth/token/refresh with the RefreshToken cookie.
///
/// So on a 401 the gateway first re-activates the current token and retries; if that still fails it
/// refreshes the token (and activates the new one) and retries once more.
/// </summary>
public sealed class UpstreamGateway(
    UpstreamApiClient client,
    SessionCookieStore sessions,
    TimeProvider time,
    IOptions<UpstreamApiOptions> options,
    ILogger<UpstreamGateway> logger)
{
    public const string RefreshPath = "auth/token/refresh?refreshAuthorization=true";
    public const string AuthorizationPath = "auth/authorization?nested=true";

    /// <summary>Refresh this long before the access token's "exp", so a request never races the expiry.</summary>
    private static readonly TimeSpan RefreshMargin = TimeSpan.FromSeconds(60);

    public async Task<UpstreamResponse> SendAsUserAsync(HttpContext context, UpstreamRequest request, CancellationToken cancellationToken)
    {
        var session = sessions.Read(context);
        if (session is null)
        {
            return UpstreamResponse.Unauthenticated();
        }

        if (IsAboutToExpire(session.AccessToken))
        {
            session = await TryRefreshAsync(context, session, cancellationToken) ?? session;
        }

        var response = await client.SendAsync(request with { AccessToken = session.AccessToken }, cancellationToken);
        if (response.StatusCode != StatusCodes.Status401Unauthorized)
        {
            return response;
        }

        logger.LogInformation("Access token rejected for {User}; re-activating it", session.Username);
        if (await ActivateAsync(session.AccessToken, cancellationToken) is { IsSuccess: true })
        {
            response = await client.SendAsync(request with { AccessToken = session.AccessToken }, cancellationToken);
            if (response.StatusCode != StatusCodes.Status401Unauthorized)
            {
                return response;
            }
        }

        logger.LogInformation("Access token still rejected for {User}; refreshing it", session.Username);
        var refreshed = await TryRefreshAsync(context, session, cancellationToken);
        if (refreshed is null)
        {
            sessions.Clear(context);
            return UpstreamResponse.Unauthenticated();
        }

        return await client.SendAsync(request with { AccessToken = refreshed.AccessToken }, cancellationToken);
    }

    public async Task<UserSession?> TryRefreshAsync(HttpContext context, UserSession session, CancellationToken cancellationToken)
    {
        // The API reads the refresh token from its "RefreshToken" cookie (set at login).
        var refreshRequest = new UpstreamRequest(HttpMethod.Get, UpstreamArea.Auth, RefreshPath)
        {
            AccessToken = session.AccessToken,
            Cookie = TokenExtractor.EnsureCookie(session.UpstreamCookies, options.Value.RefreshCookieName, session.RefreshToken),
        };

        var response = await client.SendAsync(refreshRequest, cancellationToken);
        if (!response.IsSuccess)
        {
            logger.LogWarning("Token refresh failed for {User} with status {StatusCode}", session.Username, response.StatusCode);
            return null;
        }

        var newToken = TokenExtractor.FindAccessToken(response.Body)
            ?? TokenExtractor.FindAccessTokenInCookies(response.SetCookies);

        if (string.IsNullOrEmpty(newToken))
        {
            logger.LogWarning("Token refresh for {User} succeeded but no access token was found in the response", session.Username);
            return null;
        }

        var updated = session with
        {
            AccessToken = newToken,
            RefreshToken = TokenExtractor.FindRefreshToken(response.Body) ?? session.RefreshToken,
            UpstreamCookies = TokenExtractor.MergeCookieJar(session.UpstreamCookies, response.SetCookies),
        };

        sessions.Write(context, updated);
        await ActivateAsync(newToken, cancellationToken);
        logger.LogInformation("Access token refreshed for {User}", session.Username);
        return updated;
    }

    /// <summary>
    /// Calls GET /auth/authorization with the token, which makes the API accept it on the feature
    /// endpoints. Returns the response (roles and module access) so callers can reuse it.
    /// </summary>
    public async Task<UpstreamResponse?> ActivateAsync(string accessToken, CancellationToken cancellationToken)
    {
        var response = await client.SendAsync(
            new UpstreamRequest(HttpMethod.Get, UpstreamArea.Auth, AuthorizationPath) { AccessToken = accessToken },
            cancellationToken);

        if (!response.IsSuccess)
        {
            logger.LogWarning("Token activation (GET /auth/authorization) failed with status {StatusCode}", response.StatusCode);
        }

        return response;
    }

    private bool IsAboutToExpire(string accessToken) =>
        TokenExtractor.ReadExpiry(accessToken) is { } expiry && expiry - RefreshMargin <= time.GetUtcNow();
}
