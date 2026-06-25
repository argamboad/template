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

*Amendment (2026-06-25) — `EnterTenant`: scope system/integration writes instead of bypassing scoping.*
The cross-tenant escape hatch (`QueryAllTenants()`) is right for rare **teardown** (tenant dissolve),
but a frequent, access-*granting* write that runs without a JWT — notably the **Stripe billing
webhook** (BILLING-3) — should not punch a permanent hole in tenant isolation. New primitive
**`ITenantContext.EnterTenant(tenantId)`** (implemented by `HttpCurrentTenant`, registered so one
scoped instance backs both `ICurrentTenant` and `ITenantContext`): after the caller has
**authenticated** the tenant id by other means (a verified webhook signature; an admin authz check),
it makes that tenant *current* for the scope, so the write-stamping interceptor and the global read
filter scope to it — the operation gets the **same structural isolation an authenticated request
gets**, no `IgnoreQueryFilters`. It *scopes*, it does not *authorize*; entering `Guid.Empty` is
rejected. Preferred over the escape hatch for any signature-/system-authenticated tenant-scoped write;
the escape hatch stays for genuine cross-tenant teardown/enumeration. Reused later by ADMIN
impersonation (`docs/PLATFORM_BACKLOG.md`).

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

**ADR-005 — Apple Sign In fits the agnostic provider model; implementation DEFERRED, web-first. (2026-06-24)**
A third OAuth provider (Apple) was assessed against the provider-agnostic auth stack (ADR-002). The
verdict: the **backend absorbs it with small, mechanical additions** — `.AddApple(...)` in
`ServiceCollectionExtensions`, an `Apple` arm in `AuthProviders` (const + `Supported` +
`SchemeFor`), an `Apple => true` arm in `ProviderEmailTrust` (Apple asserts `email_verified`,
private-relay addresses included), a button/glyph in `Login.razor` + `Settings.razor`, and config
keys; `ClaimsExtractor` needs **no** change (`sub`→`NameIdentifier`, `email`→`ClaimTypes.Email`
already map). The fail-closed takeover guard and tenant scoping require no structural change.
However, Apple is **NOT** the single-`.AddXxx()` that Google/Microsoft are (so the CLAUDE.md / ADR-C15
"new provider = one line" claim has a documented exception). The Apple-specific costs are recorded
here so they are not rediscovered later:
1. **No built-in handler** — ASP.NET Core ships Google + MicrosoftAccount but not Apple. Needs the
   community `AspNet.Security.OAuth.Apple` (aspnet-contrib) package; confirm a **stable** .NET 10
   build exists before adopting (no previews — ADR-C10).
2. **The "client secret" is a rotating ES256 JWT**, minted from a downloaded `.p8` key + Team ID +
   Key ID + Service ID, expiring every ≤6 months. This breaks the single-static-secret-in-`.env`
   shape of ADR-001 (the package can generate/cache the JWT from the key material).
3. **Apple forbids `localhost` redirect URIs.** Google/MS redirect to `https://localhost:7160` /
   `http://localhost:5238`, which the QA plan and `MOBILE_TESTING.md` rely on. Apple needs a real
   **HTTPS domain or tunnel** even for local QA — a workflow asterisk, not a code change.
4. **`form_post` callback** (because name/email scope is requested) ⇒ the OAuth correlation cookie
   must be `SameSite=None; Secure`; relevant given the schemeful-same-site cookie history.
5. **Display name is returned only on the first authorization** — `ExtractDisplayName` gets `null`
   thereafter (tolerated; capture on first auth if wanted).
6. **Native (MAUI desktop/Android) Apple is a separate, larger effort** — no native Apple SDK on
   Win/Android, so it reuses the web flow and inherits #3/#4. Web-first per ADR-C9.
Decision: keep the design open to Apple; implement **web-first** as the slice in
`docs/stories/apple-signin.md` when a business need arises; defer until then. External prerequisite:
**Apple Developer Program enrollment ($99/yr)** + portal setup (App ID, Service ID, Sign-in key).
*Rationale:* the architecture doesn't fight a third provider — the cost is Apple's protocol and
account setup, not our code. Recording the constraints now prevents re-scoping later and stops "it's
just one line" from being assumed for Apple.

**ADR-006 — Billing & subscriptions: provider-abstracted (`IBillingProvider`), Stripe reference impl, plan-tier entitlements + quotas. Implementation DEFERRED, web-first. (2026-06-25)**
Monetization enters through a Core abstraction **`IBillingProvider`** (same shape as `IEmailSender`):
a **Stripe** reference implementation in Infrastructure plus an in-memory **`FakeBillingProvider`**
for tests. A tenant has at most one **`Subscription`** (`ITenantScoped`) holding plan tier, status,
and Stripe customer/subscription ids; access is gated by an **`IEntitlementService`** (feature flags
keyed to plan) and an **`IQuotaService`** (countable limits — seats, metered usage). The **plan
catalog is code/config, not tenant data.** Stripe is the **system of record for money**; our DB holds
a **projection** kept current by **webhooks processed idempotently through the inbox** (ADR-007) —
we never treat our own DB as the truth for billing state.
Constraints recorded:
1. **Webhooks are at-least-once and out-of-order** — the handler verifies the Stripe signature,
   dedupes by event id, and is reentrant. This is the reliable-consumer problem ADR-007's **inbox**
   solves, so **BILLING depends on JOBS** (the outbox/inbox slice) landing first.
