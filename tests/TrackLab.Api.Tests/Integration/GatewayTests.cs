using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TrackLab.Api.Tests.Support;
using static TrackLab.Api.Tests.Support.TrackLabFactory;

namespace TrackLab.Api.Tests.Integration;

public class GatewayTests : IDisposable
{
    private static readonly Guid TrackerId = Guid.Parse("3080440e-245a-1726-adc2-6638349222d3");

    private readonly TrackLabFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private static object ValidTracker(string code = "QA-CHECK") => new
    {
        code,
        parentCode = "ANL-DONE",
        description = "QA Checked",
        actionType = "text",
        moduleFlow = "CA",
        order = 8,
        mandays = 2,
        isPublic = true,
    };

    [Fact]
    public async Task Paging_query_is_sent_as_x_paging_headers_and_totals_come_back()
    {
        _factory.Upstream.On(
            HttpMethod.Get,
            $"{FeaturePath}/trackers/paging",
            _ =>
            {
                var response = FakeUpstream.Json(HttpStatusCode.OK, """{"data":[{"code":"REQ-CREATE"}],"success":true}""");
                response.Headers.Add("X-Paging-TotalCount", "18");
                return response;
            });
        var client = await _factory.CreateLoggedInClientAsync();

        var response = await client.GetAsync("/api/trackers?offset=10&limit=10&sortBy=code&sortOrder=DESC&search=REQ");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("18", response.Headers.GetValues("X-Paging-TotalCount").Single());
        var upstream = _factory.Upstream.Requests.Last();
        Assert.Equal($"Bearer {_factory.AccessToken}", upstream.Header("Authorization"));
        Assert.Equal("10", upstream.Header("X-Paging-Offset"));
        Assert.Equal("code", upstream.Header("X-Paging-SortBy"));
        Assert.Equal("DESC", upstream.Header("X-Paging-SortOrder"));
        Assert.Equal("REQ", upstream.Header("X-Paging-Search"));
    }

    [Fact]
    public async Task Sorting_by_a_column_the_api_lacks_falls_back_to_id()
    {
        _factory.Upstream.On(HttpMethod.Get, $"{FeaturePath}/requests/paging", request =>
            request.Header("X-Paging-SortBy") == "id"
                ? FakeUpstream.Json(HttpStatusCode.OK, """{"data":{"items":[],"totalItems":0},"success":true}""")
                : FakeUpstream.Json(HttpStatusCode.InternalServerError, """{"success":false,"message":"42703: column \"created_on\" does not exist"}"""));
        var client = await _factory.CreateLoggedInClientAsync();

        var response = await client.GetAsync("/api/requests?sortBy=createdOn&sortOrder=DESC");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("createdOn", response.Headers.GetValues("X-Sort-Fallback").Single());
    }

    [Fact]
    public async Task Invalid_paging_values_are_rejected_before_calling_the_api()
    {
        var client = await _factory.CreateLoggedInClientAsync();
        var callsBefore = _factory.Upstream.Requests.Count;

        var response = await client.GetAsync("/api/trackers?limit=5000&sortBy=code;DROP");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(callsBefore, _factory.Upstream.Requests.Count);
    }

    [Fact]
    public async Task Create_sets_createdBy_from_the_session_and_writes_the_audit_trail()
    {
        _factory.Upstream.On(HttpMethod.Post, $"{FeaturePath}/trackers", HttpStatusCode.OK, """{"data":null,"success":true,"message":"OK"}""");
        var client = await _factory.CreateLoggedInClientAsync();

        var response = await client.PostAsJsonAsync("/api/trackers", ValidTracker());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var sent = JsonDocument.Parse(_factory.Upstream.Requests.Last().Body).RootElement;
        Assert.Equal("assessment.it", sent.GetProperty("createdBy").GetString());
        Assert.Equal("QA-CHECK", sent.GetProperty("code").GetString());
        Assert.True(Guid.TryParse(sent.GetProperty("id").GetString(), out _));

        var audit = await client.GetFromJsonAsync<JsonElement>("/api/audit-trails?entity=tracker");
        var entry = Assert.Single(audit.GetProperty("items").EnumerateArray());
        Assert.Equal("Create", entry.GetProperty("action").GetString());
        Assert.Equal("assessment.it", entry.GetProperty("user").GetString());
    }

