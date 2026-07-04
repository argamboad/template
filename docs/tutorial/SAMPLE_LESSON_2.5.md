# Lesson 2.5 — Tenancy & the global query filter

> **Part 2 · Identity & tenancy.** This is the keystone of the whole platform. Everything
> you build after this — notes, billing, files, webhooks — leans on the guarantee we
> install here: *a query cannot accidentally read another tenant's data.* We're going to
> earn that guarantee the hard way: build the leak first, prove it with a test, then close
> it structurally.

**Goal:** an `ITenantScoped` entity is filtered to the current tenant automatically, so a
handler that "forgets" to scope its query still cannot read another tenant's rows.

**Concepts & principles:** multi-tenancy (tenant ≠ user); the difference between
*filter-by-convention* and *structural* isolation; EF Core global query filters;
fail-closed defaults.

**Maps to:** ADR-003 (tenancy via `TenantMembership` + global filter) · Golden Rule 1 ·
Foundation Rule R2 · repo: `src/Infrastructure/Persistence/AppDbContext.cs`,
`src/Core/Entities/ITenantScoped.cs`, `src/Core/Abstractions/ICurrentTenant.cs`.

**Prerequisites:** 2.1 (users + JWT) and 2.2 (the `tenant_id` claim rides on the access
token). You have an `AppDbContext`, a Postgres Testcontainer fixture (1.3), and a way to
sign in as a user belonging to a tenant.

---

## 1. Motivate — build the leak, then look at it

Every SaaS bug that ends up in the news is some version of *"customer A saw customer B's
data."* The naive defense is discipline: "always add `.Where(x => x.TenantId == me)` to
every query." That fails the first time a tired developer forgets — and you cannot grep
your way to confidence across a growing codebase.

Let's feel it. Suppose we have a `Widget` entity and a handler that lists widgets:

```csharp
// src/Core/Entities/Widget.cs  — first attempt, NOT yet tenant-aware
public class Widget
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }   // we store it…
    public string Name { get; set; } = "";
}

// the "forgot to scope" query a real handler might contain
public Task<List<Widget>> ListAsync() =>
    _db.Set<Widget>().ToListAsync();      // …but we never filter on it
```

The `TenantId` column exists, so it *looks* multi-tenant. It isn't. Nothing enforces it.

## 2. Red — a test that proves the leak

Write the failing test first. Seed two tenants, put a widget in each, sign in as tenant A,
and assert you can only see A's widget. Against the naive handler this test **fails** — and
that failure is the whole point of the lesson.

```csharp
// tests/Api.Tests/Tenancy/TenantIsolationTests.cs
[Fact]
public async Task Listing_widgets_never_returns_another_tenants_rows()
{
    var (tenantA, tenantB) = (await SeedTenantAsync(), await SeedTenantAsync());
    await SeedWidgetAsync(tenantA, "A's widget");
    await SeedWidgetAsync(tenantB, "B's widget");

    // Act as tenant A — CurrentTenant.TenantId == tenantA.Id
    using var scope = AsTenant(tenantA);
    var visible = await scope.Handler.ListAsync();

    Assert.Single(visible);
    Assert.Equal("A's widget", visible[0].Name);   // ❌ naive handler returns BOTH
}
```

> **TDD note.** We are not testing the handler's SQL. We're testing an *invariant of the
> platform*: "a tenant-scoped read is isolated." That invariant must hold no matter how
> careless the handler is — which is exactly why the fix belongs in the platform, not the
> handler.

## 3. Green — make isolation structural, not conventional

Three moves. First, mark the entity as tenant-owned by implementing a marker interface —
this is the *opt-in* that the filter keys off:

```csharp
// src/Core/Entities/ITenantScoped.cs
public interface ITenantScoped
{
    Guid TenantId { get; }
}

// src/Core/Entities/Widget.cs  — now tenant-aware by type, not by discipline
public class Widget : ITenantScoped
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = "";
}
```

Second, resolve *who the current tenant is* from the authenticated request. That's a
request-scoped abstraction the DbContext can depend on:

```csharp
// src/Core/Abstractions/ICurrentTenant.cs
public interface ICurrentTenant
{
    Guid? TenantId { get; }   // null when unauthenticated / tenant-less
}
```

The HTTP implementation reads the `tenant_id` claim off the JWT you issued in 2.2. In tests
you swap in a trivial fake that returns whatever tenant you're acting as.

Third — the move that closes the leak for *every* entity at once — apply a global query
filter in `OnModelCreating`, discovered by reflection so you can never forget to add it to
a new entity:

