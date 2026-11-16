using System.Text;
using System.Text.Json;
using TrackLab.Api.Contracts;

namespace TrackLab.Api.Upstream;

/// <summary>
/// Turns the frontend's query-string paging parameters into the X-Paging-* headers the assessment API expects.
/// </summary>
public static class PagingHeaders
{
    /// <summary>
    /// The API turns SortBy into an ORDER BY column and fails (HTTP 500) when it is missing,
    /// so a column every table has is always sent.
    /// </summary>
    public const string DefaultSortBy = "id";

    public static Dictionary<string, string> FromSimple(PagingQuery query)
    {
        var headers = new Dictionary<string, string>
        {
            ["X-Paging-Offset"] = query.Offset.ToString(),
            ["X-Paging-Limit"] = query.Limit.ToString(),
            ["X-Paging-SortBy"] = string.IsNullOrWhiteSpace(query.SortBy) ? DefaultSortBy : query.SortBy,
            ["X-Paging-SortOrder"] = query.SortOrder.ToUpperInvariant(),
        };

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            headers["X-Paging-Search"] = query.Search.Trim();
        }

        return headers;
    }

    public static Dictionary<string, string> FromAdvanced(AdvancedPagingQuery query)
    {
        var headers = FromSimple(query);
        headers.Remove("X-Paging-Search");
        headers["X-Paging-Operator"] = query.Operator.ToUpperInvariant();
        headers["X-Paging-IgnoreCase"] = query.IgnoreCase ? "true" : "false";

        if (!string.IsNullOrWhiteSpace(query.Include))
        {
            headers["X-Paging-Include"] = EncodeFilter(query.Include);
        }

        if (!string.IsNullOrWhiteSpace(query.Exclude))
        {
            headers["X-Paging-Exclude"] = EncodeFilter(query.Exclude);
        }

        return headers;
    }

    /// <summary>
    /// Validates a filter such as {"tracker_code":"REQ-ACC||REQ-CREATE"} and Base64-encodes it.
    /// Only a flat object of string values is accepted.
    /// </summary>
    public static string EncodeFilter(string json)
    {
        if (!TryNormalizeFilter(json, out var normalized, out var error))
        {
            throw new ArgumentException(error, nameof(json));
        }

        return Convert.ToBase64String(Encoding.UTF8.GetBytes(normalized));
    }

    public static bool TryNormalizeFilter(string json, out string normalized, out string error)
    {
        normalized = string.Empty;
        error = string.Empty;

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                error = "Filter must be a JSON object, for example {\"tracker_code\":\"REQ-CREATE\"}.";
                return false;
            }

            var values = new Dictionary<string, string>();
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.String)
                {
                    error = $"Filter value for '{property.Name}' must be a string.";
                    return false;
                }

                values[property.Name] = property.Value.GetString()!;
            }

            normalized = JsonSerializer.Serialize(values);
            return true;
        }
        catch (JsonException)
        {
            error = "Filter is not valid JSON.";
            return false;
        }
    }
}
