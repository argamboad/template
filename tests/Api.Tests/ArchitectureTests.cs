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

        // IgnoreQueryFilters is never allowed in feature code — even contributors go through QueryAllTenants().
        var ignoreOffenders = SourceFiles(featuresDir)
            .Where(f => File.ReadAllText(f).Contains("IgnoreQueryFilters"))
            .Select(Path.GetFileName)
            .ToList();
        Assert.True(ignoreOffenders.Count == 0,
            $"Feature code must not call IgnoreQueryFilters — use IRepository<T>.QueryAllTenants(). Offenders: {string.Join(", ", ignoreOffenders)}");

        // QueryAllTenants is the sanctioned cross-tenant hatch, but ONLY inside *DataContributor.cs
        // (dissolve/export). In request-path slice code it bypasses tenancy identically to
        // IgnoreQueryFilters, so it is banned there too (v2 audit ADV-2).
        var queryAllOffenders = SourceFiles(featuresDir)
            .Where(f => !Path.GetFileName(f)!.EndsWith("DataContributor.cs", StringComparison.Ordinal))
            .Where(f => File.ReadAllText(f).Contains("QueryAllTenants"))
            .Select(Path.GetFileName)
            .ToList();
        Assert.True(queryAllOffenders.Count == 0,
            $"Request-path feature code must not call QueryAllTenants (allowed only in *DataContributor.cs). Offenders: {string.Join(", ", queryAllOffenders)}");
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
    public void EveryUserKeyedEntity_IsWiredIntoAccountErasure()
    {
        // Every entity with a user-owned key ("UserId") must be erased on account deletion (GDPR-2) —
        // by AccountErasureService's identity-core deletes, an IUserDataContributor, or tenant-membership
        // teardown. This canary fails when a NEW user-keyed entity appears, so its author must wire the
        // erasure and list it here (v2 audit SOLID-1 / R12). Actor references (CreatedByUserId /
        // InvitedByUserId) are deliberately excluded — they are tenant data, not the user's own PII.
        var handled = new HashSet<string>
        {
            nameof(UserLogin), nameof(RefreshToken),          // identity-core (AccountErasureService)
            nameof(TenantMembership),                          // tenant-membership teardown
            nameof(UserMfa), nameof(MfaRecoveryCode),          // MfaUserDataContributor
            nameof(Notification), nameof(NotificationPreference), // NotificationUserDataContributor
        };

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=arch-check") // model-only; never connects
            .Options;
        using var ctx = new AppDbContext(options, new TestCurrentTenant());

        var uncovered = ctx.Model.GetEntityTypes()
            .Where(e => e.ClrType.GetProperty("UserId") is not null)
            .Select(e => e.ClrType.Name)
            .Where(name => !handled.Contains(name))
            .ToList();

        Assert.True(uncovered.Count == 0,
            $"User-keyed entities not wired into account erasure — add an IUserDataContributor (or identity-core delete) and list it: {string.Join(", ", uncovered)}");
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
