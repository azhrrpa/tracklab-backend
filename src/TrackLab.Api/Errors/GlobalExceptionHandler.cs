using Microsoft.AspNetCore.Diagnostics;

namespace TrackLab.Api.Errors;

/// <summary>
/// Turns unhandled exceptions into ProblemDetails JSON. Users get a short message plus a traceId they
/// can quote; the full exception goes to the log under the same id.
/// </summary>
public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger, IProblemDetailsService problemDetails)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title, detail) = exception switch
        {
            TaskCanceledException when !context.RequestAborted.IsCancellationRequested =>
                (StatusCodes.Status504GatewayTimeout, "Upstream timeout", "The assessment API took too long to respond. Please try again."),
            HttpRequestException =>
                (StatusCodes.Status502BadGateway, "Upstream unavailable", "The assessment API could not be reached. Please try again later."),
            _ =>
                (StatusCodes.Status500InternalServerError, "Unexpected error", "Something went wrong on our side. Please try again."),
        };

        if (status == StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Unhandled exception for {Method} {Path}", context.Request.Method, context.Request.Path);
        }
        else
        {
            logger.LogWarning(exception, "Upstream problem for {Method} {Path}: {Title}", context.Request.Method, context.Request.Path, title);
        }

        context.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            Exception = exception,
            ProblemDetails =
            {
                Status = status,
                Title = title,
                Detail = detail,
            },
        });
    }
}