    [Fact]
    public async Task Update_records_what_changed()
    {
        _factory.Upstream.On(HttpMethod.Get, $"{FeaturePath}/trackers/{TrackerId}", HttpStatusCode.OK,
            """{"data":{"code":"QA-CHECK","parentCode":"ANL-DONE","description":"Old text","actionType":"text","moduleFlow":"CA","order":8,"mandays":2,"isPublic":true},"success":true}""");
        _factory.Upstream.On(HttpMethod.Put, $"{FeaturePath}/trackers", HttpStatusCode.OK, """{"success":true}""");
        var client = await _factory.CreateLoggedInClientAsync();

        var response = await client.PutAsJsonAsync($"/api/trackers/{TrackerId}", ValidTracker());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var sent = JsonDocument.Parse(_factory.Upstream.Requests.Last().Body).RootElement;
        Assert.Equal(TrackerId.ToString(), sent.GetProperty("id").GetString());
        Assert.Equal("assessment.it", sent.GetProperty("updatedBy").GetString());

        var audit = await client.GetFromJsonAsync<JsonElement>($"/api/audit-trails?entityId={TrackerId}");
        var change = Assert.Single(audit.GetProperty("items")[0].GetProperty("changes").EnumerateArray());
        Assert.Equal("description", change.GetProperty("field").GetString());
        Assert.Equal("Old text", change.GetProperty("oldValue").GetString());
        Assert.Equal("QA Checked", change.GetProperty("newValue").GetString());
    }

    [Fact]
    public async Task Delete_is_soft_by_default_and_hard_on_request()
    {
        _factory.Upstream.On(HttpMethod.Get, $"{FeaturePath}/trackers/{TrackerId}", HttpStatusCode.OK, """{"data":{"code":"QA"}}""");
        _factory.Upstream.On(HttpMethod.Delete, $"{FeaturePath}/trackers", HttpStatusCode.OK, """{"success":true}""");
        var client = await _factory.CreateLoggedInClientAsync();

        await client.DeleteAsync($"/api/trackers/{TrackerId}");
        var soft = JsonDocument.Parse(_factory.Upstream.Requests.Last().Body).RootElement;
        await client.DeleteAsync($"/api/trackers/{TrackerId}?hard=true");
        var hard = JsonDocument.Parse(_factory.Upstream.Requests.Last().Body).RootElement;

        Assert.False(soft.GetProperty("doHardDelete").GetBoolean());
        Assert.Equal("assessment.it", soft.GetProperty("deletedBy").GetString());
        Assert.True(hard.GetProperty("doHardDelete").GetBoolean());
    }

