using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using TrackLab.Api.Audit;
using TrackLab.Api.Auth;
using TrackLab.Api.Configuration;
using TrackLab.Api.Errors;
using TrackLab.Api.Logging;
using TrackLab.Api.Security;
using TrackLab.Api.Upstream;

// Local secrets (app_id / app_key) come from a git-ignored .env file or real environment variables.
DotEnv.Load(Path.Combine(Directory.GetCurrentDirectory(), ".env"));

var builder = WebApplication.CreateBuilder(args);

// ---------- Configuration (validated at startup so a missing secret fails fast) ----------
builder.Services.AddOptions<UpstreamApiOptions>()
    .Bind(builder.Configuration.GetSection(UpstreamApiOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.Configure<SessionCookieOptions>(builder.Configuration.GetSection(SessionCookieOptions.SectionName));
builder.Services.Configure<FileLoggingOptions>(builder.Configuration.GetSection(FileLoggingOptions.SectionName));
builder.Services.Configure<AuditTrailOptions>(builder.Configuration.GetSection(AuditTrailOptions.SectionName));

// ---------- Logging: console + structured JSON files ----------
builder.Logging.Services.AddSingleton<ILoggerProvider, JsonFileLoggerProvider>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<LogFileReader>();

// ---------- Errors: every error is returned as ProblemDetails JSON with a traceId ----------
builder.Services.AddProblemDetails(options =>
    options.CustomizeProblemDetails = context =>
        context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier);
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

// ---------- Upstream assessment API ----------
builder.Services.AddTransient<AppCredentialsHandler>();
builder.Services.AddTransient<UpstreamLoggingHandler>();
builder.Services
    .AddHttpClient<UpstreamApiClient>((services, http) =>
    {
        var options = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<UpstreamApiOptions>>().Value;
        http.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
        http.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
    })
    // Cookies are handled by hand: each user's upstream cookies live in their own session, never shared.
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { UseCookies = false })
    .AddHttpMessageHandler<AppCredentialsHandler>()
    .AddHttpMessageHandler<UpstreamLoggingHandler>();
builder.Services.AddScoped<UpstreamGateway>();

// ---------- Auth: encrypted session cookies, every endpoint requires login unless marked [AllowAnonymous] ----------
builder.Services.AddDataProtection().SetApplicationName("TrackLab");
builder.Services.AddSingleton<SessionCookieStore>();
builder.Services
    .AddAuthentication(SessionAuthenticationDefaults.Scheme)
    .AddScheme<AuthenticationSchemeOptions, SessionAuthenticationHandler>(SessionAuthenticationDefaults.Scheme, null);
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

// ---------- Audit trail ----------
builder.Services.AddSingleton<IAuditTrailStore, JsonFileAuditTrailStore>();
builder.Services.AddScoped<AuditTrailRecorder>();

// ---------- Security ----------
builder.Services.AddRateLimiter(RateLimitPolicies.Configure);
builder.Services.AddCors(options => options.AddPolicy("frontend", policy => policy
    .WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
    .AllowAnyHeader()
    .WithMethods("GET", "POST", "PUT", "DELETE")
    .AllowCredentials()
    .WithExposedHeaders("X-Correlation-Id", "X-Resource-Id", "X-Sort-Fallback", "X-Paging-TotalCount", "X-Paging-Total")));
builder.Services.Configure<ForwardedHeadersOptions>(options =>
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto);

builder.Services.AddControllers();
builder.Services.AddHealthChecks();

var app = builder.Build();

// ---------- HTTP pipeline (order matters: each middleware wraps the ones below it) ----------
app.UseForwardedHeaders();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseExceptionHandler();
app.UseMiddleware<RequestLoggingMiddleware>();
app.UseCors("frontend");
app.UseRateLimiter();
app.UseAuthentication();
app.UseMiddleware<CsrfHeaderMiddleware>();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health").AllowAnonymous();

app.Run();

/// <summary>Exposed so integration tests can start the app with WebApplicationFactory.</summary>
public partial class Program;
