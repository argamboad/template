using System.Text.Json.Serialization;

namespace Template.Api.Models;

/// <summary>
/// OAuth-style token response returned by the token and refresh endpoints.
/// </summary>
public record TokenResponse
{
    [JsonPropertyName("access_token")]
    public required string AccessToken { get; init; }

    [JsonPropertyName("token_type")]
    public string TokenType { get; init; } = "Bearer";

    [JsonPropertyName("expires_in")]
    public required int ExpiresIn { get; init; }

    [JsonPropertyName("user_id")]
    public Guid? UserId { get; init; }

    [JsonPropertyName("email")]
    public string? Email { get; init; }
}
