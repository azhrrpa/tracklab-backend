using System.ComponentModel.DataAnnotations;

namespace TrackLab.Api.Contracts;

/// <summary>Query-string paging parameters, e.g. GET /api/trackers?offset=0&amp;limit=10&amp;sortBy=code&amp;sortOrder=ASC&amp;search=REQ.</summary>
public class PagingQuery
{
    [Range(0, int.MaxValue)]
    public int Offset { get; set; }

    [Range(1, 100)]
    public int Limit { get; set; } = 10;

    /// <summary>Property name only (letters, digits, underscore), so nothing else can be injected into the header.</summary>
    [StringLength(64)]
    [RegularExpression("^[A-Za-z_][A-Za-z0-9_]*$", ErrorMessage = "sortBy must be a property name.")]
    public string? SortBy { get; set; }

    [RegularExpression("^(?i)(ASC|DESC)$", ErrorMessage = "sortOrder must be ASC or DESC.")]
    public string SortOrder { get; set; } = "ASC";

    [StringLength(100)]
    public string? Search { get; set; }
}

/// <summary>Advanced paging: include/exclude filters as JSON, e.g. include={"tracker_code":"REQ-ACC||REQ-CREATE"}.</summary>
public sealed class AdvancedPagingQuery : PagingQuery, IValidatableObject
{
    [RegularExpression("^(?i)(AND|OR)$", ErrorMessage = "operator must be AND or OR.")]
    public string Operator { get; set; } = "AND";

    public bool IgnoreCase { get; set; } = true;

    [StringLength(2000)]
    public string? Include { get; set; }

    [StringLength(2000)]
    public string? Exclude { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!string.IsNullOrWhiteSpace(Include)
            && !Upstream.PagingHeaders.TryNormalizeFilter(Include, out _, out var includeError))
        {
            yield return new ValidationResult(includeError, [nameof(Include)]);
        }

        if (!string.IsNullOrWhiteSpace(Exclude)
            && !Upstream.PagingHeaders.TryNormalizeFilter(Exclude, out _, out var excludeError))
        {
            yield return new ValidationResult(excludeError, [nameof(Exclude)]);
        }
    }
}
