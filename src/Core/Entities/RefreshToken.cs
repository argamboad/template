namespace Template.Core.Entities;

/// <summary>
/// Refresh token for session persistence and token rotation.
/// Stored in the database (hashed) for server-side validation.
/// </summary>
public class RefreshToken
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }
    public required string TokenHash { get; set; }
    public DateTime IssuedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public bool IsRevoked { get; set; }
    public required string IssuedFromIp { get; set; }

    /// <summary>
    /// OAuth provider used at sign-in for this session; carried through
    /// rotation so refreshed JWTs keep an accurate provider claim.
    /// </summary>
    public required string Provider { get; set; }
}
