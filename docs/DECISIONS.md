# Decisions (ADR log)

> Lightweight architecture/product decision records: decision + rationale + date. Stops us (and
> Claude Code) from re-litigating settled choices. Append new ones; supersede with a new dated
> entry rather than rewriting.
>
> The **constant ADRs** below (C-prefixed) are pre-decided across all projects from this template
> — keep them. Add **app-specific ADRs** (number them 001, 002, …) as you make decisions during
> conceptualization.

## Constant decisions (carry forward — do not re-debate)

**ADR-C1 — Multi-tenant SaaS: Tenant ≠ User, multiple users per tenant.**
*Rationale:* shared workspace model; data belongs to the tenant, not the individual.

**ADR-C2 — Tenant-scoped data; only preferences are per-user.**
*Rationale:* users in a tenant collaborate over shared data; enforce scoping on every query;
never leak across tenants.

**ADR-C3 — Backend is ASP.NET Core Web API behind a clean API boundary.**
UI never hits the DB directly. *Rationale:* the API is the durable, client-agnostic asset reused
by every client.

**ADR-C4 — Web frontend is Blazor WebAssembly (not Server).**
*Rationale:* preserves the "frontend is just another API client" boundary; modern Blazor improved
WASM bundle size and AOT. Server couples UI to server / per-user live connection — rejected.

**ADR-C5 — Blazor UI components live in a shared Razor Class Library (RCL).**
*Rationale:* makes future non-web clients (MAUI Blazor Hybrid) a component-reuse exercise, not a
rewrite. Cheap now, expensive to retrofit.

**ADR-C6 — Database is PostgreSQL.**
*Rationale:* free, portable, cheap to host, capable. Chosen over SQL Server for
economy/portability.

**ADR-C7 — ORM is Entity Framework Core (Npgsql provider).**
*Rationale:* default .NET ORM; first-class Postgres; maps data model to migrations.

**ADR-C8 — Auth is ASP.NET Core Identity; tenant scoping layered on top.**
*Rationale:* built-in user/auth; tenant association sits above Identity as a query concern.
> **Superseded by ADR-002 (2026-06-19):** the template ships a custom JWT + refresh-token auth
> stack instead of ASP.NET Core Identity.

**ADR-C9 — Non-web clients (mobile + Win/macOS desktop) are MAUI Blazor Hybrid: shells scaffolded with auth wired, feature parity DEFERRED (web-first).**
The template ships MAUI desktop + Android shells with auth already wired (see `docs/MOBILE_TESTING.md`);
build each feature on web first and extend the shells once it works there. *Rationale:* reuses the
Blazor UI via the RCL, not just the API; feature work deferred until it begins (re-check MAUI maturity then). Linux desktop out of scope; if required, tilt to Uno
Platform or Avalonia. The API being client-agnostic means worst case only the frontend is affected.

**ADR-C10 — Target latest STABLE release, never previews.**
Re-verify current stable versions at each project's start. *Rationale:* avoids building on
shifting preview ground; prefer LTS where it coincides with latest stable.

**ADR-C11 — Doc set + per-epic user stories methodology.**
Docs: PROJECT_BRIEF, FEATURES, DATA_MODEL, TECH_STACK, DECISIONS, WAYS_OF_WORKING, REBRANDING,
LOCALIZATION, MOBILE_TESTING, QA_TEST_PLAN, CLAUDE.md, plus per-epic stories under `docs/stories/`.
User stories generated per-epic at build time, not upfront. *Rationale:* lean, persistent context for solo +
Claude Code; stories stay grounded in real screens.

---

## App-specific decisions
<!-- Add ADR-001, ADR-002, … as decisions are made during conceptualization.
     Format: decision + rationale + date. -->

> _Begin numbering at ADR-001 for this app. Date each entry._

**ADR-C12 — Process conventions: vertical slices, per-epic Gherkin user stories, Conventional
Commits, standard PR template.**
Vertical end-to-end slices that keep the app working; stories in `docs/stories/` one file per epic
with Gherkin acceptance criteria; Conventional Commits for branches/commits/PR titles; PRs use
`.github/pull_request_template.md`. Full detail in `docs/WAYS_OF_WORKING.md`. *Rationale:* a
lightweight defined process keeps solo + Claude Code work consistent and mergeable.

