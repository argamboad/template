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

**ADR-C9 — Non-web clients (mobile + Win/macOS desktop) are MAUI Blazor Hybrid, DEFERRED.**
Web first. *Rationale:* reuses the Blazor UI via the RCL, not just the API; deferred until that
work begins (re-check MAUI maturity then). Linux desktop out of scope; if required, tilt to Uno
Platform or Avalonia. The API being client-agnostic means worst case only the frontend is affected.

**ADR-C10 — Target latest STABLE release, never previews.**
Re-verify current stable versions at each project's start. *Rationale:* avoids building on
shifting preview ground; prefer LTS where it coincides with latest stable.

**ADR-C11 — Doc set + per-epic user stories methodology.**
Docs: PROJECT_BRIEF, FEATURES, DATA_MODEL, TECH_STACK, DECISIONS, CLAUDE.md. User stories
generated per-epic at build time, not upfront. *Rationale:* lean, persistent context for solo +
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
All ports are environment-variable-driven so multiple projects can run simultaneously without
conflicts. Both services expose healthchecks; API containers should declare `depends_on: db:
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
section nesting) so they bind to the same config keys. `.env` stays gitignored; `.env.example`
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
