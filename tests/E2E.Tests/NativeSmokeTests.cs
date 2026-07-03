using Microsoft.Playwright;
using NUnit.Framework;

namespace Template.E2E.Tests;

/// <summary>
/// Native smoke (NATIVE-7, ADR-018): drives the REAL MAUI app over the Chrome DevTools Protocol —
/// the app's WebView is launched with remote debugging enabled (WebView2:
/// <c>WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=9223</c>; Android WebView:
/// an <c>adb forward</c> to its devtools socket) and Playwright connects with
/// <see cref="IBrowserType.ConnectOverCDPAsync"/>. Deliberately tiny (auth + one authorized page):
/// native UI automation is slower and flakier than browser E2E, so this is a boot-and-sign-in
/// canary, not a regression suite — the per-feature matrix stays manual (QA plan §12–13b).
///
/// [Explicit] keeps it out of the default `dotnet test` run (the browser e2e job); the CI native
/// smoke selects it with <c>--filter "Category=NativeSmoke"</c>. Config via env:
/// NATIVE_SMOKE_CDP (default http://localhost:9223), MAILPIT_BASE_URL (the Mailpit helper).
/// </summary>
[TestFixture]
[Explicit("Needs a running native app with CDP enabled — launched by the native-smoke CI job")]
[Category("NativeSmoke")]
public class NativeSmokeTests
{
    private static string CdpUrl =>
        Environment.GetEnvironmentVariable("NATIVE_SMOKE_CDP") ?? "http://localhost:9223";

    [Test]
    public async Task Native_App_Boots_SignsIn_And_LoadsHousehold()
    {
        using var pw = await Playwright.CreateAsync();

        // The app may still be starting when the runner reaches this test — retry the CDP connect.
        IBrowser? browser = null;
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(90);
        while (true)
        {
            try
            {
                browser = await pw.Chromium.ConnectOverCDPAsync(CdpUrl);
                break;
            }
            catch when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(3000);
            }
        }

        var page = browser.Contexts[0].Pages[0];
        TestContext.Out.WriteLine($"connected: {page.Url}");

        // Boot: the login screen renders (a startup crash — like G7 on iOS — dies here).
        var emailBox = page.GetByTestId("login-email");
        await emailBox.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 60_000 });

        // OTP sign-in end-to-end through the real API + Mailpit (the native body-token transport).
        var email = $"native-smoke-{Guid.NewGuid():N}@example.com";
        await Mailpit.ClearAsync();
        await emailBox.FillAsync(email);
        await page.GetByTestId("login-send-otp").ClickAsync();
        var code = await Mailpit.WaitForOtpAsync(email, TimeSpan.FromSeconds(60));
        await page.GetByTestId("login-otp-code").FillAsync(code);
        await page.GetByTestId("login-verify-otp").ClickAsync();
        await page.GetByTestId("sign-out").First.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 60_000 });

        // One authorized page: Household loads its data — proves the native Bearer path.
        await page.GotoAsync("https://0.0.0.1/household");
        await page.GetByTestId("household-rename-input").WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 60_000 });
        var members = page.GetByTestId("member-row");
        await Assertions.Expect(members).ToHaveCountAsync(1, new() { Timeout = 30_000 });

        TestContext.Out.WriteLine("native smoke: boot + OTP sign-in + household roster OK");
    }
}