**ADR-C13 — Local dev infrastructure via Docker Compose: PostgreSQL 17 + Mailpit. (2026-06-17)**
`docker-compose.yml` at repo root; configuration via `.env` (gitignored; copy from `.env.example`).
All **Compose service** ports (DB, Mailpit) are environment-variable-driven so multiple projects can
run simultaneously without conflicts (the API/Web app ports are fixed in their launch profiles). Both services expose healthchecks; API containers should declare `depends_on: db:
condition: service_healthy`. No pgAdmin in the template — devs use their own DB client.
*Rationale:* PostgreSQL is always needed; Mailpit traps passwordless + invitation email in dev with
zero config; env-var ports prevent port clashes across projects.

**ADR-C14 — Testing: 100% TDD; unit tests (xUnit) + E2E (Playwright/NUnit). (2026-06-17)**
All production code is test-driven (red-green-refactor). Unit tests (xUnit) in `Core.Tests` and
`Api.Tests` cover domain logic, derived rules, and API behavior. E2E tests (Playwright 1.60,
NUnit) in `E2E.Tests` cover critical user flows through a real browser against the full running
stack; Page Object Model in `tests/E2E.Tests/Pages/`. Gherkin scenarios from user stories map
directly to test cases. No slice merges without passing tests. `playwright install` required once
after first build. *Rationale:* TDD forces clear interfaces and prevents regression; E2E tests
confirm real user flows end-to-end; together they give confidence to ship and refactor continuously.

**ADR-C15 — Auth strategy: OAuth (Google + Microsoft, extensible) + magic links (web) + OTP framework (mobile, deferred). (2026-06-17)**
External OAuth via ASP.NET Core's provider model — new providers added as a single `.AddXxx()`
call, no structural changes. Magic links use a custom `MagicLinkTokenProvider`
(`DataProtectorTokenProvider` subclass, 15-min default) for passwordless web sign-in; tokens are
Data Protection-backed, single-use, and server-bound. OTP infrastructure provided by
`AddDefaultTokenProviders()` — TOTP (authenticator app) and email OTP are ready; SMS OTP deferred
until mobile work begins. `TenantInvitation` entity added for household membership flow. OAuth
credentials stored in user-secrets (dev) / environment variables (prod) — never in appsettings.
Email sent via `IEmailSender` (Core abstraction) → `SmtpEmailSender` (MailKit, `Email:Smtp`
config); dev points to Mailpit. JWT Bearer auth intentionally omitted from this ADR — configured
in the auth story slice to keep it app-specific.
*Rationale:* provider-agnostic OAuth avoids re-architecting for new providers; magic links remove
password friction on web; OTP framework is in place without committing to an SMS provider;
abstracting `IEmailSender` keeps Core independent of sending infrastructure.
> **Superseded by ADR-002 (2026-06-19):** auth is a custom JWT + `LoginToken`/`PasswordlessService`
> stack, not Identity token providers / `MagicLinkTokenProvider` / `AddDefaultTokenProviders`. The
> TOTP/authenticator support described here was never implemented (email OTP is). Secrets moved to
> `.env` per ADR-001.

**ADR-001 — Local-dev secrets/config consolidated in `.env` (DotNetEnv); supersedes user-secrets. (2026-06-19)**
All local-dev secrets and config (`Jwt__Secret`, `Authentication__{Google,Microsoft}__*`,
`Email__Smtp__*`) live in the repo-root **`.env`** alongside the existing docker-compose vars —
one file. The API loads it at startup via **DotNetEnv** (`Env.TraversePath().Load()` before the
host builder; a no-op when absent, e.g. production). Keys use the .NET env-var form (`__` =
section nesting, so `Section__Sub` ≡ the `Section:Sub` config key) so they bind to the same config keys. `.env` stays gitignored; `.env.example`
(committed) documents every key with placeholders. **Production is unchanged** — the same keys
come from real environment variables, never a committed file. This **supersedes the
`dotnet user-secrets`** approach noted in ADR-C15.
*Rationale:* a single, visible local-config file was the explicit preference; `.env` already
existed for docker-compose, so the app secrets join it. Trade-off vs user-secrets: secrets now
sit in the working tree (mitigated by `.gitignore`) rather than the user profile — accepted for
this workflow.

