using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Template.Api.Controllers;
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

    // ── v2 audit enforcement gates (B11) — lock in the B1–B7 fixes so they cannot silently regress ──

    [Fact]
    public void EveryEntityWithATenantId_IsScopedOrAllowlisted()
    {
        // R2/ARCH-2: an entity carrying a TenantId must be structurally filtered (ITenantScoped) or be on
        // an explicit allowlist of by-convention exceptions, so a new tenant-relevant table can't quietly
        // rely on hand-written filtering.
        var allow = new HashSet<string> { nameof(TenantMembership), nameof(WebhookDelivery) };

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=arch-check") // model-only; never connects
            .Options;
        using var ctx = new AppDbContext(options, new TestCurrentTenant());

        var offenders = ctx.Model.GetEntityTypes() // mapped entities only — excludes transient DTOs
            .Where(e => e.ClrType.GetProperty("TenantId")?.PropertyType == typeof(Guid))
            .Where(e => !typeof(ITenantScoped).IsAssignableFrom(e.ClrType) && !allow.Contains(e.ClrType.Name))
            .Select(e => e.ClrType.Name)
            .ToList();

        Assert.True(offenders.Count == 0,
            $"Entities with a TenantId must implement ITenantScoped or be allowlisted: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void EveryController_DerivesFromATenantOrAdminBase_OrIsAllowlisted()
    {
        // R4: a copied controller must not silently skip tenant/staff auth — it derives from a base that
        // applies it, or it is an explicitly-allowlisted system/anonymous surface.
        var allow = new HashSet<string>
        {
            nameof(AuthController), "FilesController", nameof(BillingWebhookController), nameof(NotificationsController),
        };

        var offenders = typeof(TenantApiControllerBase).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(ControllerBase).IsAssignableFrom(t))
            .Where(t => !typeof(TenantApiControllerBase).IsAssignableFrom(t)
                     && !typeof(AdminApiControllerBase).IsAssignableFrom(t)
                     && !allow.Contains(t.Name))
            .Select(t => t.Name)
            .ToList();

        Assert.True(offenders.Count == 0,
            $"Controllers must derive from TenantApiControllerBase/AdminApiControllerBase or be allowlisted: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void ServerServices_UseInjectedClock_NotAmbientUtcNow()
    {
        // R15/GAP-4/LOGIC-B3: server code takes TimeProvider, so a cookie/URL/token lifetime can't drift
        // from the injected clock. Entity default-value helpers and the WASM client (no injected clock)
        // live in Core/Shared.Ui and are outside this scan; migrations are excluded.
        var dirs = new[] { Path.Combine(RepoRoot(), "src", "Api"), Path.Combine(RepoRoot(), "src", "Infrastructure") };

        var offenders = dirs.SelectMany(d => SourceFiles(d))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}"))
            .Where(f => File.ReadAllText(f) is var t && (t.Contains("DateTime.UtcNow") || t.Contains("DateTimeOffset.UtcNow")))
            .Select(Path.GetFileName)
            .ToList();

        Assert.True(offenders.Count == 0,
            $"Server services must use an injected TimeProvider, not ambient UtcNow: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void PlatformTests_DoNotDependOnTheDeleteMeNotesSample()
    {
        // R9/TR-1: the tenancy/GDPR/outbox tests use the harness TestWidget fixture, not the DELETE-ME
        // Notes sample — so a downstream app can delete the sample without breaking the isolation guards.
        var testsDir = Path.Combine(RepoRoot(), "tests");
        string[] banned = ["Features.Notes", "Set<Note>", "new Note", "EfRepository<Note>", "IRepository<Note>"];

        // NotesSliceTests legitimately tests the sample; this file lists the banned patterns as literals.
        var exempt = new HashSet<string> { "NotesSliceTests.cs", "ArchitectureTests.cs" };
        var offenders = SourceFiles(testsDir)
            .Where(f => !exempt.Contains(Path.GetFileName(f)!))
            .Where(f => File.ReadAllText(f) is var t && Array.Exists(banned, t.Contains))
            .Select(Path.GetFileName)
            .ToList();

        Assert.True(offenders.Count == 0,
            $"Platform tests must use the harness TestWidget fixture, not the DELETE-ME Notes sample: {string.Join(", ", offenders)}");
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
