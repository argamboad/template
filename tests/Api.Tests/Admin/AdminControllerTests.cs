using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Template.Api.Configuration;
using Template.Api.Controllers;
using Template.Api.Models;
using Template.Api.Services;
using Template.Api.Tests.Infrastructure;
using Template.Core.Billing;
using Template.Core.Entities;
using Template.Infrastructure.Audit;
using Template.Infrastructure.Persistence;
using Template.Infrastructure.Repositories;

namespace Template.Api.Tests.Admin;

/// <summary>
/// ADMIN-1 (ADR-014): the staff back-office. Non-staff callers get 403; staff can list tenants and view
/// a tenant's detail. The detail read <b>enters the target tenant</b> (so its scoped data reads through
/// the normal filter) and is <b>audited in that tenant</b>. The controller shares one
/// <c>ICurrentTenant</c>/<c>ITenantContext</c> with its DbContext, exactly as in production.
/// </summary>
[Collection(PostgresCollection.Name)]
public class AdminControllerTests(PostgresFixture fixture) : PostgresTestBase(fixture)
{
    private const string StaffEmail = "staff@corp.com";

    [Fact]
    public async Task NonStaff_ListTenants_Returns403()
    {
        var callerId = await SeedUserAsync("normal@corp.com");
        var (controller, db) = BuildController(callerId);
        await using (db)
        {
            var result = await controller.ListTenants(default);
            var obj = Assert.IsType<ObjectResult>(result);
            Assert.Equal(StatusCodes.Status403Forbidden, obj.StatusCode);
        }
    }

    [Fact]
    public async Task Staff_ListTenants_ReturnsAllTenantsWithCounts()
    {
        var staffId = await SeedUserAsync(StaffEmail);
        var t1 = await SeedTenantAsync("Alpha", members: 2);
        var t2 = await SeedTenantAsync("Beta", members: 1);

        var (controller, db) = BuildController(staffId);
        await using (db)
        {
            var ok = Assert.IsType<OkObjectResult>(await controller.ListTenants(default));
            var list = Assert.IsAssignableFrom<IReadOnlyList<AdminTenantSummaryResponse>>(ok.Value);
            Assert.Contains(list, t => t.Id == t1 && t.MemberCount == 2);
            Assert.Contains(list, t => t.Id == t2 && t.MemberCount == 1);
        }
    }

    [Fact]
    public async Task Staff_TenantDetail_ReturnsDetail_AndAuditsInTenant()
    {
        var staffId = await SeedUserAsync(StaffEmail);
        var tenantId = await SeedTenantAsync("Gamma", members: 2);
        await SeedSubscriptionAsync(tenantId);

        var (controller, db) = BuildController(staffId);
        await using (db)
        {
            var ok = Assert.IsType<OkObjectResult>(await controller.TenantDetail(tenantId, default));
            var detail = Assert.IsType<AdminTenantDetailResponse>(ok.Value);
            Assert.Equal(2, detail.Members.Count);
            Assert.Equal(SubscriptionStatus.Active, detail.SubscriptionStatus); // "active"
        }

        // The view is audited in the target tenant.
        await using var read = Fixture.CreateContext(tenantId);
        Assert.True(await read.Set<AuditEvent>().AnyAsync(e => e.Action == "admin.tenant.viewed" && e.ActorUserId == staffId));
    }

    [Fact]
    public async Task Staff_TenantDetail_UnknownTenant_Returns404()
    {
        var staffId = await SeedUserAsync(StaffEmail);
        var (controller, db) = BuildController(staffId);
        await using (db)
            Assert.IsType<NotFoundObjectResult>(await controller.TenantDetail(Guid.CreateVersion7(), default));
    }

    // --- staff status probe (UI-4): 200 for any authenticated caller, never 403 ---

    [Fact]
    public async Task Staff_Me_ReturnsIsStaffTrue()
    {
        var staffId = await SeedUserAsync(StaffEmail);
        var (controller, db) = BuildController(staffId);
        await using (db)
        {
            var ok = Assert.IsType<OkObjectResult>(await controller.Me(default));
            Assert.True(Assert.IsType<AdminStatusResponse>(ok.Value).IsStaff);
        }
    }

    [Fact]
    public async Task NonStaff_Me_ReturnsIsStaffFalse_Not403()
    {
        var callerId = await SeedUserAsync("normal@corp.com");
        var (controller, db) = BuildController(callerId);
        await using (db)
        {
            var ok = Assert.IsType<OkObjectResult>(await controller.Me(default));
            Assert.False(Assert.IsType<AdminStatusResponse>(ok.Value).IsStaff);
        }
    }

    // --- ADMIN-2: impersonation ---

    [Fact]
    public async Task NonStaff_Impersonate_Returns403()
    {
        var callerId = await SeedUserAsync("normal@corp.com");
        var targetId = await SeedUserInTenantAsync();
        var (controller, db) = BuildController(callerId);
        await using (db)
        {
            var obj = Assert.IsType<ObjectResult>(await controller.Impersonate(targetId, default));
            Assert.Equal(StatusCodes.Status403Forbidden, obj.StatusCode);
        }
    }

