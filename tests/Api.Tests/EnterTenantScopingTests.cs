using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Template.Api.Services;
using Template.Api.Tests.Infrastructure;
using Template.Core.Abstractions;
using Template.Core.Entities;
using Template.Infrastructure.Persistence;

namespace Template.Api.Tests;

/// <summary>
/// The structural proof for the <c>EnterTenant</c> primitive (ADR-003): a write made while a tenant is
/// <em>entered</em> (no JWT — a webhook-like context) is stamped and scoped by the normal interceptor +
/// global filter, exactly as an authenticated request would be — <b>no <c>IgnoreQueryFilters</c> /
/// escape hatch</b>. The companion test shows that without entering, the same context is a system
/// context that does not stamp — so entering is precisely what gives such operations safe scoping.
/// </summary>
[Collection(PostgresCollection.Name)]
public class EnterTenantScopingTests(PostgresFixture fixture) : PostgresTestBase(fixture)
{
    [Fact]
    public async Task EnteredTenant_StampsAndScopes_AWrite_WithoutEscapeHatch()
    {
        var tenant = Guid.CreateVersion7();
        var current = new HttpCurrentTenant(new HttpContextAccessor()); // no JWT

        await using (var db = NewContext(current))
        using (current.EnterTenant(tenant))
        {
            db.Notes.Add(new Note { Title = "from a webhook-like context" }); // TenantId left unset
            await db.SaveChangesAsync(); // interceptor stamps the ENTERED tenant
        }

        // Visible under the entered tenant via the normal global filter (no IgnoreQueryFilters).
        await using (var read = Fixture.CreateContext(tenant))
            Assert.Equal(tenant, (await read.Notes.SingleAsync()).TenantId);

        // Invisible to any other tenant.
        await using (var other = Fixture.CreateContext(Guid.CreateVersion7()))
            Assert.Empty(await other.Notes.ToListAsync());
    }

    [Fact]
    public async Task WithoutEntering_NoJwtContext_IsSystem_AndDoesNotStamp()
    {
        var current = new HttpCurrentTenant(new HttpContextAccessor()); // no JWT, no entered tenant

        await using (var db = NewContext(current))
        {
            db.Notes.Add(new Note { Title = "unstamped" });
            await db.SaveChangesAsync();
        }

        await using var read = Fixture.CreateContext();
        var note = await read.Notes.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(Guid.Empty, note.TenantId); // system context → not stamped
    }

    private AppDbContext NewContext(ICurrentTenant currentTenant) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(Fixture.ConnectionString).Options, currentTenant);
}
