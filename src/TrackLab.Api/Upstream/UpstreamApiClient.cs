using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using TrackLab.Api.Configuration;

namespace TrackLab.Api.Upstream;

/// <summary>
/// Typed HttpClient for the assessment API. It only knows how to send and read raw requests;
/// app credentials and logging are added by the DelegatingHandlers registered in Program.cs.
/// </summary>
public sealed class UpstreamApiClient(HttpClient http, IOptions<UpstreamApiOptions> options)
{
    private static readonly string[] ForwardedHeaderPrefixes = ["X-Paging", "X-Total", "X-Pagination"];

    /// <summary>
    /// Empty optional fields are left out instead of sent as null: the assessment API rejects null for
    /// some of them (e.g. "mandays" on trackers, found against the real API) but accepts a missing field.
    /// </summary>
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly UpstreamApiOptions _options = options.Value;

    public async Task<UpstreamResponse> SendAsync(UpstreamRequest request, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(request.Method, BuildRelativeUri(request));

        if (request.Body is not null)
        {
            // Serialised up front so the request has a Content-Length. A streamed (chunked) body made
            // the assessment API hang on DELETE until the timeout, found while testing against it.
            var json = JsonSerializer.Serialize(request.Body, request.Body.GetType(), JsonOptions);
            message.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        if (!string.IsNullOrEmpty(request.AccessToken))
        {
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", request.AccessToken);
        }

        if (!string.IsNullOrEmpty(request.Cookie))
        {
            message.Headers.TryAddWithoutValidation("Cookie", request.Cookie);
        }

        if (request.Headers is not null)
        {
            foreach (var (name, value) in request.Headers)
            {
                message.Headers.TryAddWithoutValidation(name, value);
            }
        }

        using var response = await http.SendAsync(message, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        return new UpstreamResponse
        {
            StatusCode = (int)response.StatusCode,
            Body = body,
            ContentType = response.Content.Headers.ContentType?.MediaType,
            ForwardHeaders = CollectForwardHeaders(response),
            SetCookies = response.Headers.TryGetValues("Set-Cookie", out var cookies) ? cookies.ToList() : [],
        };
    }

    private string BuildRelativeUri(UpstreamRequest request)
    {
        var basePath = request.Area == UpstreamArea.Auth ? _options.AuthPath : _options.FeaturePath;
        // No leading slash, so the path is appended to HttpClient.BaseAddress.
        return $"{basePath.Trim('/')}/{request.Path.TrimStart('/')}";
    }

    private static Dictionary<string, string> CollectForwardHeaders(HttpResponseMessage response)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var allHeaders = response.Headers.Concat(response.Content.Headers);

        foreach (var header in allHeaders)
        {
            if (ForwardedHeaderPrefixes.Any(prefix => header.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            {
                result[header.Key] = string.Join(",", header.Value);
            }
        }

        return result;
    }
}
