namespace TrackLab.Api.Configuration;

/// <summary>
/// Controls the cookies that hold the user's session (tokens are never exposed to browser JavaScript).
/// </summary>
public sealed class SessionCookieOptions
{
    public const string SectionName = "SessionCookies";

    /// <summary>Send cookies only over HTTPS. Turn off only for local HTTP development.</summary>
    public bool Secure { get; set; } = true;
}