    [Fact]
    public async Task Invalid_body_returns_field_errors_and_never_reaches_the_api()
    {
        var client = await _factory.CreateLoggedInClientAsync();
        var callsBefore = _factory.Upstream.Requests.Count;

        var response = await client.PostAsJsonAsync("/api/trackers", new { code = "bad code!", description = "", actionType = "NONE", moduleFlow = "CA", order = -1 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");
        Assert.True(errors.TryGetProperty("Code", out _));
        Assert.True(errors.TryGetProperty("Description", out _));
        Assert.True(errors.TryGetProperty("Order", out _));
        Assert.Equal(callsBefore, _factory.Upstream.Requests.Count);
    }

    [Fact]
    public async Task Expired_access_token_is_refreshed_with_the_refresh_cookie_and_the_call_retried()
    {
        var newToken = Jwt.Create(DateTimeOffset.UtcNow.AddHours(1));
        _factory.Upstream.On(HttpMethod.Get, $"{FeaturePath}/trackers/paging", request =>
            request.Header("Authorization") == $"Bearer {newToken}"
                ? FakeUpstream.Json(HttpStatusCode.OK, """{"data":[],"success":true}""")
                : FakeUpstream.Json(HttpStatusCode.Unauthorized, """{"success":false,"message":"Token expired"}"""));
        _factory.Upstream.On(HttpMethod.Get, $"{AuthPath}/auth/token/refresh", HttpStatusCode.OK,
            $$$"""{"data":{"token":{"access":"{{{newToken}}}","refresh":"r2"}},"success":true}""");
        var client = await _factory.CreateLoggedInClientAsync();

        var first = await client.GetAsync("/api/trackers");
        var second = await client.GetAsync("/api/trackers");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var refresh = Assert.Single(_factory.Upstream.Requests, r => r.PathAndQuery.Contains("/auth/token/refresh"));
        Assert.Contains($"RefreshToken={_factory.RefreshToken}", refresh.Header("Cookie"));
        Assert.Contains("sl-session=abc", refresh.Header("Cookie"));
    }

    [Fact]
    public async Task A_token_rejected_before_activation_is_activated_and_the_call_retried()
    {
        var activated = false;
        _factory.Upstream.On(HttpMethod.Get, $"{AuthPath}/auth/authorization", _ =>
        {
            activated = true;
            return FakeUpstream.Json(HttpStatusCode.OK, """{"data":{},"success":true}""");
        });
        _factory.Upstream.On(HttpMethod.Get, $"{FeaturePath}/requests/paging", _ =>
            activated
                ? FakeUpstream.Json(HttpStatusCode.OK, """{"data":{"items":[],"totalItems":0},"success":true}""")
                : FakeUpstream.Json(HttpStatusCode.Unauthorized, """{"message":"Unauthorized"}"""));
        var client = await _factory.CreateLoggedInClientAsync();
        activated = false; // simulate the API forgetting the activation (e.g. cache expiry)

        var response = await client.GetAsync("/api/requests");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(_factory.Upstream.Requests, r => r.PathAndQuery.Contains("/auth/token/refresh"));
    }

    [Fact]
    public async Task When_refresh_fails_the_session_ends_with_401()
    {
        _factory.Upstream.On(HttpMethod.Get, $"{FeaturePath}/trackers/paging", HttpStatusCode.Unauthorized, """{"success":false}""");
        _factory.Upstream.On(HttpMethod.Get, $"{AuthPath}/auth/token/refresh", HttpStatusCode.Unauthorized, """{"success":false}""");
        var client = await _factory.CreateLoggedInClientAsync();

        var response = await client.GetAsync("/api/trackers");
        var afterwards = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, afterwards.StatusCode);
    }

    [Fact]
    public async Task Upstream_server_errors_become_502_without_leaking_details()
    {
        _factory.Upstream.On(HttpMethod.Get, $"{FeaturePath}/requests/paging", HttpStatusCode.InternalServerError, """{"stackTrace":"SqlException at line 42"}""");
        var client = await _factory.CreateLoggedInClientAsync();

        var response = await client.GetAsync("/api/requests");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.DoesNotContain("SqlException", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Request_trackings_are_read_from_the_request_id_endpoint()
    {
        var requestId = Guid.NewGuid();
        _factory.Upstream.On(HttpMethod.Get, $"{FeaturePath}/trackings/request-id/{requestId}", HttpStatusCode.OK, """{"data":[],"success":true}""");
        var client = await _factory.CreateLoggedInClientAsync();

        var response = await client.GetAsync($"/api/requests/{requestId}/trackings");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Frontend_errors_can_be_reported_to_the_log()
    {
        var client = _factory.CreateBrowserClient();
        client.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");

        var accepted = await client.PostAsJsonAsync("/api/logs/client", new { level = "error", message = "TypeError: x is undefined", url = "/trackers" });
        var rejected = await client.PostAsJsonAsync("/api/logs/client", new { level = "panic", message = "" });

        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
    }
}
