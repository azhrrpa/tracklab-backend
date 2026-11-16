using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace TrackLab.Api.Contracts;

public sealed class LoginRequest
{
    [Required, StringLength(100)]
    public string Username { get; set; } = string.Empty;

    [Required, StringLength(200)]
    public string Password { get; set; } = string.Empty;
}

/// <summary>What the frontend learns about the logged-in user. Tokens are never included.</summary>
public sealed record CurrentUserResponse(string Username, string DisplayName, JsonElement? Authorization);
