using System.Text.RegularExpressions;

namespace TrackLab.Api.Logging;

/// <summary>
/// Gives every request an id (from the X-Correlation-Id header, or a new one), returns it in the
/// response, and attaches it to every log line written while handling the request. One id then
/// ties together the frontend error, the backend log and the upstream call.
/// </summary>
public sealed partial class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    public const string HeaderName = "X-Correlation-Id";

    public async Task InvokeAsync(HttpContext context)
    {
        var incoming = context.Request.Headers[HeaderName].ToString();
        var correlationId = IsValid(incoming) ? incoming : Guid.NewGuid().ToString("N");

        context.TraceIdentifier = correlationId;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            await next(context);
        }
    }

    /// <summary>Only short, plain ids are accepted so the header can't be used to inject text into logs.</summary>
    public static bool IsValid(string? value) => !string.IsNullOrEmpty(value) && SafeId().IsMatch(value);

    [GeneratedRegex("^[A-Za-z0-9-]{8,64}$")]
    private static partial Regex SafeId();
}
