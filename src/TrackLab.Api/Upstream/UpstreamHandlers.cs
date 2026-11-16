using System.Diagnostics;
using Microsoft.Extensions.Options;
using TrackLab.Api.Configuration;

namespace TrackLab.Api.Upstream;

/// <summary>
/// Adds the app_id / app_key headers to every upstream call.
/// Keeping them here means the secrets never reach the browser.
/// </summary>
public sealed class AppCredentialsHandler(IOptions<UpstreamApiOptions> options) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Remove("app_id");
        request.Headers.Remove("app_key");
        request.Headers.TryAddWithoutValidation("app_id", options.Value.AppId);
        request.Headers.TryAddWithoutValidation("app_key", options.Value.AppKey);
        return base.SendAsync(request, cancellationToken);
    }
}

/// <summary>
/// Logs every upstream call with its status and duration. Headers and bodies are not logged,
/// because they contain tokens and passwords.
/// </summary>
public sealed class UpstreamLoggingHandler(ILogger<UpstreamLoggingHandler> logger) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var path = request.RequestUri?.AbsolutePath ?? "(unknown)";

        try
        {
            var response = await base.SendAsync(request, cancellationToken);
            stopwatch.Stop();

            var level = (int)response.StatusCode >= 500 ? LogLevel.Error
                : (int)response.StatusCode >= 400 ? LogLevel.Warning
                : LogLevel.Information;

            logger.Log(
                level,
                "Upstream {UpstreamMethod} {UpstreamPath} responded {UpstreamStatusCode} in {UpstreamElapsedMs} ms",
                request.Method.Method,
                path,
                (int)response.StatusCode,
                stopwatch.ElapsedMilliseconds);

            return response;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            stopwatch.Stop();
            logger.LogError(
                ex,
                "Upstream {UpstreamMethod} {UpstreamPath} failed after {UpstreamElapsedMs} ms",
                request.Method.Method,
                path,
                stopwatch.ElapsedMilliseconds);
            throw;
        }
    }
}
