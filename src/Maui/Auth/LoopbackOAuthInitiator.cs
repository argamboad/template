#if WINDOWS
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.ApplicationModel;
using Template.Shared.Ui.Auth;

namespace Template.Maui.Auth;

/// <summary>
/// Desktop OAuth via the loopback-redirect pattern for native apps (RFC 8252 §7.3):
/// open the API's native-login URL in the system browser, listen on a transient
/// <c>http://127.0.0.1:{port}/</c> address, and capture the one-time code the API
/// redirects back. No custom URL-scheme registration or MSIX packaging needed, which
/// is why this works for the unpackaged desktop app. (Android will instead use a
/// custom-scheme WebAuthenticator in its own slice.)
/// </summary>
public sealed class LoopbackOAuthInitiator(string apiBaseUrl, ILogger<LoopbackOAuthInitiator> logger)
    : IOAuthInitiator
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(5);

    public async Task<IReadOnlyDictionary<string, string>?> RunBrowserFlowAsync(string provider, string? linkToken = null)
    {
        var redirectUri = $"http://127.0.0.1:{GetFreeLoopbackPort()}/";

        using var listener = new HttpListener();
        listener.Prefixes.Add(redirectUri);
        try
        {
            listener.Start();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to start the OAuth loopback listener");
            return null;
        }

        try
        {
            var loginUrl = $"{apiBaseUrl}/api/auth/native/login/{provider.ToLowerInvariant()}" +
                           $"?redirect={Uri.EscapeDataString(redirectUri)}";
            if (!string.IsNullOrEmpty(linkToken))
                loginUrl += $"&link_token={Uri.EscapeDataString(linkToken)}";
            await Browser.Default.OpenAsync(loginUrl, BrowserLaunchMode.SystemPreferred);

            var contextTask = listener.GetContextAsync();
            if (await Task.WhenAny(contextTask, Task.Delay(Timeout)) != contextTask)
            {
                logger.LogWarning("OAuth loopback timed out waiting for the provider redirect");
                return null;
            }

            var context = await contextTask;
            var query = context.Request.QueryString;
            var result = query.AllKeys
                .Where(k => k is not null)
                .ToDictionary(k => k!, k => query[k] ?? string.Empty);

            await WriteClosePageAsync(context.Response, result.GetValueOrDefault("error"));
            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "OAuth loopback flow failed");
            return null;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static int GetFreeLoopbackPort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        try
        {
            return ((IPEndPoint)probe.LocalEndpoint).Port;
        }
        finally
        {
            probe.Stop();
        }
    }

    private static async Task WriteClosePageAsync(HttpListenerResponse response, string? error)
    {
        var message = string.IsNullOrEmpty(error)
            ? "Signed in. You can close this tab and return to the app."
            : "Sign-in didn't complete. You can close this tab and return to the app.";
        var html = $"<!doctype html><html><head><meta charset=\"utf-8\"><title>Perezosoft</title></head>" +
                   $"<body style=\"font-family:sans-serif;text-align:center;padding:3rem\"><p>{message}</p></body></html>";
        var buffer = Encoding.UTF8.GetBytes(html);
        response.ContentType = "text/html; charset=utf-8";
        response.ContentLength64 = buffer.Length;
        await response.OutputStream.WriteAsync(buffer);
        response.Close();
    }
}
#endif
