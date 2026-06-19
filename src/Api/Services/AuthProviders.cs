using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.MicrosoftAccount;

namespace Template.Api.Services;

/// <summary>
/// The OAuth providers the app supports and their ASP.NET Core scheme names — one place
/// instead of provider literals scattered across the controller. To add a provider, add a
/// constant + a <see cref="SchemeFor"/> arm here and the matching <c>.AddXxx()</c> in
/// <c>ServiceCollectionExtensions</c>.
/// </summary>
public static class AuthProviders
{
    public const string Google = "google";
    public const string Microsoft = "microsoft";

    public static readonly IReadOnlyList<string> Supported = [Google, Microsoft];

    public static bool IsSupported(string? provider) =>
        provider is not null && Supported.Contains(provider.ToLowerInvariant());

    /// <summary>The authentication scheme to challenge for a provider, or null if unsupported.</summary>
    public static string? SchemeFor(string provider) => provider.ToLowerInvariant() switch
    {
        Google => GoogleDefaults.AuthenticationScheme,
        Microsoft => MicrosoftAccountDefaults.AuthenticationScheme,
        _ => null,
    };
}