    [Fact]
    public async Task Staff_Impersonate_ReturnsShortLivedTaggedToken_AndAudits()
    {
        var staffId = await SeedUserAsync(StaffEmail);
        var (targetId, tenantId) = await SeedUserInTenantWithIdAsync();

        var (controller, db) = BuildController(staffId);
        await using (db)
        {
            var ok = Assert.IsType<OkObjectResult>(await controller.Impersonate(targetId, default));
            var resp = Assert.IsType<ImpersonationResponse>(ok.Value);
            Assert.Equal(900, resp.ExpiresIn); // 15 min, non-refreshable (no refresh token returned)

            var validator = new JwtTokenService(new TestJwtSettings(), TimeProvider.System, NullLogger<JwtTokenService>.Instance);
            var principal = validator.ValidateToken(resp.AccessToken);
            Assert.NotNull(principal);
            Assert.Equal(targetId.ToString(), principal!.FindFirstValue(ClaimTypes.NameIdentifier));
            Assert.Equal(staffId.ToString(), principal.FindFirstValue(JwtClaims.ImpersonatedBy));
            Assert.Equal(tenantId.ToString(), principal.FindFirstValue(JwtClaims.TenantId));
        }

        await using var read = Fixture.CreateContext(tenantId);
        Assert.True(await read.Set<AuditEvent>().AnyAsync(e => e.Action == "admin.impersonation.started" && e.ActorUserId == staffId));
    }

    [Fact]
    public async Task Staff_Impersonate_UnknownUser_Returns404()
    {
        var staffId = await SeedUserAsync(StaffEmail);
        var (controller, db) = BuildController(staffId);
        await using (db)
            Assert.IsType<NotFoundObjectResult>(await controller.Impersonate(Guid.CreateVersion7(), default));
    }

    // --- construction (shared tenant context between controller + DbContext) ---

    private (AdminController controller, AppDbContext db) BuildController(Guid callerId)
    {
        var ctx = new HttpCurrentTenant(new HttpContextAccessor { HttpContext = new DefaultHttpContext() });
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(Fixture.ConnectionString).Options;
        var db = new AppDbContext(options, ctx);

        var staff = new PlatformStaffService(new UserRepository(db), Options.Create(new PlatformAdminSettings { StaffEmails = [StaffEmail] }));
        var controller = new AdminController(
            staff, new TenantRepository(db), ctx,
            new AuditLog(new EfRepository<AuditEvent>(db), TimeProvider.System),
            new EfRepository<Subscription>(db), new EfRepository<AuditEvent>(db),
            new UserRepository(db),
            new JwtTokenService(new TestJwtSettings(), TimeProvider.System, NullLogger<JwtTokenService>.Instance));

        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, callerId.ToString())], "test"));
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = user } };
        return (controller, db);
    }

    // --- seeding ---

    private async Task<Guid> SeedUserAsync(string email)
    {
        var user = new User { Id = Guid.CreateVersion7(), Email = email };
        await using var db = Fixture.CreateContext();
        db.Set<User>().Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    private async Task<Guid> SeedTenantAsync(string name, int members)
    {
        var tenantId = Guid.CreateVersion7();
        await using var db = Fixture.CreateContext();
        db.Set<Tenant>().Add(new Tenant { Id = tenantId, Name = name, CreatedAt = DateTimeOffset.UtcNow });
        for (var i = 0; i < members; i++)
        {
            var uid = Guid.CreateVersion7();
            db.Set<User>().Add(new User { Id = uid, Email = $"m-{uid:N}@x.com" });
            db.Set<TenantMembership>().Add(new TenantMembership { TenantId = tenantId, UserId = uid, Role = i == 0 ? TenantRoles.Owner : TenantRoles.Member });
        }
        await db.SaveChangesAsync();
        return tenantId;
    }

    private async Task<Guid> SeedUserInTenantAsync() => (await SeedUserInTenantWithIdAsync()).userId;

    private async Task<(Guid userId, Guid tenantId)> SeedUserInTenantWithIdAsync()
    {
        var tenantId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        await using var db = Fixture.CreateContext();
        db.Set<Tenant>().Add(new Tenant { Id = tenantId, Name = "T", CreatedAt = DateTimeOffset.UtcNow });
        db.Set<User>().Add(new User { Id = userId, Email = $"t-{userId:N}@x.com", DisplayName = "Target" });
        db.Set<TenantMembership>().Add(new TenantMembership { TenantId = tenantId, UserId = userId, Role = TenantRoles.Owner });
        await db.SaveChangesAsync();
        return (userId, tenantId);
    }

    private async Task SeedSubscriptionAsync(Guid tenantId)
    {
        await using var db = Fixture.CreateContext(tenantId); // interceptor stamps TenantId
        db.Set<Subscription>().Add(new Subscription
        {
            PlanKey = PlanKeys.Pro,
            Status = SubscriptionStatus.Active,
            StripeCustomerId = "cus_x",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }
}
