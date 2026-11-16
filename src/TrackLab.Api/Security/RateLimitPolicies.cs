using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace TrackLab.Api.Security;

public static class RateLimitPolicies
{
    public const string Login = "login";
    public const string ClientLogs = "client-logs";

    /// <summary>Per-IP limits: slows down password guessing and stops the log endpoint being flooded.</summary>
    public static void Configure(RateLimiterOptions options)
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

        options.AddPolicy(Login, context => RateLimitPartition.GetFixedWindowLimiter(
            ClientKey(context),
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));

        options.AddPolicy(ClientLogs, context => RateLimitPartition.GetFixedWindowLimiter(
            ClientKey(context),
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 60, Window = TimeSpan.FromMinutes(1) }));
    }

    private static string ClientKey(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
