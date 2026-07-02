using Microsoft.Playwright;

namespace Template.E2E.Tests.Pages;

/// <summary>Page object for the invitation-accept page (/join?token=…).</summary>
public class JoinPage(IPage page) : BasePage(page)
{
    public override string Path => "/join";

    public ILocator Success => Page.GetByTestId("join-success");
    public ILocator GoToHousehold => Page.GetByTestId("join-go-household");
    public ILocator NeedsSignIn => Page.GetByTestId("join-needs-signin");
    public ILocator Error => Page.GetByTestId("join-error");

    public Task GotoWithTokenAsync(string token) =>
        Page.GotoAsync($"{Path}?token={Uri.EscapeDataString(token)}");
}
