using System.ComponentModel.DataAnnotations;

namespace TrackLab.Api.Contracts;

/// <summary>Fields a user can edit on a master tracker (table m_trackers).</summary>
public class TrackerFields
{
    [Required, StringLength(50)]
    [RegularExpression("^[A-Za-z0-9][A-Za-z0-9_.-]*$", ErrorMessage = "Code may only contain letters, numbers, '-', '_' and '.'.")]
    public string Code { get; set; } = string.Empty;

    [StringLength(50)]
    [RegularExpression("^[A-Za-z0-9][A-Za-z0-9_.-]*$", ErrorMessage = "Parent code may only contain letters, numbers, '-', '_' and '.'.")]
    public string? ParentCode { get; set; }

    [Required, StringLength(255)]
    public string Description { get; set; } = string.Empty;

    /// <summary>What the step asks for when tracked: none, text, file or date.</summary>
    [Required, StringLength(20)]
    [RegularExpression("^[a-z_]+$", ErrorMessage = "Action type must be lowercase, e.g. none, text, file or date.")]
    public string ActionType { get; set; } = "none";

    [Required, StringLength(20)]
    public string ModuleFlow { get; set; } = string.Empty;

    public bool IsUsingNotification { get; set; }

    public bool IsPublic { get; set; }

    [StringLength(100)]
    public string? Icon { get; set; }

    [Range(0, 9999)]
    public int Order { get; set; }

    [Range(0, 3650)]
    public int? Mandays { get; set; }

    public bool IsParallel { get; set; }
}

public sealed class CreateTrackerRequest : TrackerFields;

public sealed class UpdateTrackerRequest : TrackerFields
{
    public bool IsActive { get; set; } = true;
}
