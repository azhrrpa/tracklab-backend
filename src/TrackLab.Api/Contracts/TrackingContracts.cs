using System.ComponentModel.DataAnnotations;

namespace TrackLab.Api.Contracts;

/// <summary>Fields of a tracking record (table t_trackings): request X has reached tracker step Y.</summary>
public class TrackingFields
{
    [Required]
    public Guid? TrackerId { get; set; }

    [StringLength(50)]
    public string? TrackerCode { get; set; }

    [Required]
    public Guid? RequestId { get; set; }

    [Required]
    public Guid? PartnerId { get; set; }

    /// <summary>The value captured for the step (a date, text or file reference, depending on the tracker's action type).</summary>
    [StringLength(1000)]
    public string? Value { get; set; }
}

public sealed class CreateTrackingRequest : TrackingFields;

public sealed class UpdateTrackingRequest : TrackingFields
{
    public bool IsActive { get; set; } = true;
}
