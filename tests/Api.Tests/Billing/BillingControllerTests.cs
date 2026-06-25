using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Template.Api.Controllers;
using Template.Api.Models;
using Template.Api.Services;
using Template.Api.Tests.Infrastructure;
using Template.Core.Billing;
using Template.Core.Entities;
using Template.Infrastructure.Billing;
using Template.Infrastructure.Persistence;
using Template.Infrastructure.Repositories;

namespace Template.Api.Tests.Billing;

/// <summary>
/// BILLING-2 owner gate on the platform <see cref="BillingController"/>: only the tenant <b>owner</b>
/// can start checkout. Exercises the controller through the real <c>TenantApiControllerBase</c> gate
/// with a Postgres-backed membership lookup and a claims principal for the caller.
/// </summary>
[Collection(PostgresCollection.Name)]
public class BillingControllerTests(PostgresFixture fixture) : PostgresTestBase(fixture)
{
    [Fact]
    public async Task Owner_Checkout_ReturnsOkWithUrl()
    {
        var ownerId = await SeedMembershipAsync(Guid.CreateVersion7(), TenantRoles.Owner);
        await using var db = Fixture.CreateContext();
        var fake = new FakeBillingProvider();
        var controller = NewController(db, fake, ownerId);

        var result = await controller.Checkout(new CreateCheckoutRequest { PlanKey = PlanKeys.Pro }, default);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.IsType<CheckoutResponse>(ok.Value);
        Assert.Single(fake.Requests);
    }

    [Fact]
    public async Task Member_Checkout_Returns403_NoSession()
    {
        var tenantId = Guid.CreateVersion7();
        await SeedMembershipAsync(tenantId, TenantRoles.Owner);            // tenant must have an owner
        var memberId = await SeedMembershipAsync(tenantId, TenantRoles.Member);
        await using var db = Fixture.CreateContext();
        var fake = new FakeBillingProvider();
        var controller = NewController(db, fake, memberId);

        var result = await controller.Checkout(new CreateCheckoutRequest { PlanKey = PlanKeys.Pro }, default);

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, obj.StatusCode);
        Assert.Empty(fake.Requests);
    }

    [Fact]
    public async Task NoMembership_Returns401()
    {
        await using var db = Fixture.CreateContext();
        var controller = NewController(db, new FakeBillingProvider(), Guid.CreateVersion7());

        var result = await controller.Checkout(new CreateCheckoutRequest { PlanKey = PlanKeys.Pro }, default);

        Assert.IsType<UnauthorizedObjectResult>(result);
    }

    private static BillingController NewController(AppDbContext db, FakeBillingProvider billing, Guid currentUserId)
    {
        var controller = new BillingController(
            new TenantRepository(db),
            new ErrorResponseFactory(),
            new BillingService(billing, new TestAppSettings()));

        var user = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, currentUserId.ToString())], authenticationType: "test"));
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = user } };
        return controller;
    }

    /// <summary>Adds a (Tenant, User, TenantMembership) and returns the new user id. Not tenant-scoped.</summary>
    private async Task<Guid> SeedMembershipAsync(Guid tenantId, string role)
    {
        var userId = Guid.CreateVersion7();
        await using var db = Fixture.CreateContext();
        if (!await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.AnyAsync(db.Set<Tenant>(), t => t.Id == tenantId))
            db.Set<Tenant>().Add(new Tenant { Id = tenantId, Name = "Tenant" });
        db.Set<User>().Add(new User { Id = userId, Email = $"u-{userId:N}@x.com" });
        db.Set<TenantMembership>().Add(new TenantMembership { TenantId = tenantId, UserId = userId, Role = role });
        await db.SaveChangesAsync();
        return userId;
    }
}
