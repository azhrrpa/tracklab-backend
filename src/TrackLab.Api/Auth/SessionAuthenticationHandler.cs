using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace TrackLab.Api.Auth;

public static class SessionAuthenticationDefaults
{
    public const string Scheme = "TrackLabSession";
    public const string DisplayNameClaim = "display_name";
}

/// <summary>
/// Plugs the session cookies into ASP.NET Core authentication, so controllers can use [Authorize]
/// and User.Identity.Name like any other auth scheme.
/// </summary>
public sealed class SessionAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    SessionCookieStore sessions,
    IProblemDetailsService problemDetails)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var session = sessions.Read(Context);
        if (session is null)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.Name, session.Username),
                new Claim(SessionAuthenticationDefaults.DisplayNameClaim, session.DisplayName),
            ],
            SessionAuthenticationDefaults.Scheme);

        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SessionAuthenticationDefaults.Scheme);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    /// <summary>An API answers 401 with JSON instead of redirecting to a login page.</summary>
    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        await problemDetails.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = Context,
            ProblemDetails =
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "Not logged in",
                Detail = "Please log in to continue.",
            },
        });
    }

    protected override async Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        await problemDetails.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = Context,
            ProblemDetails =
            {
                Status = StatusCodes.Status403Forbidden,
                Title = "Forbidden",
                Detail = "You do not have access to this resource.",
            },
        });
    }
}
