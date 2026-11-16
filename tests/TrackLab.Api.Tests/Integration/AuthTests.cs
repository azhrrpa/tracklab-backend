using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TrackLab.Api.Tests.Support;

namespace TrackLab.Api.Tests.Integration;

public class AuthTests : IDisposable
{
    private readonly TrackLabFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Login_sets_encrypted_httponly_cookies_and_never_returns_tokens()
    {
        var client = _factory.CreateBrowserClient();
        client.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");

        var response = await client.PostAsJsonAsync("/api/auth/login", new { username = "assessment.it", password = "correct-horse" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("assessment.it", body);
        Assert.DoesNotContain(_factory.AccessToken, body);
        Assert.DoesNotContain(_factory.RefreshToken, body);

        var cookies = response.Headers.GetValues("Set-Cookie").ToList();
        Assert.Contains(cookies, c => c.StartsWith("tl_at=") && c.Contains("httponly") && c.Contains("samesite=strict"));
        Assert.DoesNotContain(cookies, c => c.Contains(_factory.AccessToken));

        // The upstream call carried the app credentials and the expected body.
        var login = Assert.Single(_factory.Upstream.Requests, r => r.PathAndQuery.EndsWith("/auth/login"));
        Assert.Equal("test-app-id", login.Header("app_id"));
        Assert.Equal("test-app-key", login.Header("app_key"));
        using var json = JsonDocument.Parse(login.Body);
        Assert.Equal("ASSESSMENT", json.RootElement.GetProperty("appCode").GetString());
        Assert.Equal("ALL", json.RootElement.GetProperty("moduleCode").GetString());
        Assert.Equal("assessment.it", json.RootElement.GetProperty("user").GetString());
    }

    [Fact]
    public async Task Login_activates_the_token_and_returns_the_roles()
    {
        var client = _factory.CreateBrowserClient();
        client.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");

        var body = await (await client.PostAsJsonAsync("/api/auth/login", new { username = "assessment.it", password = "correct-horse" }))
            .Content.ReadFromJsonAsync<JsonElement>();

        var activation = Assert.Single(_factory.Upstream.Requests, r => r.PathAndQuery.Contains("/auth/authorization"));
        Assert.Equal($"Bearer {_factory.AccessToken}", activation.Header("Authorization"));
        Assert.Equal("API-TRACKER", body.GetProperty("authorization").GetProperty("data").GetProperty("moduleAccesses")[0].GetProperty("moduleCode").GetString());
    }

    [Fact]
    public async Task Wrong_password_returns_401_with_the_api_message()
    {
        var client = _factory.CreateBrowserClient();
        client.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");

        var response = await client.PostAsJsonAsync("/api/auth/login", new { username = "assessment.it", password = "wrong" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Invalid username or password", problem.GetProperty("detail").GetString());
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task Login_without_the_csrf_header_is_rejected()
    {
        var client = _factory.CreateBrowserClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new { username = "assessment.it", password = "correct-horse" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(_factory.Upstream.Requests);
    }

    [Fact]
    public async Task Login_validates_the_body()
    {
        var client = _factory.CreateBrowserClient();
        client.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");

        var response = await client.PostAsJsonAsync("/api/auth/login", new { username = "", password = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.GetProperty("errors").TryGetProperty("Username", out _));
    }

    [Fact]
    public async Task Protected_endpoints_return_401_without_a_session()
    {
        var response = await _factory.CreateBrowserClient().GetAsync("/api/trackers");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Tampered_session_cookies_are_treated_as_logged_out()
    {
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/trackers");
        request.Headers.Add("Cookie", "tl_at=forged; tl_user=forged");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Logout_calls_the_api_and_clears_the_session()
    {
        var client = await _factory.CreateLoggedInClientAsync();

        var logout = await client.PostAsync("/api/auth/logout", content: null);
        var afterwards = await client.GetAsync("/api/trackers");

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Contains(_factory.Upstream.Requests, r => r.PathAndQuery.EndsWith("/auth/logout"));
        Assert.Equal(HttpStatusCode.Unauthorized, afterwards.StatusCode);
    }

    [Fact]
    public async Task Every_response_carries_a_correlation_id_and_security_headers()
    {
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("X-Correlation-Id", "test-correlation-1");

        var response = await client.SendAsync(request);

        Assert.Equal("test-correlation-1", response.Headers.GetValues("X-Correlation-Id").Single());
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
    }
}
