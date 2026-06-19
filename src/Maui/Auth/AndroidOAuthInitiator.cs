#if ANDROID
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Authentication;
using Template.Shared.Ui.Auth;

namespace Template.Maui.Auth;

/// <summary>
/// Android OAuth via a custom-scheme <see cref="WebAuthenticator"/> flow. A loopback
/// HTTP listener (the desktop approach) can't work on Android — 127.0.0.1 is the phone
/// itself — so the API instead redirects to <c>{scheme}://auth?…</c>, which the OS routes
/// back to the app through the registered intent filter
/// (<c>WebAuthenticatorCallbackActivity</c>). <see cref="WebAuthenticator"/> opens a
/// Custom Tab, follows the provider round-trip, and surfaces the callback query.
/// </summary>
public sealed class AndroidOAuthInitiator(
    string apiBaseUrl,
    string callbackScheme,
    ILogger<AndroidOAuthInitiator> logger) : IOAuthInitiator
{
    public async Task<IReadOnlyDictionary<string, string>?> RunBrowserFlowAsync(string provider, string? linkToken = null)
    {
        var callbackUrl = $"{callbackScheme}://auth";
        var loginUrl = $"{apiBaseUrl}/api/auth/native/login/{provider.ToLowerInvariant()}" +
                       $"?redirect={Uri.EscapeDataString(callbackUrl)}";
        if (!string.IsNullOrEmpty(linkToken))
            loginUrl += $"&link_token={Uri.EscapeDataString(linkToken)}";

        try
        {
            var result = await WebAuthenticator.Default.AuthenticateAsync(
                new Uri(loginUrl), new Uri(callbackUrl));
            // result.Properties is the parsed callback query (code, or linked/error).
            return result.Properties;
        }
        catch (TaskCanceledException)
        {
            // User dismissed the Custom Tab.
            return null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Android WebAuthenticator flow failed for {Provider}", provider);
            return null;
        }
    }
}
#endif
