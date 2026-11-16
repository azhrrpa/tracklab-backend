using System.Text;
using System.Text.Json;

namespace TrackLab.Api.Tests.Support;

/// <summary>Builds unsigned JWT-shaped strings for tests (the backend only reads the payload).</summary>
public static class Jwt
{
    public static string Create(DateTimeOffset expires, string? name = null)
    {
        var header = Encode(new { alg = "HS256", typ = "JWT" });
        object payload = name is null
            ? new { Value = Guid.NewGuid().ToString("N"), exp = expires.ToUnixTimeSeconds(), aud = "assessment.it" }
            : new { name, exp = expires.ToUnixTimeSeconds(), aud = "assessment.it" };
        return $"{header}.{Encode(payload)}.signature";
    }

    private static string Encode(object value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value)))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
