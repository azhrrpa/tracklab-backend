using System.ComponentModel.DataAnnotations;

namespace TrackLab.Api.Contracts;

/// <summary>Fields a user can edit on a lab analysis request (table t_request_headers).</summary>
public class RequestFields : IValidatableObject
{
    [Required]
    public Guid? PartnerId { get; set; }

    [Required, StringLength(50)]
    public string ProjectCode { get; set; } = string.Empty;

    [Range(0, 1_000_000_000_000)]
    public decimal TotalFee { get; set; }

    [StringLength(100)]
    public string? LastStatus { get; set; }

    [StringLength(255)]
    public string? ReceivingStatus { get; set; }

    [Range(0, 100_000)]
    public int? LeadTime { get; set; }

    public DateTime? ReceivedDate { get; set; }

    public DateTime? AnalysisWipDate { get; set; }

    public DateTime? AnalysisCompletedDate { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (AnalysisWipDate is not null && AnalysisCompletedDate is not null && AnalysisCompletedDate < AnalysisWipDate)
        {
            yield return new ValidationResult(
                "Analysis completed date cannot be earlier than the analysis start date.",
                [nameof(AnalysisCompletedDate)]);
        }
    }
}

public sealed class CreateRequestRequest : RequestFields;

public sealed class UpdateRequestRequest : RequestFields
{
    public bool IsActive { get; set; } = true;
}
