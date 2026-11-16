namespace TrackLab.Api.Upstream;

/// <summary>Which base path of the assessment API a request goes to.</summary>
public enum UpstreamArea
{
    Auth,
    Feature,
}

/// <summary>A request to the assessment API, described independently of HttpClient.</summary>
public sealed record UpstreamRequest(HttpMethod Method, UpstreamArea Area, string Path)
{
    public object? Body { get; init; }

    public string? AccessToken { get; init; }

    /// <summary>Raw Cookie header value, used for the refresh-token call.</summary>
    public string? Cookie { get; init; }

    public IReadOnlyDictionary<string, string>? Headers { get; init; }
}

/// <summary>The parts of an upstream response the gateway needs.</summary>
public sealed class UpstreamResponse
{
    public required int StatusCode { get; init; }

    public string Body { get; init; } = string.Empty;

    public string? ContentType { get; init; }

    /// <summary>Response headers worth passing on to the frontend (paging totals and similar).</summary>
    public IReadOnlyDictionary<string, string> ForwardHeaders { get; init; } = new Dictionary<string, string>();

    public IReadOnlyList<string> SetCookies { get; init; } = [];

    public bool IsSuccess => StatusCode is >= 200 and < 300;

    public static UpstreamResponse Unauthenticated() => new()
    {
        StatusCode = StatusCodes.Status401Unauthorized,
        Body = """{"message":"Your session has expired. Please log in again."}""",
        ContentType = "application/json",
    };
}
