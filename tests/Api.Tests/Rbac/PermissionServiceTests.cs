using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Template.Api.Services;
using Template.Api.Tests.Infrastructure;
using Template.Core.Authorization;
using Template.Core.Entities;
using Template.Infrastructure.Repositories;

namespace Template.Api.Tests.Rbac;

/// <summary>
/// RBAC-1 (ADR-009): <see cref="PermissionService"/> resolves the authenticated caller's membership
/// (by the NameIdentifier claim) and answers "does the caller's role grant this permission?" via the
/// <see cref="RolePermissions"/> matrix. Fails closed when there is no principal or no membership.
/// Postgres-backed so the real repository membership lookup is exercised.
/// </summary>
[Collection(PostgresCollection.Name)]
public class PermissionServiceTests(PostgresFixture fixture) : PostgresTestBase(fixture)
{
    [Theory]
    [InlineData(Permission.ManageBilling)]
    [InlineData(Permission.ManageRoles)]
    [InlineData(Permission.ManageMembers)]
    [InlineData(Permission.ViewTenant)]
    public async Task Owner_HasEveryPermission(Permission permission)
    {
        var ownerId = await SeedMembershipAsync(Guid.CreateVersion7(), TenantRoles.Owner);
        Assert.True(await NewService(ownerId).HasAsync(permission));
    }

    [Theory]
    [InlineData(Permission.ManageMembers, true)]
    [InlineData(Permission.RenameTenant, true)]
    [InlineData(Permission.ViewTenant, true)]
    [InlineData(Permission.ManageBilling, false)]
    [InlineData(Permission.ManageRoles, false)]
    [InlineData(Permission.TransferOwnership, false)]
    public async Task Admin_HasManagementButNotOwnerOnly(Permission permission, bool granted)
    {
        var tenantId = Guid.CreateVersion7();
        await SeedMembershipAsync(tenantId, TenantRoles.Owner);
        var adminId = await SeedMembershipAsync(tenantId, TenantRoles.Admin);
        Assert.Equal(granted, await NewService(adminId).HasAsync(permission));
    }

    [Theory]
    [InlineData(Permission.ViewTenant, true)]
    [InlineData(Permission.ManageMembers, false)]
    [InlineData(Permission.ManageBilling, false)]
    public async Task Member_HasOnlyViewTenant(Permission permission, bool granted)
    {
        var tenantId = Guid.CreateVersion7();
        await SeedMembershipAsync(tenantId, TenantRoles.Owner);
        var memberId = await SeedMembershipAsync(tenantId, TenantRoles.Member);
        Assert.Equal(granted, await NewService(memberId).HasAsync(permission));
    }

    [Fact]
    public async Task NoMembership_FailsClosed()
    {
        Assert.False(await NewService(Guid.CreateVersion7()).HasAsync(Permission.ViewTenant));
    }

    [Fact]
    public async Task NoAuthenticatedUser_FailsClosed()
    {
        var service = new PermissionService(
            new HttpContextAccessor { HttpContext = new DefaultHttpContext() },
            new TenantRepository(Fixture.CreateContext()));

        Assert.False(await service.HasAsync(Permission.ViewTenant));
    }

    private PermissionService NewService(Guid currentUserId)
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, currentUserId.ToString())], authenticationType: "test"));
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = user } };
        return new PermissionService(accessor, new TenantRepository(Fixture.CreateContext()));
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