**ADR-002 — Auth is a custom JWT + rotating-refresh-token stack, not ASP.NET Core Identity. (2026-06-19)**
Supersedes ADR-C8 and the Identity parts of ADR-C15. The template implements its own auth on custom
`User` / `UserLogin` / `RefreshToken` / `LoginToken` entities: JWT access tokens (60 min) + rotating,
hashed refresh tokens (single-use, replay-protected); OAuth (Google + Microsoft) account-linking with
an unverified-email takeover guard; passwordless magic-link + email OTP via `PasswordlessService`
(hashed, single-use, time-limited `LoginToken`s). There is **no** `IdentityUser`, `UserManager`,
`MagicLinkTokenProvider`, or `AddDefaultTokenProviders`; JWT Bearer is configured in `Program.cs`.
*Rationale:* the Identity + cookie approach hit a persistent Blazor WASM client failure; the proven
JWT model (ported and hardened) was chosen over more Identity debugging, and it also gives native
(MAUI) clients clean body-token transport.

**ADR-003 — Tenancy is membership-based and enforced by a global query filter. (2026-06-19)**
A user's tenant lives in a **`TenantMembership`** join entity (unique on `UserId` — one tenant at a
time; `Role` owner/member), **not** a `tenant_id` column on `User`. Tenant-owned entities implement
`ITenantScoped`; `AppDbContext` applies a global EF query filter scoping them to the JWT's `tenant_id`
claim (fail-closed when absent). Genuinely cross-tenant / pre-auth lookups (invitation accept by token
hash) opt out with `IgnoreQueryFilters()`.
*Rationale:* membership models "a user moves between tenants" and the always-in-exactly-one-tenant
invariant cleanly; the global filter turns "never leak across tenants" (ADR-C2) from a per-query
convention into a structural guarantee, so feature slices can't forget to scope.

*Amendment (2026-06-22) — scoping is now structural on BOTH read and write.* The original query
filter scoped reads only; nothing stamped or validated `TenantId` on insert, so a slice that forgot
the stamp (or bound it from request input) could persist a row under the wrong tenant — and the read
filter would then hide that row from its true owner, an invisible data-integrity bug (audit CONF-1).
A **write-side `TenantStampingInterceptor`** (`src/Infrastructure/Persistence/`, wired via
`AppDbContext.OnConfiguring` so every context — including tests — enforces it) now closes that gap:
for each `Added` `ITenantScoped` entity *while a tenant is current*, an unset `TenantId` is stamped
with the current tenant, and a `TenantId` belonging to a **different** tenant **throws** (fail
closed). A context with **no** current tenant (`CurrentTenantId == Guid.Empty`) is a system/seed/
cross-tenant context and is not enforced — the same trust level that may bypass the read filter.
The audited cross-tenant **escape hatch** is named and greppable: `IRepository<T>.QueryAllTenants()`
for reads (replacing ad-hoc `Query().IgnoreQueryFilters()` in feature code; used by dissolve
`ITenantDataContributor`s), and `IgnoreQueryFilters()` on the platform's own teardown
(`TenantRepository.WipeDataAsync`, which targets its argument tenant regardless of who is current).
A build-time ban on `IgnoreQueryFilters` inside `src/Api/Features/**` is planned (audit B9-1) to make
the escape hatch unreachable from slice code.

**ADR-004 — Clean platform baseline + vertical-slice features (hybrid). (2026-06-19)**
The reusable **platform** stays clean-layered / horizontal — Core, Infrastructure, and the
auth/tenancy controllers (the durable chassis: JWT auth, membership tenancy, the global query
filter, email, persistence). App **features** are organized as **vertical slices**: one
self-contained folder per feature in `src/Api/Features/<Feature>/` — a minimal-API `MapGroup`, a
handler, co-located models, and an `ITenantDataContributor` — reusing the platform via
`IRepository<T>`, `ICurrentTenant`, and `IUnitOfWork`. Feature endpoints are minimal-API groups; the
platform stays controllers. The entity lives in `Core` and implements `ITenantScoped` so tenant
scoping is automatic. Full convention in `docs/WAYS_OF_WORKING.md`; reference slice at
`src/Api/Features/Notes` (marked DELETE-ME).
*Rationale:* the platform is cross-cutting, stable, and shared by every feature — it benefits from
clean layering. Features are independent and churn-y — co-locating each one's endpoint/handler/
models/data makes them easy to add, understand, and delete without touching central code. The
generic repository + global tenant filter let a slice be added without authoring a repository pair
or remembering to scope. This is the architectural convention for app work on top of the template.
