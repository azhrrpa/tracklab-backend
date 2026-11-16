using System.Text;
using System.Text.Json;

namespace TrackLab.Api.Upstream;

/// <summary>
/// Reads tokens and user details out of the assessment API's auth responses.
/// The search is deliberately tolerant (nested "data" objects, camelCase or snake_case keys)
/// because the API documentation does not include response examples.
/// </summary>
public static class TokenExtractor
{
    // The assessment API returns { "data": { "token": { "access": "...", "refresh": "...", "expiresIn": 3600 } } }.
    // The other names are fallbacks in case the shape changes.
    private static readonly string[] AccessTokenKeys = ["access", "accesstoken", "token", "jwt", "jwttoken", "bearertoken"];

    private static readonly string[] RefreshTokenKeys = ["refresh", "refreshtoken"];

    private static readonly string[] DisplayNameClaims =
        ["name", "fullname", "displayname", "unique_name", "given_name", "preferred_username", "username"];

    public static string? FindAccessToken(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var _ = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            // Body is not JSON (for example a bare token string).
            var trimmed = json.Trim().Trim('"');
            return LooksLikeJwt(trimmed) ? trimmed : null;
        }

        return FindString(json, AccessTokenKeys);
    }

    public static string? FindRefreshToken(string? json) => FindString(json, RefreshTokenKeys);

    /// <summary>Reads the "exp" claim of a JWT. The signature is not checked; the upstream API does that.</summary>
    public static DateTimeOffset? ReadExpiry(string? token)
    {
        var payload = ReadJwtPayload(token);
        if (payload is null)
        {
            return null;
        }

        using var document = JsonDocument.Parse(payload);
        return document.RootElement.ValueKind == JsonValueKind.Object
            && document.RootElement.TryGetProperty("exp", out var exp)
            && exp.TryGetInt64(out var seconds)
                ? DateTimeOffset.FromUnixTimeSeconds(seconds)
                : null;
    }

    /// <summary>Finds the first string value whose key matches one of <paramref name="keys"/> (case and '_'/'-' insensitive).</summary>
    public static string? FindString(string? json, params string[] keys)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            foreach (var key in keys)
            {
                var found = FindStringByKey(document.RootElement, Normalize(key), depth: 0);
                if (!string.IsNullOrEmpty(found))
                {
                    return found;
                }
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    /// <summary>Some APIs return the access token as a cookie instead of in the body.</summary>
    public static string? FindAccessTokenInCookies(IEnumerable<string> setCookieHeaders)
    {
        foreach (var (name, value) in setCookieHeaders.Select(ParseSetCookie).OfType<KeyValuePair<string, string>>())
        {
            var normalized = Normalize(name);
            if (normalized.Contains("access") && normalized.Contains("token") && value.Length > 0)
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>
    /// Merges Set-Cookie headers into a "name=value; name2=value2" jar that can be sent back as a Cookie header.
    /// A cookie set to an empty value is removed.
    /// </summary>
    public static string? MergeCookieJar(string? existingJar, IEnumerable<string> setCookieHeaders)
    {
        var jar = new List<KeyValuePair<string, string>>();

        if (!string.IsNullOrWhiteSpace(existingJar))
        {
            foreach (var part in existingJar.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var separator = part.IndexOf('=');
                if (separator > 0)
                {
                    jar.Add(new(part[..separator], part[(separator + 1)..]));
                }
            }
        }

        foreach (var cookie in setCookieHeaders.Select(ParseSetCookie).OfType<KeyValuePair<string, string>>())
        {
            jar.RemoveAll(existing => existing.Key == cookie.Key);
            if (cookie.Value.Length > 0)
            {
                jar.Add(cookie);
            }
        }

        return jar.Count == 0 ? null : string.Join("; ", jar.Select(c => $"{c.Key}={c.Value}"));
    }

    /// <summary>
    /// Makes sure the cookie jar contains <paramref name="name"/>. A value the API set itself wins;
    /// otherwise <paramref name="fallbackValue"/> (e.g. the refresh token from the login body) is added.
    /// </summary>
    public static string? EnsureCookie(string? jar, string name, string? fallbackValue)
    {
        if (string.IsNullOrEmpty(fallbackValue))
        {
            return jar;
        }

        var hasCookie = (jar ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(part => part.StartsWith(name + "=", StringComparison.Ordinal));

        return hasCookie ? jar : MergeCookieJar(jar, [$"{name}={fallbackValue}"]);
    }

    /// <summary>Reads a display name from a JWT payload. The token is not validated here; the upstream API does that.</summary>
    public static string? ReadDisplayName(string? token)
    {
        var payload = ReadJwtPayload(token);
        if (payload is null)
        {
            return null;
        }

        using var document = JsonDocument.Parse(payload);
        foreach (var claim in DisplayNameClaims)
        {
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (Normalize(property.Name) == Normalize(claim)
                    && property.Value.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(property.Value.GetString()))
                {
                    return property.Value.GetString();
                }
            }
        }

        return null;
    }

    private static string? ReadJwtPayload(string? token)
    {
        if (token is null || !LooksLikeJwt(token))
        {
            return null;
        }

        try
        {
            var payload = token.Split('.')[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + ((4 - (payload.Length % 4)) % 4), '=');
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
            using var _ = JsonDocument.Parse(json);
            return json;
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            return null;
        }
    }

    private static bool LooksLikeJwt(string value) => value.Count(c => c == '.') == 2 && !value.Contains(' ');

    private static string? FindStringByKey(JsonElement element, string normalizedKey, int depth)
    {
        if (depth > 5)
        {
            return null;
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (Normalize(property.Name) == normalizedKey && property.Value.ValueKind == JsonValueKind.String)
                {
                    return property.Value.GetString();
                }
            }

            foreach (var property in element.EnumerateObject())
            {
                var nested = FindStringByKey(property.Value, normalizedKey, depth + 1);
                if (nested is not null)
                {
                    return nested;
                }
            }
        }

        return null;
    }

    private static KeyValuePair<string, string>? ParseSetCookie(string header)
    {
        var firstPart = header.Split(';', 2)[0];
        var separator = firstPart.IndexOf('=');
        if (separator <= 0)
        {
            return null;
        }

        return new(firstPart[..separator].Trim(), firstPart[(separator + 1)..].Trim());
    }

    private static string Normalize(string key) =>
        key.Replace("_", string.Empty).Replace("-", string.Empty).ToLowerInvariant();
}
