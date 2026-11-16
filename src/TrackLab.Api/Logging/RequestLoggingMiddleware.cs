using System.Diagnostics;

namespace TrackLab.Api.Logging;

/// <summary>
/// Writes one log line per HTTP request: method, path, status, duration and user.
/// Request bodies are not logged because they can contain passwords.
/// </summary>
public sealed class RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            await next(context);
        }
        finally
        {
            stopwatch.Stop();
            var status = context.Response.StatusCode;
            var level = status >= 500 ? LogLevel.Error : status >= 400 ? LogLevel.Warning : LogLevel.Information;

            logger.Log(
                level,
                "HTTP {Method} {Path}{QueryString} responded {StatusCode} in {ElapsedMs} ms for {User} from {ClientIp}",
                context.Request.Method,
                context.Request.Path.Value,
                context.Request.QueryString.Value,
                status,
                stopwatch.ElapsedMilliseconds,
                context.User.Identity?.Name ?? "anonymous",
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown");
        }
    }
}
