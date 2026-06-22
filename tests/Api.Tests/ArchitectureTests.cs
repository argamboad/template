using Microsoft.EntityFrameworkCore;
using Template.Api.Tests.Infrastructure;
using Template.Core.Entities;
using Template.Infrastructure.Persistence;

namespace Template.Api.Tests;

/// <summary>
/// Architecture invariants enforced by the build (B9-1), so the structural guarantees the audit
/// established can't silently regress: feature slices never bypass the tenant filter, every
/// tenant-scoped entity IS filtered, and Blazor components live in the shared RCL — not the web app.
/// These are source/model scans, no extra dependency.
/// </summary>
public class ArchitectureTests
{
    [Fact]
    public void FeatureSlices_DoNotBypassTheTenantFilter()
    {
        var featuresDir = Path.Combine(RepoRoot(), "src", "Api", "Features");
        var offenders = SourceFiles(featuresDir)
            .Where(f => File.ReadAllText(f).Contains("IgnoreQueryFilters"))
            .Select(Path.GetFileName)
            .ToList();

        Assert.True(offenders.Count == 0,
            $"Feature code must not call IgnoreQueryFilters — use IRepository<T>.QueryAllTenants(). Offenders: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void EveryTenantScopedEntity_HasAGlobalQueryFilter()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=arch-check") // model-only; never connects
            .Options;
        using var ctx = new AppDbContext(options, new TestCurrentTenant());

        var missing = ctx.Model.GetEntityTypes()
            .Where(e => typeof(ITenantScoped).IsAssignableFrom(e.ClrType))
            .Where(e => e.GetDeclaredQueryFilters().Count == 0)
            .Select(e => e.ClrType.Name)
            .ToList();

        Assert.True(missing.Count == 0,
            $"ITenantScoped entities missing the global tenant query filter: {string.Join(", ", missing)}");
    }

    [Fact]
    public void WebApp_HasNoInlineBlazorComponents()
    {
        // The web app may only carry bootstrap markup; all UI components live in Shared.Ui (RCL).
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "_Imports.razor", "App.razor", "Routes.razor",
        };

        var inline = SourceFiles(Path.Combine(RepoRoot(), "src", "Web"), "*.razor")
            .Select(Path.GetFileName)
            .Where(name => !allowed.Contains(name!))
            .ToList();

        Assert.True(inline.Count == 0,
            $"Blazor components belong in Shared.Ui, not the web app: {string.Join(", ", inline)}");
    }

    private static IEnumerable<string> SourceFiles(string dir, string pattern = "*.cs") =>
        !Directory.Exists(dir)
            ? []
            : Directory.EnumerateFiles(dir, pattern, SearchOption.AllDirectories)
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                         && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Api", "Features")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Could not locate the repo root from the test assembly.");
    }
}
