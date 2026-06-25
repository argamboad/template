using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Template.Api.Services;
using Template.Api.Tests.Infrastructure;
using Template.Core.Abstractions;
using Template.Core.Billing;
using Template.Core.Entities;
using Template.Infrastructure.Billing;
using Template.Infrastructure.Inbox;
using Template.Infrastructure.Persistence;
using Template.Infrastructure.Repositories;

namespace Template.Api.Tests.Billing;

/// <summary>
/// Drives BILLING-3 (ADR-006): the webhook is what actually grants access. Verifies signature
/// rejection, inbox idempotency (ADR-007), fail-closed downgrades, and that the write lands under the
/// right tenant via <c>EnterTenant</c> (ADR-003) — through the normal tenant-scoped path, no escape
/// hatch. Offline via <see cref="FakeBillingProvider"/>; real Postgres for the inbox + projection.
/// </summary>
[Collection(PostgresCollection.Name)]
public class BillingWebhookHandlerTests(PostgresFixture fixture) : PostgresTestBase(fixture)
{
    [Fact]
    public async Task SubscriptionActivated_FlipsTenantToActivePlan()
    {
        var tenant = Guid.CreateVersion7();

        Assert.Equal(WebhookResult.Applied, await HandleAsync(Event(tenant, SubscriptionStatus.Active)));

        Assert.True(await EntitledAsync(tenant));
        await using var read = Fixture.CreateContext(tenant);
        var sub = await read.Set<Subscription>().SingleAsync();
        Assert.Equal(SubscriptionStatus.Active, sub.Status);
        Assert.Equal(tenant, sub.TenantId);
    }

    [Fact]
    public async Task DuplicateEvent_IsIdempotent_NoDoubleApply()
    {
        var tenant = Guid.CreateVersion7();
        var evt = Event(tenant, SubscriptionStatus.Active);

        Assert.Equal(WebhookResult.Applied, await HandleAsync(evt));
        Assert.Equal(WebhookResult.Duplicate, await HandleAsync(evt)); // same EventId

        await using var read = Fixture.CreateContext();
        Assert.Equal(1, await read.Set<Subscription>().IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task InvalidSignature_IsRejected_NothingApplied()
    {
        var tenant = Guid.CreateVersion7();

        Assert.Equal(WebhookResult.InvalidSignature, await HandleAsync(Event(tenant, SubscriptionStatus.Active), signature: "bad"));

        await using var read = Fixture.CreateContext();
        Assert.Empty(await read.Set<Subscription>().IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task CanceledSubscription_FailsClosedToFree()
    {
        var tenant = Guid.CreateVersion7();
        await HandleAsync(Event(tenant, SubscriptionStatus.Active, eventId: "evt_1"));
        Assert.True(await EntitledAsync(tenant));

        await HandleAsync(Event(tenant, SubscriptionStatus.Canceled, eventId: "evt_2"));
        Assert.False(await EntitledAsync(tenant)); // fail closed
    }

    [Fact]
    public async Task Applied_OnlyVisibleToItsOwnTenant()
    {
        var tenant = Guid.CreateVersion7();
        await HandleAsync(Event(tenant, SubscriptionStatus.Active));

        await using var other = Fixture.CreateContext(Guid.CreateVersion7());
        Assert.Empty(await other.Set<Subscription>().ToListAsync()); // scoped by EnterTenant, not leaked
    }

    // --- helpers ---

    private static BillingWebhookEvent Event(Guid tenant, string status, string eventId = "evt_default") =>
        new(eventId, tenant, PlanKeys.Pro, status, "cus_1", "sub_1", DateTimeOffset.UtcNow.AddDays(30));

    /// <summary>Fresh context+handler per call (a webhook delivery is its own scope); DB is shared.</summary>
    private async Task<WebhookResult> HandleAsync(BillingWebhookEvent evt, string signature = FakeBillingProvider.ValidSignature)
    {
        var current = new HttpCurrentTenant(new HttpContextAccessor()); // no JWT
        await using var db = NewContext(current);
        var handler = new BillingWebhookHandler(
            new FakeBillingProvider(),
            new EfInbox(db, TimeProvider.System),
            new EfRepository<Subscription>(db),
            current,
            new EfUnitOfWork(db),
            TimeProvider.System);

        return await handler.HandleAsync(JsonSerializer.Serialize(evt), signature, default);
    }

    private async Task<bool> EntitledAsync(Guid tenant)
    {
        await using var db = Fixture.CreateContext(tenant);
        return await new EntitlementService(new EfRepository<Subscription>(db), TimeProvider.System)
            .HasAsync(Entitlements.ProFeature);
    }

    private AppDbContext NewContext(ICurrentTenant currentTenant) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(Fixture.ConnectionString).Options, currentTenant);
}
