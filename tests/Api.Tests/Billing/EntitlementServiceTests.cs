using Microsoft.Extensions.Time.Testing;
using Perezosoft.Api.Services;
using Perezosoft.Api.Tests.Infrastructure;
using Perezosoft.Core.Billing;
using Perezosoft.Core.Entities;
using Perezosoft.Infrastructure.Repositories;

namespace Perezosoft.Api.Tests.Billing;

/// <summary>
/// Drives BILLING-1 (ADR-006): entitlements resolve from the tenant's Subscription projection and
/// **fail closed to Free** — no subscription, a non-active status, or a lapsed period never grant a
/// paid entitlement. Real Postgres so the tenant-scoped read goes through the global query filter.
/// </summary>
[Collection(PostgresCollection.Name)]
public class EntitlementServiceTests(PostgresFixture fixture) : PostgresTestBase(fixture)
{
    [Fact]
    public async Task NoSubscription_TreatedAsFree_DeniesProFeature()
    {
        var tenant = Guid.CreateVersion7();
        await using var db = Fixture.CreateContext(tenant);
        var service = new EntitlementService(new EfRepository<Subscription>(db), TimeProvider.System);

        Assert.False(await service.HasAsync(Entitlements.ProFeature));
    }

    [Fact]
    public async Task ActiveProSubscription_GrantsProFeature()
    {
        var tenant = Guid.CreateVersion7();
        await SeedAsync(tenant, PlanKeys.Pro, SubscriptionStatus.Active, periodEnd: null);

        await using var db = Fixture.CreateContext(tenant);
        var service = new EntitlementService(new EfRepository<Subscription>(db), TimeProvider.System);

        Assert.True(await service.HasAsync(Entitlements.ProFeature));
    }

    [Fact]
    public async Task TrialingSubscription_GrantsProFeature()
    {
        var tenant = Guid.CreateVersion7();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 6, 25, 0, 0, 0, TimeSpan.Zero));
        await SeedAsync(tenant, PlanKeys.Pro, SubscriptionStatus.Trialing, periodEnd: clock.GetUtcNow().AddDays(7));

        await using var db = Fixture.CreateContext(tenant);
        var service = new EntitlementService(new EfRepository<Subscription>(db), clock);

        Assert.True(await service.HasAsync(Entitlements.ProFeature));
    }

    [Theory]
    [InlineData(SubscriptionStatus.PastDue)]
    [InlineData(SubscriptionStatus.Canceled)]
    public async Task InactiveSubscription_FailsClosedToFree(string status)
    {
        var tenant = Guid.CreateVersion7();
        await SeedAsync(tenant, PlanKeys.Pro, status, periodEnd: null);

        await using var db = Fixture.CreateContext(tenant);
        var service = new EntitlementService(new EfRepository<Subscription>(db), TimeProvider.System);

        Assert.False(await service.HasAsync(Entitlements.ProFeature));
    }

    [Fact]
    public async Task ActiveButPeriodEnded_FailsClosedToFree()
    {
        var tenant = Guid.CreateVersion7();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 6, 25, 0, 0, 0, TimeSpan.Zero));
        await SeedAsync(tenant, PlanKeys.Pro, SubscriptionStatus.Active, periodEnd: clock.GetUtcNow().AddDays(-1));

        await using var db = Fixture.CreateContext(tenant);
        var service = new EntitlementService(new EfRepository<Subscription>(db), clock);

        Assert.False(await service.HasAsync(Entitlements.ProFeature));
    }

    private async Task SeedAsync(Guid tenant, string plan, string status, DateTimeOffset? periodEnd)
    {
        await using var db = Fixture.CreateContext(tenant); // interceptor stamps TenantId
        db.Set<Subscription>().Add(new Subscription
        {
            PlanKey = plan,
            Status = status,
            CurrentPeriodEnd = periodEnd,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }
}
