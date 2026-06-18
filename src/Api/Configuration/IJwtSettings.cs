namespace Template.Api.Configuration;

/// <summary>
/// JWT token configuration settings.
/// </summary>
public interface IJwtSettings
{
    string SecretKey { get; }
    string Issuer { get; }
    int ExpiryMinutes { get; }
}

/// <summary>
/// Refresh token configuration settings.
/// </summary>
public interface IRefreshTokenSettings
{
    int ExpiryDays { get; }
}

/// <summary>
/// Application configuration settings.
/// </summary>
public interface IApplicationSettings
{
    /// <summary>Base URL of the Blazor client (used for redirects after the OAuth round-trip).</summary>
    string ClientUrl { get; }
}

/// <summary>
/// Passwordless (magic-link + OTP) configuration settings.
/// </summary>
public interface IPasswordlessSettings
{
    int MagicLinkLifespanMinutes { get; }
    int OtpLifespanMinutes { get; }
    int OtpLength { get; }
    int OtpMaxAttempts { get; }
}

/// <summary>
/// Tenant-invitation configuration settings.
/// </summary>
public interface IInvitationSettings
{
    /// <summary>How long an invitation token stays valid, in days.</summary>
    int LifespanDays { get; }
}
