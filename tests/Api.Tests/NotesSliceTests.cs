using Template.Api.Features.Notes;
using Template.Api.Tests.Infrastructure;
using Template.Core.Entities;
using Template.Infrastructure.Repositories;

namespace Template.Api.Tests;

/// <summary>
/// Reference-slice tests for the sample Notes feature: it's tenant-scoped purely through
/// the platform (generic repository + global query filter — no per-query Where(TenantId)),
/// and its <see cref="NotesDataContributor"/> participates in tenant dissolve. This is the
/// behavior every real feature slice inherits for free.
/// </summary>
[Collection(PostgresCollection.Name)]
public class NotesSliceTests(PostgresFixture fixture)
{
    private static NotesHandler Handler(Template.Infrastructure.Persistence.AppDbContext db, Guid tenantId) =>
        new(new EfRepository<Note>(db), new TestCurrentTenant { TenantId = tenantId });

    [Fact]
    public async Task Notes_AreVisibleOnlyToTheirTenant()
    {
        await fixture.ResetAsync();
        var tenantA = Guid.CreateVersion7();
        var tenantB = Guid.CreateVersion7();

        await using (var db = fixture.CreateContext(tenantA))
            Assert.NotNull(await Handler(db, tenantA).CreateAsync(new CreateNoteRequest("A's note", "hi"), default));

        await using (var db = fixture.CreateContext(tenantB))
            Assert.Empty(await Handler(db, tenantB).ListAsync(default));   // B sees nothing

        await using (var db = fixture.CreateContext(tenantA))
            Assert.Single(await Handler(db, tenantA).ListAsync(default));  // A sees its own
    }

    [Fact]
    public async Task Create_BlankTitle_IsRejected()
    {
        await fixture.ResetAsync();
        var tenant = Guid.CreateVersion7();

        await using var db = fixture.CreateContext(tenant);
        Assert.Null(await Handler(db, tenant).CreateAsync(new CreateNoteRequest("  ", null), default));
    }

    [Fact]
    public async Task Contributor_ReportsAndWipesTenantData()
    {
        await fixture.ResetAsync();
        var tenant = Guid.CreateVersion7();

        await using (var db = fixture.CreateContext(tenant))
            await Handler(db, tenant).CreateAsync(new CreateNoteRequest("keep", null), default);

        // The contributor runs for arbitrary tenants (dissolve), so it ignores the filter.
        await using (var db = fixture.CreateContext())
        {
            var contributor = new NotesDataContributor(new EfRepository<Note>(db));
            Assert.True(await contributor.HasDataAsync(tenant));
            await contributor.WipeAsync(tenant);
            Assert.False(await contributor.HasDataAsync(tenant));
        }
    }
}
