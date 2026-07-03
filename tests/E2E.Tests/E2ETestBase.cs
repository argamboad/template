using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using NUnit.Framework;
using Template.E2E.Tests.Pages;

namespace Template.E2E.Tests;

/// <summary>
/// Base for E2E tests. Drives a real browser against the running Web app.
/// <para>
/// Prereqs (see docs/WAYS_OF_WORKING.md): <c>docker compose up -d</c>, then start the API
/// (https profile) and the Web app, then run. Base URL defaults to the Web app's https
/// profile and is overridable via <c>PLAYWRIGHT_BASE_URL</c>.
/// </para>
/// </summary>
public abstract class E2ETestBase : PageTest
{
    // Resolution order: the runsettings TestRunParameter wins (so `-- TestRunParameters...` /
    // playwright.runsettings actually take effect), then the environment variable, then the Web
    // app's https launch profile (https://localhost:7008).
    protected static string BaseUrl =>
        TestContext.Parameters.Get("PLAYWRIGHT_BASE_URL")
        ?? Environment.GetEnvironmentVariable("PLAYWRIGHT_BASE_URL")
        ?? "https://localhost:7008";

    // Dev runs on a self-signed cert, so ignore HTTPS errors for the test browser.
    public override BrowserNewContextOptions ContextOptions() => new()
    {
        BaseURL = BaseUrl,
        IgnoreHTTPSErrors = true,
    };

    /// <summary>OTP sign-in on the given page/context and wait for the app shell.</summary>
    protected static async Task SignInAsync(IPage page, string email)
    {
        var login = new LoginPage(page);
        await login.GotoAsync();
        await Assertions.Expect(login.Email).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await login.SignInWithOtpAsync(email);
        await Assertions.Expect(page.GetByTestId("sign-out")).ToBeVisibleAsync(new() { Timeout = 30_000 });
    }

    /// <summary>Clears Mailpit, signs the user in, and lands on the Household page.</summary>
    protected static async Task<HouseholdPage> SignInToHouseholdAsync(IPage page, string email)
    {
        await Mailpit.ClearAsync();
        await SignInAsync(page, email);
        var household = new HouseholdPage(page);
        await household.GotoAsync();
        return household;
    }

    protected static string UniqueEmail(string role) => $"e2e-{role}-{Guid.NewGuid():N}@example.com";
}
