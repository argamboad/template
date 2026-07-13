using Microsoft.Playwright;

namespace Perezosoft.E2E.Tests;

/// <summary>
/// Dark-mode journey (THEME-1): the header switcher flips <c>data-bs-theme</c> live (no
/// reload), the choice survives a reload via the pre-paint localStorage bootstrap, and —
/// because it's saved per user server-side — a fresh browser context ("new device") signing
/// in as the same user is reconciled back to dark on its next cold start, mirroring the
/// locale reconcile (MITI-5). Maps to QA-SET-08.
/// </summary>
[TestFixture]
public class ThemeJourneyTests : E2ETestBase
{
    [Test]
    public async Task ThemeChoice_AppliesLive_PersistsLocally_AndFollowsTheUser()
    {
        await Mailpit.ClearAsync();
        var email = UniqueEmail("theme");

        await SignInAsync(Page, email);

        // Default is the OS scheme; the Playwright browser emulates light.
        await Expect(Page.Locator("html")).ToHaveAttributeAsync("data-bs-theme", "light");

        // Flip to dark from the header: applies live, no reload. The switcher saves the
        // choice server-side with a best-effort background PUT; wait for it so the reload
        // below can't abort it in flight (the "follows the user" leg depends on it).
        await Page.RunAndWaitForResponseAsync(
            () => Page.GetByTestId("theme-switcher").SelectOptionAsync("dark"),
            r => r.Url.EndsWith("/api/auth/theme") && r.Request.Method == "PUT");
        await Expect(Page.Locator("html")).ToHaveAttributeAsync("data-bs-theme", "dark");

        // Survives a reload: theme.js re-applies from localStorage before first paint.
        await Page.ReloadAsync();
        await Expect(Page.GetByTestId("sign-out")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(Page.Locator("html")).ToHaveAttributeAsync("data-bs-theme", "dark");

        // "New device": a fresh context has no localStorage, so it renders light; after
        // sign-in, the next cold start (reload with the refresh cookie) reconciles the
        // layout to the server-stored dark, exactly like the locale playbook. Drop the
        // first sign-in's email first so the OTP poll can't misread the consumed code.
        await Mailpit.ClearAsync();
        await using var secondCtx = await Browser.NewContextAsync(ContextOptions());
        var secondPage = await secondCtx.NewPageAsync();
        await SignInAsync(secondPage, email);
        await secondPage.ReloadAsync();
        await Expect(secondPage.GetByTestId("sign-out")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(secondPage.Locator("html")).ToHaveAttributeAsync("data-bs-theme", "dark", new() { Timeout = 30_000 });
    }
}
