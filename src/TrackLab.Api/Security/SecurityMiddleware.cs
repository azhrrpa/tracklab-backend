namespace TrackLab.Api.Security;

/// <summary>
/// CSRF protection for cookie-based auth: every state-changing request must carry
/// "X-Requested-With: XMLHttpRequest". A malicious site can't add that header to a cross-site
/// request without a CORS preflight, which this API rejects. SameSite=Strict cookies add a second layer.
/// </summary>
public sealed class CsrfHeaderMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Requested-With";
    public const string HeaderValue = "XMLHttpRequest";

    public async Task InvokeAsync(HttpContext context, IProblemDetailsService problemDetails)
    {
        var isUnsafe = !(HttpMethods.IsGet(context.Request.Method)
            || HttpMethods.IsHead(context.Request.Method)
            || HttpMethods.IsOptions(context.Request.Method));

        var hasHeader = string.Equals(context.Request.Headers[HeaderName], HeaderValue, StringComparison.OrdinalIgnoreCase);

        if (isUnsafe && !hasHeader)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await problemDetails.WriteAsync(new ProblemDetailsContext
            {
                HttpContext = context,
                ProblemDetails =
                {
                    Status = StatusCodes.Status403Forbidden,
                    Title = "Missing CSRF header",
                    Detail = $"State-changing requests must include the header '{HeaderName}: {HeaderValue}'.",
                },
            });
            return;
        }

        await next(context);
    }
}

/// <summary>Basic hardening headers for every response.</summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            headers.CacheControl = "no-store";
            return Task.CompletedTask;
        });

        return next(context);
    }
}
