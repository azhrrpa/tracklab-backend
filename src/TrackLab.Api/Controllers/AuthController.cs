using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using TrackLab.Api.Auth;
using TrackLab.Api.Configuration;
using TrackLab.Api.Contracts;
using TrackLab.Api.Security;
using TrackLab.Api.Upstream;

namespace TrackLab.Api.Controllers;

/// <summary>
/// Login, logout and "who am I". The access and refresh tokens from the assessment API are kept in
/// encrypted HttpOnly cookies; the browser never sees them.
/// </summary>
[Route("api/auth")]
public sealed class AuthController(
    UpstreamApiClient client,
    UpstreamGateway gateway,
    SessionCookieStore sessions,
    IOptions<UpstreamApiOptions> options,
    ILogger<AuthController> logger)
    : GatewayControllerBase(gateway, logger)
{
    private readonly UpstreamApiOptions _options = options.Value;

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    public async Task<IActionResult> Login(LoginRequest body, CancellationToken cancellationToken)
    {
        var username = body.Username.Trim();
        var response = await client.SendAsync(
            new UpstreamRequest(HttpMethod.Post, UpstreamArea.Auth, "auth/login")
            {
                Body = new
                {
                    appCode = _options.AppCode,
                    moduleCode = _options.ModuleCode,
                    user = username,
                    password = body.Password,
                },
            },
            cancellationToken);

        if (!response.IsSuccess)
        {
            logger.LogWarning("Login failed for {User}: upstream status {StatusCode}", username, response.StatusCode);
            return LoginFailure(response);
        }

        var accessToken = TokenExtractor.FindAccessToken(response.Body)
            ?? TokenExtractor.FindAccessTokenInCookies(response.SetCookies);

        if (string.IsNullOrEmpty(accessToken))
        {
            logger.LogError("Login for {User} succeeded upstream, but no access token was found in the response", username);
            return Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: "Login unavailable",
                detail: "The authentication service returned an unexpected response.");
        }

        var displayName = TokenExtractor.ReadDisplayName(accessToken)
            ?? TokenExtractor.FindString(response.Body, "fullName", "name", "displayName", "userName")
            ?? username;

        var session = new UserSession(
            username,
            displayName,
            accessToken,
            TokenExtractor.FindRefreshToken(response.Body),
            TokenExtractor.MergeCookieJar(existingJar: null, response.SetCookies));

        sessions.Write(HttpContext, session);

        // The API only accepts the new token on data endpoints after /auth/authorization was called.
        var authorization = await Gateway.ActivateAsync(accessToken, cancellationToken);
        logger.LogInformation("User {User} logged in", username);

        return Ok(new CurrentUserResponse(
            session.Username,
            session.DisplayName,
            authorization is { IsSuccess: true } ? TryParseJson(authorization.Body) : null));
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        var session = sessions.Read(HttpContext);
        if (session is not null)
        {
            try
            {
                await client.SendAsync(
                    new UpstreamRequest(HttpMethod.Post, UpstreamArea.Auth, "auth/logout")
                    {
                        AccessToken = session.AccessToken,
                        Cookie = session.UpstreamCookies,
                        Body = new { appCode = _options.AppCode, moduleCode = _options.ModuleCode, user = session.Username },
                    },
                    cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                // The local session is cleared either way; the user should not be stuck logged in.
                logger.LogWarning(ex, "Upstream logout failed for {User}; clearing the local session anyway", session.Username);
            }

            logger.LogInformation("User {User} logged out", session.Username);
        }

        sessions.Clear(HttpContext);
        return NoContent();
    }

    /// <summary>The current user plus their roles and modules from /auth/authorization.</summary>
    [HttpGet("me")]
    public async Task<IActionResult> Me([FromQuery] bool nested = true, CancellationToken cancellationToken = default)
    {
        var response = await Gateway.SendAsUserAsync(
            HttpContext,
            new UpstreamRequest(HttpMethod.Get, UpstreamArea.Auth, $"auth/authorization?nested={(nested ? "true" : "false")}"),
            cancellationToken);

        var session = sessions.Read(HttpContext);
        if (response.StatusCode == StatusCodes.Status401Unauthorized || session is null)
        {
            return ToActionResult(UpstreamResponse.Unauthenticated());
        }

        var authorization = response.IsSuccess ? TryParseJson(response.Body) : null;
        if (!response.IsSuccess)
        {
            logger.LogWarning("Could not load authorization for {User}: upstream status {StatusCode}", session.Username, response.StatusCode);
        }

        return Ok(new CurrentUserResponse(session.Username, session.DisplayName, authorization));
    }

    /// <summary>Refreshes the access token now. Normally not needed: expired tokens are refreshed automatically.</summary>
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(CancellationToken cancellationToken)
    {
        var session = sessions.Read(HttpContext);
        var refreshed = session is null ? null : await Gateway.TryRefreshAsync(HttpContext, session, cancellationToken);
        if (refreshed is null)
        {
            sessions.Clear(HttpContext);
            return ToActionResult(UpstreamResponse.Unauthenticated());
        }

        return NoContent();
    }

    private IActionResult LoginFailure(UpstreamResponse response)
    {
        if (response.StatusCode == StatusCodes.Status429TooManyRequests)
        {
            return Problem(statusCode: StatusCodes.Status429TooManyRequests, title: "Too many attempts", detail: "Please wait a minute and try again.");
        }

        if (response.StatusCode >= 500)
        {
            return Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: "Login unavailable",
                detail: "The authentication service is not available right now. Please try again.");
        }

        var upstreamMessage = TokenExtractor.FindString(response.Body, "message", "errorMessage", "error", "title");
        return Problem(
            statusCode: StatusCodes.Status401Unauthorized,
            title: "Login failed",
            detail: string.IsNullOrWhiteSpace(upstreamMessage) ? "Incorrect username or password." : upstreamMessage);
    }
}
