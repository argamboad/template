# Contributing

Working conventions (slices, Gherkin stories, Conventional Commits, the PR template) live in
[`docs/WAYS_OF_WORKING.md`](docs/WAYS_OF_WORKING.md). Test-Driven Development is mandatory — write the
failing test before the production code on every slice (see `CLAUDE.md`, golden rule 7).

## Definition of "Solid" — the frozen quality bar

This is the finish line from the 2026-06-21 deep audit (`docs/audits/v1-2026-06/AUDIT_REPORT.md` §6). It is the spec
that ends the audit treadmill: when all five hold, "solid" is **provable by the test suite + CI**,
not asserted by a reviewer. **Do not run another discovery audit — keep these green instead.**

- [x] **1. No open High.** The three High findings are fixed and test-backed: write-side tenant
  stamping (CONF-1), passwordless brute-force/rate-limiting (CONF-5), and the fail-closed
  `email_verified` takeover guard (MITI-3).
- [x] **2. Tenant safety is structural in both directions.** Reads are scoped by the global query
  filter and writes by `TenantStampingInterceptor`; an architecture test fails the build if
  `IgnoreQueryFilters` appears in `src/Api/Features/**` (use `IRepository<T>.QueryAllTenants()`), and
  live two-tenant isolation tests prove the filter.
- [x] **3. Passwordless auth is rate-limited and the takeover guard fails closed**, each with a test
  (`RateLimitingTests`, `PasswordlessServiceTests`, `ClaimsExtractorTests`).
- [x] **4. The reference slice (Notes) is exemplary** — injected clock + shared
  `MapTenantFeatureGroup` scaffolding — because every feature copies it.
- [x] **5. Docs don't contradict code, and the invariants are enforced in CI** — warnings-as-errors
  (`Directory.Build.props`), the architecture tests (B9-1), and the migration-drift guard
  (`MigrationsTests`) all run in [`.github/workflows/ci.yml`](.github/workflows/ci.yml).

## How the bar is enforced (so it can't rot)

| Guard | Where | Catches |
|-------|-------|---------|
| Architecture tests | `tests/Api.Tests/ArchitectureTests.cs` | `IgnoreQueryFilters` in features; an unfiltered `ITenantScoped` entity; Blazor components in the web app |
| Migration-drift test | `tests/Api.Tests/MigrationsTests.cs` | a model change with no migration; a broken migration |
| Per-test isolation | `tests/Api.Tests/Infrastructure/PostgresTestBase.cs` | order-dependent / leaky relational tests |
| Warnings-as-errors | `Directory.Build.props` | nullable violations + every compiler/analyzer warning |
| CI gate | `.github/workflows/ci.yml` | all of the above, on every PR to `main`/`develop` |

When you add a feature, copy `src/Api/Features/Notes` (the DELETE-ME reference slice) and keep these
guards green. That is the whole contract.
