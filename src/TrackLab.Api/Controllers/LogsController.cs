using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TrackLab.Api.Contracts;
using TrackLab.Api.Logging;
using TrackLab.Api.Security;

namespace TrackLab.Api.Controllers;

/// <summary>
/// The logging service: the frontend reports its errors here, and the log viewer reads entries back.
/// </summary>
[ApiController]
[Route("api/logs")]
[Produces("application/json")]
public sealed class LogsController(LogFileReader reader, ILoggerFactory loggerFactory) : ControllerBase
{
    private readonly ILogger _frontendLogger = loggerFactory.CreateLogger("TrackLab.Frontend");

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<LogRecord>>> Get([FromQuery] LogQuery query, CancellationToken cancellationToken) =>
        Ok(await reader.ReadAsync(query, cancellationToken));

    /// <summary>Accepts an error or event from the browser. Anonymous so errors on the login page are captured too.</summary>
    [HttpPost("client")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.ClientLogs)]
    public IActionResult PostClientLog(ClientLogRequest body)
    {
        var level = body.Level.ToLowerInvariant() switch
        {
            "error" => LogLevel.Error,
            "warning" => LogLevel.Warning,
            _ => LogLevel.Information,
        };

        var context = body.Context is null
            ? string.Empty
            : string.Join(", ", body.Context.Select(entry => $"{entry.Key}={entry.Value}"));

        _frontendLogger.Log(
            level,
            "Frontend {ClientLevel}: {ClientMessage} at {ClientUrl} for {User} {ClientContext} {ClientStack}",
            body.Level,
            Sanitize(body.Message),
            Sanitize(body.Url),
            User.Identity?.Name ?? "anonymous",
            Sanitize(context),
            Sanitize(body.Stack));

        return Accepted();
    }

    /// <summary>Strips line breaks so a client can't forge extra lines in plain-text log viewers.</summary>
    private static string Sanitize(string? value) =>
        value is null ? string.Empty : value.Replace('\r', ' ').Replace('\n', ' ');
}