2. **Entitlement checks are server-side and fail-closed** — no/expired/`past_due` subscription ⇒
   Free tier; never trust the client.
3. **No card data, minimal PCI scope** — money mutations happen on **Stripe Checkout + Customer
   Portal** (redirects); we build no card forms and store no PANs (SAQ-A).
4. **Access is granted on the webhook, not the Checkout redirect** — returning from Checkout does
   not flip the tenant to paid; the `subscription.created/updated` event does.
5. **Quotas ≠ rate limits** — the existing `RateLimiting.cs` is per-IP request throttling (abuse);
   plan quotas are per-tenant **persisted** counters (seats = membership count; usage = a counter).
   Different mechanism — don't conflate.
6. `Subscription` participates in tenant **dissolve** via an `ITenantDataContributor` that cancels
   the Stripe subscription and wipes the projection.
7. **Sandbox/fake test stack (the answer to "can we test billing without real money": yes):**
   `FakeBillingProvider` for unit; **stripe-mock** (Stripe's official offline mock server) for
   `Api.Tests` request/response; **Stripe test mode + Stripe CLI** (`stripe listen`/`stripe trigger`)
   for webhook E2E; **Stripe Test Clocks** to simulate trial-end/renewal/dunning deterministically.
   This is the Mailpit-for-billing analogue (ADR-C13: trap it locally, zero real charges).
*Rationale:* billing is what makes this a SaaS template rather than a multi-tenant CRUD app;
abstracting the provider keeps Core clean and the test suite offline; projecting Stripe state (rather
than owning money truth) avoids reconciliation bugs; deferring matches the web-first/business-need
posture (ADR-C9) — there is no app or plan catalog yet (`PROJECT_BRIEF.md` is still TODO).
Stories + slice plan: `docs/stories/billing.md` (epic `BILLING`). Future siblings parked in
`docs/PLATFORM_BACKLOG.md`.

*Amendment (2026-06-25) — billing HTTP surface is a PLATFORM controller, not a feature slice.* The
BILLING-1/2 slice plan said "`Features/Billing` slice (`MapTenantFeatureGroup`)", but **billing is
horizontal platform/chassis** (reusable by every app), and per ADR-004 the platform's HTTP surface is
**controllers**, while `src/Api/Features/<X>/` minimal-API slices are reserved for the *downstream
app's* vertical features. BILLING-2 initially (and wrongly) shipped `/api/billing` as a
`Features/Billing` slice with a hand-rolled owner check; it is now a **`BillingController :
TenantApiControllerBase`** (`src/Api/Controllers/`) next to the household controllers, reusing the
base's `GetMembershipAsync`/`IsOwner`/`Forbid403` gate, with the checkout orchestration in
`IBillingService` (`src/Api/Services/`). The provider/entitlement/catalog/`Subscription` pieces were
already platform and are unchanged; `.RequireEntitlement(...)` stays as `Features/`-root scaffolding
(like `MapTenantFeatureGroup`) that the downstream app's slices call. BILLING-3's webhook lands as a
controller action too. (The only vertical slice in the template remains the `Notes` 🗑️ DELETE-ME
sample.)

**ADR-007 — Reliable async work: transactional outbox + inbox + background dispatcher + scheduled jobs. Implementation DEFERRED. (2026-06-25)**
Side effects that must not be lost (email, billing webhooks, future integrations) move off the
request thread through a **transactional outbox**: an **`OutboxMessage`** is written in the **same EF
`SaveChanges`** as the business change (via `IUnitOfWork`), so the effect is atomic with the data —
no "saved the row but lost the email," no "charged but didn't provision." A **`BackgroundService`**
(`OutboxDispatcher`) polls unsent rows (claiming with Postgres `FOR UPDATE SKIP LOCKED`), dispatches
via typed handlers, and retries with backoff into a **dead-letter** state. The **inbox** is its
mirror — same table family with a `direction` discriminator, keyed by an external idempotency id
(e.g. Stripe event id) — giving exactly-once **inbound** processing. **Scheduled/recurring** work
(trial-expiry sweeps, dunning nudges, expired-token cleanup, quota resets) runs via a lightweight
timer hosted service.
Constraints recorded:
1. **In-process on Postgres, no broker** — keeps the template's run cost "Postgres only" (ADR-C13).
   A distributed scheduler (Hangfire/Quartz) or message broker is a documented **swap-in** when
   multi-node arrives, not a dependency now; the `SKIP LOCKED` claim design keeps a single-table
   approach correct even multi-instance.
2. **`OutboxMessage` is NOT `ITenantScoped`** — it's platform infra and may carry system (non-tenant)
   effects; it stores an optional `TenantId` for handler context but is outside the global filter.
   On dissolve, pending tenant-related outbox rows are drained/cancelled by the relevant contributor.
3. **At-least-once delivery ⇒ all handlers must be idempotent** — the same contract billing webhooks
   need (ADR-006).
4. **First consumer is the existing email path** — passwordless and invitation sends currently call
   `IEmailSender` **inline in the request**; the first slice migrates them to enqueue-to-outbox (the
   SMTP send moves into a handler), proving the path on existing, already-tested behavior.
*Rationale:* the template already sends email inline during request handling, so a transient SMTP
failure becomes a request error or a silently lost message. A generic outbox makes every side effect
reliable once, and gives billing webhooks a correct idempotent home. In-process keeps infrastructure
minimal until scale actually forces a broker.
Stories + slice plan: `docs/stories/async-jobs.md` (epic `JOBS`).

*Amendment (2026-06-25) — JOBS-1 + JOBS-2 implemented; inbox is a separate dedup ledger, not a
`direction` column.* JOBS-1 shipped the outbox + `OutboxDispatcher` + the email migration as described.
JOBS-2 shipped the **inbox**, but as a **purpose-built `InboxMessage` ledger** (`Id`, `Source`,
`IdempotencyKey`, `ReceivedAt`; unique on `(Source, IdempotencyKey)`) rather than the originally-sketched
"same table + `direction` discriminator." Reasons: (a) inbox rows need none of the outbox's
queue columns (`Type`/`Payload`/`Status`/`AttemptCount`/`NextAttemptAt`), so a shared table would be
half-null; (b) dedup is a **unique-key concern**, so `IInbox.TryClaimAsync` uses
`INSERT … ON CONFLICT DO NOTHING` against the unique index — **race-free by construction** (concurrent
claims of one key serialise on the index; exactly one wins), which is cleaner here than the outbox's
`SKIP LOCKED` queue-claim (that pattern is for *picking work off a queue*, not deduping). The claim runs
on the shared `AppDbContext`, so it enlists in the caller's transaction: claim + guarded work commit
together (or roll back together, freeing the key for the inevitable redelivery). BILLING-3 consumes
`IInbox` for webhook idempotency.

*Amendment (2026-06-25) — JOBS-3 implemented; epic COMPLETE.* The scheduled-jobs host
(`ScheduledJobsHost : BackgroundService`) runs registered `IScheduledJob`s on per-job intervals with
failure isolation (one job's throw never stops the others or the host) and a fresh DI scope per run;
the reference `ExpiredTokenCleanupJob` deletes expired login/refresh tokens hourly. In-process,
single-instance per the baseline; Hangfire/Quartz remains the documented multi-node swap-in. The JOBS
epic (outbox, inbox, scheduler) is now done — **BILLING is unblocked.**

**ADR-008 — Observability (structured logging + OpenTelemetry + health checks) and a tenant-scoped audit log. Implementation DEFERRED. (2026-06-25)**
Two complementary concerns shipped as one slice group.
**(a) Operational observability** — structured (JSON) logging with per-request scopes enriched with
`tenant_id`/`user_id` (from the JWT claim via `HttpCurrentTenant`); **OpenTelemetry** traces +
metrics (ASP.NET Core + EF Core + HttpClient instrumentation) with the request span tagged by
tenant/user; and `/health` (liveness) + `/health/ready` (readiness — DB reachable) endpoints. The
OTLP exporter is **config-gated** (console in dev, OTLP when an endpoint is configured — same
config-presence pattern as the OAuth providers), so the template runs with **no external telemetry
dependency** by default.
**(b) Audit log** — an append-only, tenant-scoped **`AuditEvent`** (`actor_user_id`, `action`,
`entity_type`, `entity_id`, `metadata` jsonb, `created_at`) for security/compliance-relevant actions
(member invited/removed, role changed, subscription changed, tenant dissolved). Written via an EF
`SaveChanges` interceptor (sibling of `TenantStampingInterceptor`) for declarative cases plus an
explicit `IAuditLog.Record(...)` for semantic events; **append-only** (no update/delete from app
code); `ITenantScoped` so it's auto-filtered per tenant and participates in dissolve.
Constraints recorded:
1. **Audit ≠ logs** — audit is durable, queryable, exportable **tenant data** (compliance); logs and
   traces are operational telemetry (sampled, ephemeral). Neither substitutes for the other.
2. **No secrets/PII in spans or audit metadata** — identifiers only; never tokens or card data.
3. **Health endpoints are unauthenticated and status-only** — must not leak internals.
4. **Dissolve vs retention tension** — wiping a tenant deletes its audit trail; if legal-hold/
   retention is required, the dissolve contributor must **export-then-wipe** (flagged for the
   GDPR/Account-Lifecycle backlog item).
*Rationale:* nothing in the template currently emits structured telemetry, a health endpoint, or an
audit trail — every downstream app would re-invent all three. Adding them once at the platform layer
means every feature inherits them, and audit slots naturally onto the existing interceptor +
tenant-scoping machinery (ADR-003 amendment).
Stories + slice plan: `docs/stories/observability.md` (epic `OBS`).
