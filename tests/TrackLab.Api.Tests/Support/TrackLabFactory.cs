using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using TrackLab.Api.Upstream;

namespace TrackLab.Api.Tests.Support;

/// <summary>
/// Starts the real backend in memory, with the assessment API replaced by <see cref="FakeUpstream"/>
/// and logs/audit files written to a temporary folder.
/// </summary>
public sealed class TrackLabFactory : WebApplicationFactory<Program>
{
    public const string AuthPath = "/api/v1/authentication";
    public const string FeaturePath = "/api/v1/feature";

    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), "tracklab-tests-" + Guid.NewGuid().ToString("N"));

    public FakeUpstream Upstream { get; } = new();

    public string AccessToken { get; } = Jwt.Create(DateTimeOffset.UtcNow.AddHours(1));

    public string RefreshToken { get; } = Jwt.Create(DateTimeOffset.UtcNow.AddHours(12));

    /// <summary>Use the API's DELETE endpoints instead of deactivating through PUT.</summary>
    public bool UseDeleteEndpoint { get; init; }

    public TrackLabFactory()
    {
        Upstream.On(HttpMethod.Post, $"{AuthPath}/auth/login", request =>
            request.Body.Contains("\"password\":\"correct-horse\"")
                ? FakeUpstream.Json(
                    HttpStatusCode.OK,
                    $$"""{"data":{"token":{"type":"Bearer","access":"{{AccessToken}}","refresh":"{{RefreshToken}}","expiresIn":3600},"username":"assessment.it"},"success":true,"message":"OK","code":200}""",
                    $"RefreshToken={RefreshToken}; path=/; httponly",
                    "sl-session=abc; path=/; secure; httponly")
                : FakeUpstream.Json(HttpStatusCode.Unauthorized, """{"data":null,"success":false,"message":"Invalid username or password","code":401}"""));

        Upstream.On(HttpMethod.Post, $"{AuthPath}/auth/logout", HttpStatusCode.OK, """{"success":true,"message":"OK","code":200}""");
        Upstream.On(HttpMethod.Get, $"{AuthPath}/auth/authorization", HttpStatusCode.OK,
            """{"data":{"moduleAccesses":[{"roleCode":"ASSESSMENT-USER","moduleCode":"API-TRACKER","read":true,"create":true,"update":true,"delete":true}],"userRoles":[{"roleCode":"ASSESSMENT-USER"}]},"success":true,"code":200}""");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("UpstreamApi:BaseUrl", "https://upstream.test");
        builder.UseSetting("UpstreamApi:AppId", "test-app-id");
        builder.UseSetting("UpstreamApi:AppKey", "test-app-key");
        builder.UseSetting("SessionCookies:Secure", "false");
        builder.UseSetting("UpstreamApi:UseDeleteEndpoint", UseDeleteEndpoint ? "true" : "false");
        builder.UseSetting("FileLogging:Directory", Path.Combine(_tempDirectory, "logs"));
        builder.UseSetting("AuditTrail:FilePath", Path.Combine(_tempDirectory, "audit-trail.jsonl"));

        builder.ConfigureTestServices(services =>
        {
            services.AddDataProtection().UseEphemeralDataProtectionProvider();
            // Keep the app's credential and logging handlers, but send the calls to the fake API.
            services.AddHttpClient<UpstreamApiClient>().ConfigurePrimaryHttpMessageHandler(() => Upstream);
        });
    }

    /// <summary>A client that keeps cookies between calls, like a browser.</summary>
    public HttpClient CreateBrowserClient() =>
        CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, AllowAutoRedirect = false });

    /// <summary>Logs in and returns a client that carries the session cookies and the CSRF header.</summary>
    public async Task<HttpClient> CreateLoggedInClientAsync()
    {
        var client = CreateBrowserClient();
        client.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");
        var response = await client.PostAsJsonAsync("/api/auth/login", new { username = "assessment.it", password = "correct-horse" });
        response.EnsureSuccessStatusCode();
        return client;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(_tempDirectory))
        {
            try
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
            catch (IOException)
            {
                // A log file may still be open on some platforms; leaving temp files is harmless.
            }
        }
    }
}
