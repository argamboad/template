using Template.Api.Features.Billing;
using Template.Api.Services;
using Template.Api.Tests.Infrastructure;
using Template.Core.Billing;
using Template.Core.Entities;
using Template.Infrastructure.Billing;
using Template.Infrastructure.Repositories;

namespace Template.Api.Tests.Billing;

/// <summary>
/// Drives BILLING-2 (ADR-006): only a tenant <b>owner</b> can start checkout; the session carries the
/// tenant id; and completing checkout does <b>not</b> grant access (only the BILLING-3 webhook will).
/// Uses <see cref="FakeBillingProvider"/> (offline, zero charges) and real Postgres for the membership
/// lookup the owner gate depends on.
/// </summary>
[Collection(PostgresCollection.Name)]
public class BillingHandlerTests(PostgresFixture fixture) : PostgresTestBase(fixture)
{
    [Fact]
    public async Task Owner_StartsCheckout_SessionCarriesTenantAndPlan()
    {
        var (tenantId, ownerId) = await SeedOwnerAsync();
        await using var db = Fixture.CreateContext();
        var fake = new FakeBillingProvider();
        var handler = new BillingHandler(new TenantRepository(db), fake, new TestAppSettings());

        var result = await handler.CreateCheckoutAsync(ownerId, PlanKeys.Pro, default);

        Assert.Equal(CheckoutStatus.Created, result.Status);
        Assert.False(string.IsNullOrEmpty(result.Url));
        var request = Assert.Single(fake.Requests);
        Assert.Equal(tenantId, request.TenantId);
        Assert.Equal(PlanKeys.Pro, request.PlanKey);
    }

    [Fact]
    public async Task Member_CannotStartCheckout_403_NoSessionCreated()
    {
        var memberId = await SeedMemberAsync();
        await using var db = Fixture.CreateContext();
        var fake = new FakeBillingProvider();
        var handler = new BillingHandler(new TenantRepository(db), fake, new TestAppSettings());

        var result = await handler.CreateCheckoutAsync(memberId, PlanKeys.Pro, default);

        Assert.Equal(CheckoutStatus.Forbidden, result.Status);
        Assert.Empty(fake.Requests);
    }

    [Fact]
    public async Task NoMembership_IsUnauthenticated()
    {
        await using var db = Fixture.CreateContext();
        var handler = new BillingHandler(new TenantRepository(db), new FakeBillingProvider(), new TestAppSettings());

        var result = await handler.CreateCheckoutAsync(Guid.CreateVersion7(), PlanKeys.Pro, default);

        Assert.Equal(CheckoutStatus.Unauthenticated, result.Status);
    }

    [Theory]
    [InlineData(PlanKeys.Free)] // Free isn't purchasable
    [InlineData("gold")]        // unknown plan
    [InlineData(null)]
    public async Task UnknownOrFreePlan_IsRejected(string? planKey)
    {
        var (_, ownerId) = await SeedOwnerAsync();
        await using var db = Fixture.CreateContext();
        var fake = new FakeBillingProvider();
        var handler = new BillingHandler(new TenantRepository(db), fake, new TestAppSettings());

        var result = await handler.CreateCheckoutAsync(ownerId, planKey, default);

        Assert.Equal(CheckoutStatus.InvalidPlan, result.Status);
        Assert.Empty(fake.Requests);
    }

    [Fact]
    public async Task CompletingCheckout_DoesNotGrantAccess_UntilWebhook()
    {
        var (tenantId, ownerId) = await SeedOwnerAsync();

        await using (var db = Fixture.CreateContext())
            await new BillingHandler(new TenantRepository(db), new FakeBillingProvider(), new TestAppSettings())
                .CreateCheckoutAsync(ownerId, PlanKeys.Pro, default);

        // No Subscription was written, so the tenant is still Free.
        await using var read = Fixture.CreateContext(tenantId);
        var entitlements = new EntitlementService(new EfRepository<Subscription>(read), TimeProvider.System);
        Assert.False(await entitlements.HasAsync(Entitlements.ProFeature));
    }

    // --- seeding (Tenant/User/TenantMembership are not tenant-scoped, so a null-tenant context is fine) ---

    private async Task<(Guid tenantId, Guid ownerId)> SeedOwnerAsync()
    {
        var tenantId = Guid.CreateVersion7();
        var ownerId = await AddMembershipAsync(tenantId, TenantRoles.Owner);
        return (tenantId, ownerId);
    }

    private async Task<Guid> SeedMemberAsync()
    {
        var tenantId = Guid.CreateVersion7();
        await AddMembershipAsync(tenantId, TenantRoles.Owner);              // tenant must have an owner
        return await AddMembershipAsync(tenantId, TenantRoles.Member);      // the caller is a member
    }

    private async Task<Guid> AddMembershipAsync(Guid tenantId, string role)
    {
        var userId = Guid.CreateVersion7();
        await using var db = Fixture.CreateContext();
        if (!await ExistsAsync(db, tenantId))
            db.Set<Tenant>().Add(new Tenant { Id = tenantId, Name = "Tenant" });
        db.Set<User>().Add(new User { Id = userId, Email = $"u-{userId:N}@x.com" });
        db.Set<TenantMembership>().Add(new TenantMembership { TenantId = tenantId, UserId = userId, Role = role });
        await db.SaveChangesAsync();
        return userId;
    }

    private static async Task<bool> ExistsAsync(Template.Infrastructure.Persistence.AppDbContext db, Guid tenantId) =>
        await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.AnyAsync(db.Set<Tenant>(), t => t.Id == tenantId);
}
