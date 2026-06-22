using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using NUnit.Framework;

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
}
