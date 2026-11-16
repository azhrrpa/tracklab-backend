using System.ComponentModel.DataAnnotations;

namespace TrackLab.Api.Contracts;

/// <summary>An error or event reported by the frontend so it ends up in the same log as backend events.</summary>
public sealed class ClientLogRequest : IValidatableObject
{
    [Required]
    [RegularExpression("^(?i)(error|warning|info)$", ErrorMessage = "level must be error, warning or info.")]
    public string Level { get; set; } = "error";

    [Required, StringLength(2000)]
    public string Message { get; set; } = string.Empty;

    [StringLength(500)]
    public string? Url { get; set; }

    [StringLength(8000)]
    public string? Stack { get; set; }

    public Dictionary<string, string>? Context { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Context is null)
        {
            yield break;
        }

        if (Context.Count > 20)
        {
            yield return new ValidationResult("context can have at most 20 entries.", [nameof(Context)]);
        }

        if (Context.Any(entry => entry.Key.Length > 100 || entry.Value.Length > 1000))
        {
            yield return new ValidationResult("context keys must be at most 100 and values at most 1000 characters.", [nameof(Context)]);
        }
    }
}
