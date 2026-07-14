using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Perezosoft.Api.Services;
using Perezosoft.Api.Tests.Infrastructure;
using Perezosoft.Core.Billing;
using Perezosoft.Core.Entities;
using Perezosoft.Infrastructure.Persistence;
using Perezosoft.Infrastructure.Repositories;

namespace Perezosoft.Api.Tests.Billing;

/// <summary>
/// Drives BILLING-6 (ADR-006/007): the scheduled lapse sweep. A subscription still marked active/trialing
/// whose paid period ended (no webhook) gets the owner a **one-time** "expired" nudge; the sweep records
/// that it notified (<c>LapseNotifiedAt</c>) so a second run is a no-op, and it never touches an
/// in-period subscription. Cross-tenant scan via <c>QueryAllTenants</c>, notify inside <c>EnterTenant</c>.
/// </summary>
[Collection(PostgresCollection.Name)]
public class SubscriptionLapseSweepJobTests(PostgresFixture fixture) : PostgresTestBase(fixture)
{
    [Fact]
    public async Task LapsedSubscription_NotifiesOwnerOnce_AndStamps()
    {
        var tenant = Guid.CreateVersion7();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero));
        var ownerId = await SeedAsync(tenant, SubscriptionStatus.Active, periodEnd: clock.GetUtcNow().AddDays(-1));

        await RunSweepAsync(clock);

        // Owner nudged exactly once, and the stamp recorded.
        await using var read = Fixture.CreateContext(tenant);
        var note = Assert.Single(await read.Set<Notification>().Where(n => n.UserId == ownerId).ToListAsync());
        Assert.Equal(BillingNotifications.LapsedKind, note.Kind);
        var sub = await read.Set<Subscription>().SingleAsync();
        Assert.NotNull(sub.LapseNotifiedAt);

        // A second sweep does not re-notify (idempotent per lapse).
        await RunSweepAsync(clock);
        await using var read2 = Fixture.CreateContext(tenant);
        Assert.Single(await read2.Set<Notification>().Where(n => n.UserId == ownerId).ToListAsync());
    }

    [Fact]
    public async Task InPeriodSubscription_IsNotNudged()
    {
        var tenant = Guid.CreateVersion7();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero));
        var ownerId = await SeedAsync(tenant, SubscriptionStatus.Active, periodEnd: clock.GetUtcNow().AddDays(10));

        await RunSweepAsync(clock);

        await using var read = Fixture.CreateContext(tenant);
        Assert.Empty(await read.Set<Notification>().Where(n => n.UserId == ownerId).ToListAsync());
    }

    // --- helpers ---

    private async Task RunSweepAsync(TimeProvider clock)
    {
        var ctx = new HttpCurrentTenant(new HttpContextAccessor()); // no ambient tenant; job enters each
        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(Fixture.ConnectionString).Options, ctx);

        var notifier = new BillingNotifier(
            new TenantRepository(db),
            new NotificationService(
                new EfRepository<Notification>(db), new EfRepository<NotificationPreference>(db),
                new UserRepository(db), new NoopEmailSender(), clock));

        var job = new SubscriptionLapseSweepJob(
            new EfRepository<Subscription>(db), ctx, notifier, clock, NullLogger<SubscriptionLapseSweepJob>.Instance);

        await job.RunAsync();
    }

    private async Task<Guid> SeedAsync(Guid tenant, string status, DateTimeOffset periodEnd)
    {
        var ownerId = Guid.CreateVersion7();
        await using var db = Fixture.CreateContext(tenant); // interceptor stamps TenantId on the subscription
        db.Set<Tenant>().Add(new Tenant { Id = tenant, Name = "T", CreatedAt = DateTimeOffset.UtcNow });
        db.Set<User>().Add(new User { Id = ownerId, Email = $"owner-{ownerId:N}@x.com" });
        db.Set<TenantMembership>().Add(new TenantMembership { TenantId = tenant, UserId = ownerId, Role = TenantRoles.Owner });
        db.Set<Subscription>().Add(new Subscription
        {
            PlanKey = PlanKeys.Pro,
            Status = status,
            CurrentPeriodEnd = periodEnd,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
        return ownerId;
    }
}
