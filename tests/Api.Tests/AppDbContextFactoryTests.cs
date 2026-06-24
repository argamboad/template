using Microsoft.EntityFrameworkCore;
using Template.Core.Entities;
using Template.Infrastructure.Persistence;

namespace Template.Api.Tests;

/// <summary>
/// Guards the design-time DbContext factory that EF tooling uses for migrations /
/// <c>has-pending-model-changes</c>. The factory must build the context from a connection string
/// alone — NEVER via the API host — so <c>dotnet ef</c> doesn't trip the host's runtime config
/// validation (e.g. <c>Jwt:Secret</c>), which is absent at design time and in CI. No DB is touched:
/// the model is built in-memory, mirroring the offline model-diff CI step.
/// </summary>
public class AppDbContextFactoryTests
{
    [Fact]
    public void CreateDbContext_BuildsContext_WithoutHostOrRuntimeConfig()
    {
        // No Jwt:Secret / connection-string env configured — exactly the design-time/CI situation
        // that previously failed when EF routed through the API host.
        var context = new AppDbContextFactory().CreateDbContext([]);

        Assert.NotNull(context);
        // The model resolves — proves the context is usable for migration/model-diff operations.
        Assert.NotNull(context.Model.FindEntityType(typeof(User)));
        Assert.NotNull(context.Model.FindEntityType(typeof(Tenant)));
    }
}
