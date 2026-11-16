using System.Globalization;
using System.Text.Json;

namespace TrackLab.Api.Audit;

/// <summary>
/// Works out which fields changed between the "before" snapshot (from the API) and the data sent.
/// Keys are matched ignoring case and '_' so tracker_code and trackerCode count as the same field.
/// </summary>
public static class AuditDiff
{
    private static readonly HashSet<string> IgnoredFields =
    [
        "id", "createdby", "createdon", "createddate", "lastupdatedby", "lastupdatedon",
        "updatedby", "updatedon", "deletedby", "deletedon",
    ];

    public static IReadOnlyList<AuditChange> Compute(JsonElement? before, JsonElement? after)
    {
        var afterFields = Flatten(after);
        if (afterFields.Count == 0)
        {
            return [];
        }

        var beforeFields = Flatten(before);
        var changes = new List<AuditChange>();

        foreach (var (key, (name, newValue)) in afterFields)
        {
            if (before is null)
            {
                if (newValue is not null)
                {
                    changes.Add(new AuditChange(name, null, newValue));
                }

                continue;
            }

            if (!beforeFields.TryGetValue(key, out var old))
            {
                continue;
            }

            if (!AreEqual(old.Value, newValue))
            {
                changes.Add(new AuditChange(name, old.Value, newValue));
            }
        }

        return changes;
    }

    /// <summary>Unwraps a {"data": {...}} envelope if the API uses one.</summary>
    public static JsonElement? Unwrap(JsonElement? element)
    {
        if (element is not { ValueKind: JsonValueKind.Object } value)
        {
            return element;
        }

        foreach (var property in value.EnumerateObject())
        {
            if (string.Equals(property.Name, "data", StringComparison.OrdinalIgnoreCase)
                && property.Value.ValueKind == JsonValueKind.Object)
            {
                return property.Value;
            }
        }

        return value;
    }

    private static Dictionary<string, (string Name, string? Value)> Flatten(JsonElement? element)
    {
        var result = new Dictionary<string, (string, string?)>();
        if (Unwrap(element) is not { ValueKind: JsonValueKind.Object } value)
        {
            return result;
        }

        foreach (var property in value.EnumerateObject())
        {
            var key = Normalize(property.Name);
            if (!IgnoredFields.Contains(key))
            {
                result[key] = (property.Name, ToText(property.Value));
            }
        }

        return result;
    }

    private static string? ToText(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonValueKind.String => value.GetString(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => value.GetRawText(),
    };

    private static bool AreEqual(string? left, string? right)
    {
        if (string.IsNullOrEmpty(left) && string.IsNullOrEmpty(right))
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        if (string.Equals(left, right, StringComparison.Ordinal))
        {
            return true;
        }

        if (decimal.TryParse(left, NumberStyles.Number, CultureInfo.InvariantCulture, out var leftNumber)
            && decimal.TryParse(right, NumberStyles.Number, CultureInfo.InvariantCulture, out var rightNumber))
        {
            return leftNumber == rightNumber;
        }

        if (LooksLikeDate(left) && LooksLikeDate(right)
            && DateTimeOffset.TryParse(left, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var leftDate)
            && DateTimeOffset.TryParse(right, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var rightDate))
        {
            return leftDate.UtcDateTime == rightDate.UtcDateTime;
        }

        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase) && bool.TryParse(left, out _);
    }

    private static bool LooksLikeDate(string value) => value.Length >= 10 && value[4] == '-' && value[7] == '-';

    private static string Normalize(string key) => key.Replace("_", string.Empty).ToLowerInvariant();
}
