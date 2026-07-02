using Microsoft.Playwright;
using Template.E2E.Tests.Pages;

namespace Template.E2E.Tests;

/// <summary>
/// v2 audit B8-5: localization end-to-end. Switching the language selector re-renders the UI in the
/// chosen culture (resx-backed <c>IStringLocalizer</c>, EN/ES shipped). Asserts the change by capturing
/// a label before and after the switch rather than hard-coding a translated string, so the test doesn't
/// couple to the exact wording. Maps to the QA plan's i18n case.
/// </summary>
[TestFixture]
public class I18nTests : E2ETestBase
{
    [Test]
    public async Task Switching_Language_ReRendersTheUi()
    {
        var login = new LoginPage(Page);
        await login.GotoAsync();
        await Expect(login.SendOtp).ToBeVisibleAsync(new() { Timeout = 30_000 });

        var english = (await login.SendOtp.InnerTextAsync()).Trim();

        var switcher = Page.GetByTestId("language-switcher");
        await Expect(switcher).ToBeVisibleAsync();
        await switcher.SelectOptionAsync("es"); // triggers a full reload into Spanish

        // After the reload the same control shows a different (Spanish) label.
        await Expect(login.SendOtp).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(login.SendOtp).Not.ToHaveTextAsync(english, new() { Timeout = 15_000 });
    }
}
