using System.Net;
using System.Text;

namespace TrackLab.Api.Tests.Support;

/// <summary>A request the backend sent to the (fake) assessment API.</summary>
public sealed record RecordedRequest(HttpMethod Method, string PathAndQuery, Dictionary<string, string> Headers, string Body, long? ContentLength = null)
{
    public string? Header(string name) => Headers.TryGetValue(name, out var value) ? value : null;
}

/// <summary>
/// Stands in for the assessment API in integration tests: records every request and answers with
/// whatever the test configured. Unconfigured routes return 404.
/// </summary>
public sealed class FakeUpstream : HttpMessageHandler
{
    private readonly List<(HttpMethod Method, string PathPrefix, Func<RecordedRequest, HttpResponseMessage> Respond)> _routes = [];

    public List<RecordedRequest> Requests { get; } = [];

    public FakeUpstream On(HttpMethod method, string pathPrefix, Func<RecordedRequest, HttpResponseMessage> respond)
    {
        _routes.Insert(0, (method, pathPrefix, respond));
        return this;
    }

    public FakeUpstream On(HttpMethod method, string pathPrefix, HttpStatusCode status, string json, params string[] setCookies) =>
        On(method, pathPrefix, _ => Json(status, json, setCookies));

    public static HttpResponseMessage Json(HttpStatusCode status, string json, params string[] setCookies)
    {
        var response = new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        foreach (var cookie in setCookies)
        {
            response.Headers.TryAddWithoutValidation("Set-Cookie", cookie);
        }

        return response;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        var recorded = new RecordedRequest(request.Method, request.RequestUri!.PathAndQuery, headers, body, request.Content?.Headers.ContentLength);
        Requests.Add(recorded);

        var route = _routes.FirstOrDefault(r => r.Method == request.Method && recorded.PathAndQuery.StartsWith(r.PathPrefix, StringComparison.Ordinal));
        return route.Respond is null
            ? Json(HttpStatusCode.NotFound, """{"success":false,"message":"Not found"}""")
            : route.Respond(recorded);
    }
}
