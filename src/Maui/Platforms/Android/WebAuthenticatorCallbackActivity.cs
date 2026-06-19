using Android.App;
using Android.Content;
using Android.Content.PM;

namespace Template.Maui.Platforms.Android;

/// <summary>
/// Receives the OAuth callback redirect (<c>perezosoft://auth?…</c>) and hands it to
/// <see cref="Microsoft.Maui.Authentication.WebAuthenticator"/> to complete the flow.
/// The intent filter must match the scheme used by <c>AndroidOAuthInitiator</c> and the
/// API's <c>Auth:Native:CallbackScheme</c>.
/// </summary>
[Activity(NoHistory = true, LaunchMode = LaunchMode.SingleTop, Exported = true)]
[IntentFilter(
    new[] { Intent.ActionView },
    Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
    DataScheme = CallbackScheme)]
public class WebAuthenticatorCallbackActivity : Microsoft.Maui.Authentication.WebAuthenticatorCallbackActivity
{
    // Must equal MauiProgram.CallbackScheme and the API's Auth:Native:CallbackScheme.
    private const string CallbackScheme = "perezosoft";
}
