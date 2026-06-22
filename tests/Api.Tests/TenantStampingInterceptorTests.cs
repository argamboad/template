using Microsoft.EntityFrameworkCore;
using Template.Api.Tests.Infrastructure;
using Template.Core.Entities;

namespace Template.Api.Tests;

/// <summary>
/// Proves tenant isolation is structural on the WRITE side too: the stamping interceptor
/// stamps the current tenant onto a new <see cref="ITenantScoped"/> row that left
/// <c>TenantId</c> unset, and fails closed (throws, persists nothing) if a row carries a
/// TenantId belonging to a different tenant. Together with the read-side global query
/// filter this closes the "a feature can silently persist under the wrong tenant" hazard
/// (CONF-1).
/// </summary>
[Collection(PostgresCollection.Name)]
public class TenantStampingInterceptorTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Insert_WithoutTenantId_StampsCurrentTenant()
    {
        await fixture.ResetAsync();
        var tenant = Guid.CreateVersion7();

        await using (var db = fixture.CreateContext(tenant))
        {
            // TenantId deliberately left default — the interceptor must stamp it.
            db.Notes.Add(new Note { Title = "no tenant set" });
            await db.SaveChangesAsync();
        }

        await using var read = fixture.CreateContext();
        var note = await read.Notes.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(tenant, note.TenantId);
    }

    [Fact]
    public async Task Insert_WithForeignTenantId_Throws_AndPersistsNothing()
    {
        await fixture.ResetAsync();
        var current = Guid.CreateVersion7();
        var foreignTenant = Guid.CreateVersion7();

        await using (var db = fixture.CreateContext(current))
        {
            db.Notes.Add(new Note { Title = "wrong tenant", TenantId = foreignTenant });
            await Assert.ThrowsAnyAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        }

        await using var read = fixture.CreateContext();
        Assert.Empty(await read.Notes.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task Insert_WithMatchingTenantId_IsAllowed()
    {
        await fixture.ResetAsync();
        var tenant = Guid.CreateVersion7();

        await using (var db = fixture.CreateContext(tenant))
        {
            db.Notes.Add(new Note { Title = "explicit but correct", TenantId = tenant });
            await db.SaveChangesAsync();
        }

        await using var read = fixture.CreateContext();
        Assert.Equal(tenant, (await read.Notes.IgnoreQueryFilters().SingleAsync()).TenantId);
    }
}
