using System.ComponentModel.DataAnnotations;

namespace TrackLab.Api.Configuration;

/// <summary>
/// Settings for the assessment API that this backend forwards requests to.
/// AppId and AppKey are secrets: set them in .env or environment variables, never in appsettings.json.
/// </summary>
public sealed class UpstreamApiOptions
{
    public const string SectionName = "UpstreamApi";

    [Required, Url]
    public string BaseUrl { get; set; } = string.Empty;

    [Required]
    public string AuthPath { get; set; } = "/api/v1/authentication";

    [Required]
    public string FeaturePath { get; set; } = "/api/v1/feature";

    [Required(ErrorMessage = "UpstreamApi:AppId is missing. Copy .env.example to .env and fill in UpstreamApi__AppId.")]
    public string AppId { get; set; } = string.Empty;

    [Required(ErrorMessage = "UpstreamApi:AppKey is missing. Copy .env.example to .env and fill in UpstreamApi__AppKey.")]
    public string AppKey { get; set; } = string.Empty;

    [Required]
    public string AppCode { get; set; } = "ASSESSMENT";

    [Required]
    public string ModuleCode { get; set; } = "ALL";

    /// <summary>Kept below the Next.js proxy timeout (30 s) so users get this API's error message, not the proxy's.</summary>
    [Range(1, 300)]
    public int TimeoutSeconds { get; set; } = 25;

    /// <summary>
    /// Whether deletes go to the API's DELETE endpoints. Off by default: the API's infrastructure drops
    /// DELETE request bodies (every call fails, and the connection can hang afterwards), so records are
    /// deactivated through PUT with isActive=false instead. Turn on if the API gets fixed.
    /// </summary>
    public bool UseDeleteEndpoint { get; set; }

    /// <summary>Name of the cookie the refresh endpoint reads the refresh token from.</summary>
    [Required]
    public string RefreshCookieName { get; set; } = "RefreshToken";
}