```csharp
// src/Infrastructure/Persistence/AppDbContext.cs
public Guid CurrentTenantId => _currentTenant.TenantId ?? Guid.Empty;

protected override void OnModelCreating(ModelBuilder builder)
{
    base.OnModelCreating(builder);
    builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

    // Every ITenantScoped entity is filtered to the current tenant by default,
    // so feature/domain queries can't forget to scope. Discovered, not hand-listed.
    foreach (var entityType in builder.Model.GetEntityTypes())
        if (typeof(ITenantScoped).IsAssignableFrom(entityType.ClrType))
            ApplyTenantFilterMethod.MakeGenericMethod(entityType.ClrType).Invoke(this, [builder]);
}

private void ApplyTenantFilter<TEntity>(ModelBuilder builder) where TEntity : class, ITenantScoped
    => builder.Entity<TEntity>().HasQueryFilter(e => e.TenantId == CurrentTenantId);
```

Now the *unchanged*, still-"careless" `ListAsync()` — `_db.Set<Widget>().ToListAsync()` —
emits `WHERE tenant_id = @currentTenant` under the hood. Re-run the test: **green.** The
handler never mentioned the tenant. The platform did.

## 4. Refactor & harden — fail closed, then lock it with a guardrail

Look hard at one line:

```csharp
public Guid CurrentTenantId => _currentTenant.TenantId ?? Guid.Empty;
```

Why `Guid.Empty` and not "throw" or "return everything"? Because tenant ids are UUIDv7 —
`Guid.Empty` is never a real row. So an **unauthenticated or tenant-less caller matches
nothing** rather than *everything*. The failure mode of a missing tenant is "see no data,"
not "see all data." That is *fail-closed*, and it's a deliberate security decision, not an
accident of defaulting. Add a test that pins it:

```csharp
[Fact]
public async Task With_no_current_tenant_scoped_reads_return_nothing()
{
    await SeedWidgetAsync(await SeedTenantAsync(), "someone's widget");
    using var scope = AsNobody();                       // ICurrentTenant.TenantId == null
    Assert.Empty(await scope.Handler.ListAsync());      // fail closed
}
```

Finally, make the invariant *enforceable by the build*. A filter you can bypass with
`IgnoreQueryFilters()` is a filter a future slice will bypass. Add an architecture test
(this is the recurring "guardrail ships with the feature" beat you'll see every part) that
bans the escape hatch inside feature code:

```csharp
// tests/Api.Tests/ArchitectureTests.cs
[Fact]
public void Feature_slices_never_call_IgnoreQueryFilters()
{
    var offenders = SourceFilesUnder("src/Api/Features")
        .Where(f => f.Contents.Contains("IgnoreQueryFilters"));
    Assert.Empty(offenders);   // cross-tenant reads use the sanctioned QueryAllTenants() seam
}
```

## 5. Run it

```sh
docker compose up -d                       # Postgres + Mailpit
dotnet test tests/Api.Tests --filter Tenancy
# 3 passing: isolation, fail-closed, and the arch guardrail
```

Optional: sign in via the running API as a user in tenant A, `GET /api/widgets`, and
confirm B's rows are absent even though the SQL your handler wrote had no `WHERE` clause.

## 6. Why it's built this way

- **Structural beats conventional.** Isolation enforced by the *type system + one central
  filter* can't be forgotten per-query. Isolation enforced by "remember to add `.Where`"
  will be forgotten — it's a matter of when. (ADR-003.)
- **Reflection over a hand-maintained list.** New entities are protected the moment they
  implement `ITenantScoped`; there's no registration step to forget. (This is also why R11
  builds the test-fixture reset list from the model, not by hand.)
- **Fail closed on purpose.** `?? Guid.Empty` turns "no tenant" into "no data." A system
  that leaks when misconfigured is worse than one that shows an empty screen.
- **What deliberately *doesn't* implement `ITenantScoped`:** `User`, `TenantMembership`,
  auth tokens — they're needed *before* a tenant is resolved, or span tenants. Recognizing
  which entities are cross-tenant is part of the skill this lesson teaches.
- **Reads are only half.** The filter scopes *reads*. A malicious or buggy *write* could
  still stamp a foreign `TenantId`. That's the next lesson (2.6): the write-side
  `TenantStampingInterceptor` + the `EnterTenant` primitive.

## 7. Checkpoint

```sh
git tag lesson-2.5
```

You should now be able to: explain why a stored `TenantId` column is *not* multi-tenancy;
add a global query filter keyed on a marker interface; justify a fail-closed default; and
write a test that asserts a platform invariant rather than a single method's behavior.

**Next:** *Lesson 2.6 — Write-side tenancy*, where we stop trusting handlers to set
`TenantId` correctly on the way *in*.
