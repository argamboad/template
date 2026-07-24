using Bunit;
using Perezosoft.Shared.Ui.Components;
using Perezosoft.Shared.Ui.Pages;
using Perezosoft.Ui.Tests.Infrastructure;
using Xunit;

namespace Perezosoft.Ui.Tests;

/// <summary>
/// v3 audit LB-UI-9 (bell double-decrement) + UX-5 (raw billing tokens), T42.
/// </summary>
public class NotifyBillingTests : ComponentTestBase
{
    private const string ItemA = "aaaaaaaa-0000-0000-0000-000000000001";
    private const string ItemB = "bbbbbbbb-0000-0000-0000-000000000002";

    [Fact]
    public async Task Bell_DoubleClickUnread_DecrementsOnce_NotTwice()
    {
        await SignInAsync();
        static string Notif(string id, string title) =>
            $$"""{"id":"{{id}}","kind":"x","title":"{{title}}","body":"","read_at":null,"created_at":"2026-01-01T00:00:00Z"}""";
        Http.On(HttpMethod.Get, "/api/notifications/unread-count", """{"count":2}""");
        Http.On(HttpMethod.Get, "/api/notifications", $"[{Notif(ItemA, "A")},{Notif(ItemB, "B")}]");
        // The read POST hangs, so a second click lands while the first is still in flight (the race).
        var releaseRead = Http.OnGated(HttpMethod.Post, $"/api/notifications/{ItemA}/read");

        var cut = Render<NotificationBell>();
        cut.Find("[data-testid='notif-bell']").Click(); // open the dropdown → loads the list

        // Re-find before each click: the optimistic mark re-renders the item (new handler id). The second
        // click lands while the first read POST is still gated — the race that used to double-decrement.
        cut.FindAll("[data-testid='notif-item']")[0].Click();
        cut.FindAll("[data-testid='notif-item']")[0].Click();
        releaseRead();  // let the first POST complete

        // Two unread → reading ONE leaves exactly one unread (a double-decrement would show 0).
        Assert.Equal("1", cut.Find("[data-testid='notif-count']").TextContent.Trim());
    }

    [Fact]
    public async Task Billing_RendersLocalizedPlanAndStatus_NotRawTokens()
    {
        await SignInAsync();
        Http.On(HttpMethod.Get, "/api/billing", """{"plan_key":"free","status":"active"}""");

        var cut = Render<Billing>();
        cut.WaitForElement("[data-testid='billing-plan']");

        // The fake localizer echoes the key, proving the token is looked up (not rendered raw).
        Assert.Equal("Plan_free", cut.Find("[data-testid='billing-plan']").TextContent.Trim());
        Assert.Equal("BillingStatus_active", cut.Find("[data-testid='billing-status']").TextContent.Trim());
    }
}
