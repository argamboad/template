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

*Amendment (v2 audit, 2026-07-01, per v2 decision D7) — config-gated minimal-API PLATFORM surfaces are a
sanctioned exception; "zero central edits" is really a ~5-touchpoint slice contract.* Two clarifications
to reconcile this ADR with what shipped:
1. **Platform is not *exclusively* controllers.** PUBAPI (ADR-015) and HOOKS (ADR-016) are horizontal
   **platform** capabilities but ship as **minimal-API groups** (`ApiKeyEndpoints`, `WebhookEndpoints`),
   not controllers, because their routes must be **conditionally mapped** behind a config gate
   (`PublicApi:Enabled` / `Webhooks:Enabled`) — off ⇒ the routes don't exist (404), which minimal-API
   conditional mapping expresses cleanly. So the rule is: platform HTTP is controllers **by default**,
   with **config-gated minimal-API groups as an explicit exception** for surfaces that must appear/vanish
   by configuration. Downstream *app* vertical features remain `src/Api/Features/<X>/` slices.
2. **"Without touching central code" is a bounded contract, not literally zero edits.** Adding a slice
   still touches a small, fixed set of central seams — roughly: register the `DbSet`/config, add a
   migration, wire DI (`Add*`) + map the group in `Program.cs`, register the `ITenantDataContributor`,
   and reset the table in the test fixture (the "add-a-slice checklist" in `docs/WAYS_OF_WORKING.md`).
   The point stands — you never edit a central *wipe/has-data/export* method or author a repository pair
   — but it's ~5 mechanical touchpoints, not none.

*Amendment (v2 audit B9-6 / DEBT-6, 2026-07-02, per v2 decision D7) — the config-gated platform surfaces
now live in a distinct namespace/folder, separated from vertical-slice features.* The prior amendment
established that PUBAPI/HOOKS are **platform** (not app features); this refines *where* they live so the
distinction is structural, not just narrative:
1. **Config-gated minimal-API PLATFORM surfaces live under `src/Api/Endpoints/`** (namespace
   `Template.Api.Endpoints`), NOT `src/Api/Features/`. `ApiKeyEndpoints` (PUBAPI) and `WebhookEndpoints`
   (HOOKS) moved there. They may use a raw `MapGroup(...)` because they are platform surfaces, not slices.
2. **The shared endpoint-extension helpers are platform infra and live with Endpoints.**
   `MapTenantFeatureGroup` (`FeatureEndpointExtensions`), `RequirePermission` (`PermissionEndpointExtensions`),
   and `RequireEntitlement` (`EntitlementEndpointExtensions`) moved from `Template.Api.Features` to
   `Template.Api.Endpoints`. This is what lets the R8 gate hold: nothing outside `src/Api/Features/` (except
   `Program.cs`, which composes the Notes sample) references `Template.Api.Features.*`.
3. **Vertical-slice features stay under `src/Api/Features/<X>/`** and register their routes via
   `MapTenantFeatureGroup` — never a raw `MapGroup`. A new build gate (R6,
   `FeatureFiles_RegisterRoutesViaMapTenantFeatureGroup_NotRawMapGroup`) scans `src/Api/Features/**` and
   fails on any raw `.MapGroup(` there, so a future slice can't quietly bypass the shared tenant-API auth.
   This is a pure move + namespace change — no route, behavior, or signature changed.

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

*Addendum (BILLING-5, 2026-07-01) — quotas implemented (mechanism-first, policy as data).* Decision
point on quotas is now built: **`IQuotaService`** (`src/Api/Services/QuotaService.cs`) resolves the plan
exactly like `EntitlementService` (fail-closed to Free) and enforces two limit kinds. **Seats** =
tenant members + **pending invites** (a pending invite reserves a seat, so N invites can't over-provision
past the cap) vs `Plan.SeatLimit`; checked in `TenantInvitationService.CreateAsync` for new invites →
**402 `seat_limit_reached`**. **Metered usage** = `TryConsumeAsync(key)` against a **monthly**
`UsageCounter` (`{tenant, key, yyyy-MM}`) — the calendar-month period key makes it **self-resetting with
no sweep job** (simpler than the point-5 "usage counter + JOBS-3 reset" sketch). Limits live in
`PlanCatalog` as **data**: a `null`/absent limit means unlimited, so the mechanism ships **inert** until a
plan sets a number; the template ships example numbers (Free 3/3, Pro 10/100) to demonstrate. Confirms
point 5 (quotas ≠ rate limits): these are per-tenant persisted counters, not the per-IP throttle.

*Addendum (BILLING-6, 2026-07-01) — trial/dunning lifecycle (owner-facing reaction, mechanism-first).*
The projection already reflects Stripe's lifecycle (BILLING-3), and entitlements already fail closed on a
lapsed period — so BILLING-6 adds the **reaction**, not new state. **`IBillingNotifier`** notifies the
tenant **owner** through the notification center (NOTIFY: in-app row + outbox email per prefs). The
**webhook handler** compares the pre-event status and, on a **transition into `past_due` or `canceled`**,
notifies once (same-status redeliveries don't re-notify; the inbox dedups by event id). A
**`SubscriptionLapseSweepJob`** (`IScheduledJob`, ADR-007) scans all tenants (`QueryAllTenants`) for
active/trialing subscriptions whose `CurrentPeriodEnd` has passed, sends a one-time "expired" nudge, and
records `Subscription.LapseNotifiedAt` so it fires once per lapse — **without fabricating a status**
(Stripe stays the money-truth; a later webhook corrects the projection). Deliberately **not** built:
Stripe's own retry schedule / card-failure emails (**Smart Retries** owns that), and the advance
"trial-ends-in-N-days" nudge (a small follow-up — needs a Stripe `trial_will_end` event kind).

*Addendum (BILLING-7, 2026-07-01) — billing participates in tenant dissolve (point 6 delivered).* The
design (point 6) always said the `Subscription` should be torn down on dissolve; it's now built.
**`BillingDataContributor : ITenantDataContributor`** wipes the tenant's `Subscription` projection and —
if it has a live provider subscription — **cancels it at the provider**, so a dissolved tenant stops being
billed (the "delete account → Stripe keeps charging" bug). The cancel is **not** an external call inside
the dissolve transaction: it's a `"billing.cancel"` **outbox** message (staged with the teardown, then run
out-of-band with retry by `BillingCancelOutboxHandler` → new idempotent `IBillingProvider.CancelSubscriptionAsync`).
`HasDataAsync` returns **false** — a subscription is billing plumbing, not tenant content, so it never trips
the "would abandon data" guard; it's cleaned up automatically instead. Export (GDPR-1) gains a `billing`
section (plan/status/period — never Stripe ids or card data). **This closes the BILLING epic (1–7).**

*Amendment (v2 audit GAP-1, 2026-07-01) — the fake provider is Development-only; production without a key fails fast.* The
original wiring registered `FakeBillingProvider` whenever `Billing:Stripe:SecretKey` was absent — including in
production. Because the fake **trusts a literal webhook signature** (`Stripe-Signature: valid`) and the webhook
endpoint is anonymous and always mapped, a production deploy that hadn't yet configured Stripe would accept
**forged, unauthenticated cross-tenant subscription writes** (an attacker could grant/rewrite any tenant's plan).
`AddInfrastructure` now takes `IHostEnvironment` and registers the fake **only when `environment.IsDevelopment()`**;
outside Development with no key it **throws at startup** (the app cannot boot with the fake). The webhook controller
also **logs a warning with the source IP** on a rejected signature (GAP-5), so a forged-webhook probe is observable
rather than a silent 400. Consequence: a **production/staging deploy MUST configure a real `Billing__Stripe__SecretKey`**
(it no longer silently falls back to the fake). Dev/E2E are unchanged. Tests: `BillingProviderRegistrationTests`,
`BillingWebhookControllerTests`.

*Amendment (v2 audit, 2026-07-01) — the stripe-mock request/response test stack (point 7) was deferred.*
The test stack as built is `FakeBillingProvider` (unit) + Stripe test-mode/CLI for E2E; **stripe-mock**
(Stripe's official offline mock server, sketched in point 7 for `Api.Tests` request/response coverage) is
**not** in the test stack — it was deferred in BILLING-2 over Testcontainers friction (see the note in
`docs/ROADMAP.md` under "Test & hardening debt"). The rest of point 7 holds.

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

*Amendment (v2 audit, 2026-07-01) — (b) the declarative SaveChanges audit-writer was deferred; audit
writes are explicit only.* Decision point (b) above sketched an EF `SaveChanges` interceptor that would
write audit rows declaratively *plus* an explicit `IAuditLog.RecordAsync` for semantic events. As
shipped, only the **explicit `IAuditLog.RecordAsync`** path exists — audit events are always written
deliberately at the call site (member invited/removed, role changed, subscription changed, tenant
dissolved, admin access). The `AuditAppendOnlyInterceptor` is present but **only GUARDS** append-only
(it throws on any tracked update/delete of an `AuditEvent`); it does **not** author audit rows. A
declarative auto-audit-on-SaveChanges interceptor remains an optional future add (also noted in
`docs/ROADMAP.md`).

**ADR-009 — RBAC: a third `admin` role + a permission seam (capability checks, not role checks). (2026-06-30)**
The template shipped with exactly two tenant roles — `owner` and `member` — enforced by `IsOwner(...)`
boolean checks copied across every tenant controller (`HouseholdController`,
`HouseholdInvitationsController`, `BillingController`). B2B tenants delegate administration almost
immediately, and a copied `role == "owner"` test is both too coarse (no middle tier) and too brittle
(scattered, easy to drift). This ADR adds an `admin` tier **and**, more importantly, a **permission
seam** so call sites ask *"can the caller do X?"* instead of *"is the caller the owner?"*.

**Decision:**
1. **Roles are ordered: `owner` > `admin` > `member`.** `admin` is a new `TenantRoles` constant; the
   "exactly one owner" invariant (ADR-003) is unchanged — owner is conferred only via
   `TransferOwnershipAsync`, never via a role-change endpoint.
2. **A `Permission` enum + a static role→permission matrix** (`RolePermissions`) in **Core** is the
   single source of truth for "what can this role do". Permissions are coarse capabilities
   (`ViewTenant`, `RenameTenant`, `ManageMembers`, `ManageRoles`, `ManageBilling`,
   `TransferOwnership`, `DissolveTenant`), **not** per-entity ACLs. The matrix:

   | Permission | owner | admin | member |
   |---|:--:|:--:|:--:|
   | `ViewTenant` | ✅ | ✅ | ✅ |
   | `RenameTenant` | ✅ | ✅ | ❌ |
   | `ManageMembers` | ✅ | ✅ | ❌ |
   | `ManageRoles` | ✅ | ❌ | ❌ |
   | `ManageBilling` | ✅ | ❌ | ❌ |
   | `TransferOwnership` / `DissolveTenant` | ✅ | ❌ | ❌ |

   **Owner-only by deliberate choice:** billing is **financial** and role/ownership changes are the
   **privilege-escalation surface** — keeping both owner-only stops an admin from minting more admins
   or touching money. Apps that want a different posture edit one matrix, not N call sites.
3. **Two enforcement mechanisms mirror the two API styles (ADR-004):** controllers get a
   `RequirePermission(membership, Permission.X)` helper on `TenantApiControllerBase` (returns the
   standard 403 envelope); feature minimal-API groups get a `.RequirePermission(Permission.X)`
   endpoint filter that mirrors `.RequireEntitlement(...)` (ADR-006) but yields **403 Forbidden**
   (authorization), not 402 (payment). The existing `IsOwner` checks are refactored onto
   `RequirePermission` so there is one enforcement path.
4. **Role is read live from membership, never from the JWT.** A role change takes effect on the
   caller's next request with **no token refresh** — the access token carries `tenant_id`, not the
   role (status quo, made explicit here). This is why the seam is a runtime DB-backed check, not a
   claims policy.
5. **Role changes are audited** (ADR-008) — promote/demote records an `AuditEvent` with the actor,
   target, and old→new role.

**Constraints recorded:**
1. **Exactly one owner, always** — the role-change endpoint moves users only between `admin` and
   `member`; it can never set or clear `owner` (that path stays `TransferOwnershipAsync`), and it can
   never target the owner.
2. **No self-escalation / no lockout** — a caller cannot change their own role; an admin cannot act
   on the owner.
3. **Permissions are coarse capabilities, not resource ACLs** — fine-grained per-record sharing is a
   different (deferred) concern; don't grow this into an ACL system without a new ADR.
4. **The matrix is the only place roles map to capabilities** — no new scattered `role == "admin"`
   checks; add a `Permission` and a matrix row instead.
*Rationale:* every downstream B2B app needs an admin tier and will otherwise re-invent role checks ad
hoc. Centralizing the capability mapping once, behind a seam the existing entitlement-filter pattern
already established, makes the common case (add a permission, gate an endpoint) a one-liner and keeps
the owner-only blast-radius items explicit. Pairs with the `ADMIN` (back-office/impersonation) and
`PUBAPI` backlog items, which build on this seam.
Stories + slice plan: `docs/stories/rbac.md` (epic `RBAC`).

---

**ADR-010 — File/blob storage: `IFileStorage` abstraction, local-disk dev default, config-gated S3-compatible prod impl; tenant-scoped keys; signed time-limited download URLs. (2026-06-30)**
The template has no way to store binary content. Avatars, attachments, and the GDPR data-export
artifact (backlog) all block on it, and every downstream app would otherwise re-invent file handling
(and likely leak files across tenants). This adds one storage seam, mirroring the `IEmailSender` →
`SmtpEmailSender` shape (Core abstraction + Infrastructure impl, registered by config presence).

**Decision:**
1. **`IFileStorage` (Core) is the only way to store/retrieve blobs** — `PutAsync`/`GetAsync`/
   `DeleteAsync`/`ExistsAsync` + `GetDownloadUrlAsync`. It **streams, never buffers** whole files
   (bound memory; large uploads/downloads). Features depend on this abstraction, never on a cloud SDK
   or `System.IO` directly — the same rule as "never reference MailKit outside `Infrastructure/Email/`".
2. **Keys are tenant-scoped and enforced server-side.** Every object key is namespaced `{tenantId}/…`
   from `ICurrentTenant`; the storage layer **rejects** keys that escape the tenant prefix or contain
   traversal (`..`, absolute/rooted paths, alternate separators). This is the blob equivalent of the
   `ITenantScoped` global query filter (ADR-003): isolation is structural, not by-convention, and the
   client path is never trusted. No `ICurrentTenant` (system context) ⇒ fail closed.
3. **Two implementations, config-gated like the billing provider (ADR-006).** `LocalDiskFileStorage`
   (root dir from config) is the **dev/test default** so the app boots and the suite runs with **zero
   cloud setup**; an **S3-compatible** impl (`S3FileStorage`, AWS SDK — works with AWS S3, MinIO,
   Cloudflare R2, DO Spaces) is selected when `Storage:S3:*` is configured, else local. Same
   config-presence switch as Stripe-vs-Fake.
4. **Signed, time-limited download URLs — never proxy bytes through the API for the common case.**
   Cloud returns a **native presigned GET URL**. Local disk can't presign, so a **platform endpoint**
   `GET /api/files/{token}` verifies a short-lived token minted with `ITimeLimitedDataProtector` (the
   Data Protection stack is already wired, keys persisted to the DB) and streams the file
   tenant-checked. `GetDownloadUrlAsync` returns the right URL per impl — a **uniform contract** so
   feature code never branches on the backend.
5. **Uploads flow through `IFileStorage.PutAsync` from feature services.** The template ships the
   abstraction + both impls + the download surface; it does **not** prescribe what gets stored or wire
   an upload endpoint to a specific entity (that's a vertical/app concern — horizontal-only template).

**Constraints recorded:**
1. **Tenant isolation is structural** — keys carry the tenant; the layer refuses cross-tenant or
   traversal keys. A feature passes a logical key; the layer prepends/validates the tenant prefix.
2. **Stream, don't buffer** — `PutAsync`/`GetAsync` take/return streams; never read a whole file into
   memory.
3. **Signed URLs are short-lived and scoped to one key** — never a directory/prefix/wildcard; the
   token encodes key + expiry, signed, opaque.
4. **No content sniffing / AV scanning / image processing** here — out of scope; a downstream concern.
   The declared content-type is stored and served back.
5. **Deletion is best-effort idempotent** — deleting a missing key is not an error (parity with cloud
   semantics).
*Rationale:* one storage seam at the platform layer means avatars, attachments, exports, etc. all get
tenant-safe, backend-agnostic file handling for free, and swapping local→S3 is a config change, not a
code change — exactly the property the email and billing seams already give. The signed-URL contract
keeps large transfers off the API process while staying uniform across dev and prod.
Stories + slice plan: `docs/stories/files.md` (epic `FILES`).

---

**ADR-011 — Account & data lifecycle (GDPR): tenant data export + account erasure, built on the existing contributor + dissolve machinery. (2026-06-30)**
Once the template has EU users it needs **data portability** ("download my data") and **erasure**
("right to be forgotten") — legal requirements with real penalties, and a credible trust feature. The
platform already has most of the machinery: the `ITenantDataContributor` seam
(`HasDataAsync`/`WipeAsync`) that each feature registers, the transactional **dissolve** flow, the
**audit log** (ADR-008), and now **file storage** (ADR-010) for the export artifact. GDPR is assembled
from these rather than invented.

**Decision:**
1. **Export mirrors wipe — one more contributor method.** `ITenantDataContributor` gains
   `ExportAsync(tenantId)` (+ an `ExportKey` section name) alongside `HasDataAsync`/`WipeAsync`, so each
   feature contributes its data to a tenant export **the same way** it contributes to teardown — adding
   a feature never means editing a central exporter. A platform `TenantExportService` assembles the
   **core** tenant data (tenant, memberships + member emails, pending invitations) plus every
   contributor's section into one JSON bundle.
2. **The export artifact is a stored file with a signed URL (ADR-010).** The bundle is written via
   `IFileStorage` under a tenant-scoped key and handed back as a **signed, time-limited download URL** —
   never streamed inline, never a permanent link. Owner-only (a new `Permission.ExportData`), audited.
3. **Erasure has two granularities.** **Tenant erasure** is the existing **dissolve** (leave-with-confirm
   → contributors wipe + core teardown) — GDPR adds the **export-then-wipe** option so a tenant can take
   its data before deletion. **User (account) erasure** — "delete my account" — removes the user's
   **identity/PII** (`User`, `UserLogin`, `LoginToken`, `RefreshToken`) but **not** tenant app data
   (that belongs to the tenant, not the user).
4. **Account erasure honors the single-owner invariant (ADR-003).** A sole owner of a tenant with other
   members must **transfer ownership first**; a solo owner's tenant is **dissolved** as part of erasure
   (its data wiped via the contributors); a plain member is simply removed (**not** re-homed — the
   account is going away, unlike leave). Then the identity rows are deleted. The whole operation is one
   transaction and is **audited**.
5. **Erasure vs. audit/legal-hold tension is resolved explicitly.** The audit trail keeps **actor ids,
   never PII**, so erasing a user leaves audit events intact (an id that no longer resolves to a person)
   rather than deleting the compliance record. Where a regulatory **legal hold** requires retaining more,
   the contributor path supports **export-then-wipe**; retention windows are a deployment policy, not
   hard-coded.

**Constraints recorded:**
1. **Export is tenant-scoped and owner-gated** — it contains a whole tenant's data; only the owner may
   request it, and it comes back as a signed URL, not inline bytes.
2. **Export contains identifiers + content, never secrets** — no password/OTP/token hashes, no card
   data, no session tokens; the same rule as audit metadata.
3. **Single-owner invariant is never violated by erasure** — transfer-or-dissolve first; erasure can't
   strand a tenant ownerless.
4. **User app data stays with the tenant** — erasing a user removes their identity, not the
   tenant-scoped records they created (those are the tenant's, and are removed only by tenant dissolve).
5. **Audit survives user erasure** — actor ids remain; audit is not a place PII lives.
*Rationale:* every SaaS with EU users hits this, and re-implementing export/erasure per app is both
wasteful and risky (the failure mode is a cross-tenant data leak or an orphaned tenant). Building both
on the contributor seam + dissolve + file storage keeps the common case a one-liner per feature (add an
`ExportAsync`) and keeps the dangerous invariants (single-owner, tenant isolation, no-secrets) in one
audited place. Depends on: audit (ADR-008, ✅), file storage (ADR-010, ✅), the dissolve flow, and the
permission seam (ADR-009).
Stories + slice plan: `docs/stories/gdpr.md` (epic `GDPR`).

---

**ADR-012 — MFA: authenticator-app TOTP as a step-up after primary auth; secret encrypted at rest; hashed single-use recovery codes. (2026-07-01)**
The template's custom auth stack (ADR-002) has no second factor. ADR-C15 once claimed TOTP via
`AddDefaultTokenProviders()`, but that was superseded by ADR-002 and never built — so this is a genuine
gap, not a re-do. Add authenticator-app **TOTP** (RFC 6238) as an optional second factor, enforced as a
**step-up** after the existing primary auth, reusing the crypto the platform already has.

**Decision:**
1. **TOTP via Otp.NET** (latest stable, no previews — ADR-C10). Per-user secret; enrollment returns an
   `otpauth://…` provisioning URI the client renders as a QR. Verification allows a small time-step
   window (±1) for clock skew; comparisons are constant-time.
2. **The secret is encrypted at rest** with the existing **Data Protection** stack (an `IDataProtector`;
   keys already persisted to the DB). It is **never returned after enrollment and never logged**.
3. **Two user-scoped entities** (identity, not tenant): **`UserMfa`** (`UserId`, `EncryptedSecret`,
   `Enabled`, `EnrolledAt`) — one per user; **`MfaRecoveryCode`** (`UserId`, `CodeHash`, `UsedAt`) —
   single-use, **hashed with the existing `ITokenHasher`** (SHA-256), same pattern as
   `LoginToken`/`RefreshToken`. Both are **wiped by account erasure** (GDPR-2, ADR-011).
4. **Step-up at the auth convergence point.** Every primary-auth path (OAuth callback, magic-link/OTP
   verify, native exchange) resolves a `User` then calls `SessionService.IssueAsync`. When the user has
   MFA enabled, primary auth does **not** issue a full session; it returns an **MFA challenge** — a
   short-lived **signed** token (Data Protection time-limited, like the file-download token) naming the
   user + purpose. `POST /api/auth/mfa/verify` accepts the challenge + a TOTP **or recovery** code and,
   on success, calls `IssueAsync` to complete login. One enforcement path, no per-endpoint duplication.
5. **Recovery codes** are issued once at enrollment (shown once), stored **hashed + single-use**, and
   accepted at the challenge as an alternative to a TOTP code; regenerating invalidates the old set.

**Constraints recorded:**
1. **Secret stays encrypted at rest**, is returned only as the enrollment provisioning URI, and never
   appears in logs or later reads.
2. **Recovery codes are hashed + single-use**, shown exactly once; verification is constant-time.
3. **Step-up is enforced server-side** — the signed MFA challenge is required to complete login; a
   client cannot skip straight to a full session.
4. **MFA is user-scoped PII** — wiped by account erasure (GDPR-2); it is not tenant data.
5. **Enabling requires proving possession** — MFA turns on only after a valid code confirms enrollment
   (never enabled from an unverified secret); disabling likewise requires a valid code.
*Rationale:* MFA is a security baseline any serious SaaS needs, and doing it as a step-up at the single
`IssueAsync` convergence keeps every login path covered without touching each one's transport quirks.
Reusing Data Protection (secret encryption + challenge signing) and `ITokenHasher` (recovery codes)
means no new crypto primitives — only Otp.NET for the standard TOTP math.

**Addendum (MFA-3, 2026-07-01) — redirect paths brought in line with decision point 4.** MFA-2 wired the
step-up only into the **JSON** paths (OTP verify, native exchange); the **web OAuth callback** and
**magic-link verify** still issued a session directly, so an MFA-enabled user could sign in via those and
skip the second factor — an implementation gap against point 4, which always intended *every* primary-auth
path to enforce step-up. MFA-3 routes both redirect handlers through `CompleteOrChallengeAsync`: when a
challenge is returned they redirect to `/login?mfa=<challenge>` instead of `/auth-callback`. The challenge
travelling as a query param is acceptable — it's the same signed, single-use, 5-min Data-Protection token
already returned in JSON elsewhere, carries no secret, and is useless without a live TOTP/recovery code
(same class as an OAuth authorization code in a URL). The client reuses the existing step-up prompt →
`POST /api/auth/mfa/verify`.

**Addendum (MFA-4, 2026-07-01) — native step-up completes the coverage.** The server always challenged the
native OTP/OAuth-exchange paths (point 4), but the MAUI client only understood a tokens response and
treated a challenge as a failure. MFA-4 teaches the client to recognize `{mfa_required, challenge}`
(`AuthService` now returns a `SignInResult`; `VerifyMfaAsync` completes the step-up with tokens in the
body) and reuse the same in-app prompt. Client-only, no API change. **MFA is now enforced on every
sign-in path — web (OTP/OAuth/magic-link) and native (OTP/OAuth) — with no remaining gaps.**

Stories + slice plan: `docs/stories/mfa.md` (epic `MFA`).

---

**ADR-013 — In-app notifications: a per-user notification center + delivery preferences, fanned out through the outbox. (2026-07-01)**
Transactional email exists (`IEmailSender`), but there's no in-app notification center and no per-user
control over how a user is reached. This adds both, reusing the reliable-delivery path the platform
already has (the outbox, ADR-007) rather than a second delivery mechanism.

**Decision:**
1. **`Notification` is per-user, not tenant-scoped.** It's a personal artifact ("your bell menu"), so it
   is keyed by `user_id` and is the sanctioned per-user carve-out (**ADR-C2** — only preferences/personal
   state are per-user; everything else is tenant-scoped). A user reads only their own notifications,
   filtered by the authenticated user id — **not** the tenant filter. Fields: `id`, `user_id`, `kind`
   (stable verb), `title`, `body`, `metadata` (jsonb), `read_at` (nullable), `created_at`.
2. **One fan-out entry point.** `INotificationService.NotifyAsync(userId, kind, title, body, metadata)`
   is the single call a feature makes to notify a user. It creates the **in-app** row **transactionally**
   (a DB write in the same unit of work as the triggering change — no extra reliability machinery needed)
   and, per the user's preferences, dispatches the **email** copy through `IEmailSender` — which is
   already the **outbox-backed** sender (ADR-007), so the out-of-process channel is reliable + retried.
   One domain event → one call → both channels, each delivered by the right mechanism.
3. **Per-user delivery preferences** (`NotificationPreference`, keyed by `user_id`): channel toggles
   (in-app / email), defaulting to on. This is the ADR-C2 per-user preference, alongside `User.Locale`.
   The fan-out consults it; a feature never hard-codes channels.
4. **A user-scoped notification-center API** — list (paginated), unread count, mark-one/all read, and
   get/update preferences. All scoped to the caller (`NameIdentifier` claim), like `/api/auth/me` — never
   tenant-filtered, never another user's notifications.
5. **No new delivery infrastructure.** In-app = a DB row; email = the existing outbox path. The template
   ships the center + the fan-out seam; a feature calls `NotifyAsync`, it does not wire channels itself.

**Constraints recorded:**
1. **Per-user, never cross-user** — every read/write is scoped to the authenticated user; a notification
   is only ever visible to its owner.
2. **In-app is transactional, email is outbox-reliable** — don't push the in-app insert through the
   outbox (it's a same-DB write); don't send email inline (use the outbox-backed `IEmailSender`).
3. **Preferences gate delivery** — the fan-out reads prefs; no channel is hard-coded at a call site.
4. **Notifications are user PII** — wiped by account erasure (GDPR-2, ADR-011), like the other
   user-scoped identity rows.
5. **No secrets/PII beyond identifiers in `metadata`** — same rule as audit metadata.
*Rationale:* the next ask after transactional email is almost always an in-app center + "how do you want
to be reached." Keying it per-user (ADR-C2) and fanning out through the existing outbox keeps it a thin,
reliable addition — a feature gets multi-channel, preference-aware notification from a single call, with
no new delivery machinery to operate.
Stories + slice plan: `docs/stories/notify.md` (epic `NOTIFY`).

---

**ADR-014 — Admin back-office: a config-gated platform-staff surface for cross-tenant inspection + audited, short-lived impersonation. (2026-07-01)**
Support and debugging at scale need a **platform-staff** surface — outside the tenant model — to inspect
any tenant and, when necessary, "sign in as" a user. This is the **highest-blast-radius** feature in the
platform, so it is built entirely on the guardrails already in place (the audited cross-tenant escape
hatch, ADR-003; the audit log, ADR-008) rather than loosening any of them.

**Decision:**
1. **Platform staff is a config allowlist, not a self-serve role or a DB flag.** The set of staff is
   configured **out-of-band** (`Admin:StaffEmails`, from `.env`/env vars), checked by
   `IPlatformStaffService` and enforced by an **`AdminOnly`** authorization gate (403 for anyone not on
   the list). It is deliberately **not** part of `TenantRoles`/the RBAC matrix (that's tenant-scoped) and
   **not** a toggle reachable from the app — the highest privilege can only be granted by whoever controls
   deployment config.
2. **Cross-tenant reads go through the audited `QueryAllTenants()` escape hatch only (ADR-003).** The
   global tenant filter is **never loosened**; admin read endpoints use the same audited hatch feature
   slices are forbidden from using, re-constrained to the target tenant. Admin is read-only over tenant
   data (inspect, don't mutate).
3. **Impersonation issues a short-lived, non-refreshable, loudly-audited access token for the target
   user.** "Sign in as" mints an access token carrying the target's claims **plus an `impersonated_by`
   claim** (the staff user id) and a **short expiry**, with **no refresh token** — so it auto-expires and
   can't be silently extended. The impersonator acts as the target within that window; the token scopes
   naturally via the target's `tenant_id` claim (no filter bypass).
4. **Every admin action is audited (ADR-008), prominently.** Cross-tenant reads and — especially —
   impersonation start record an `AuditEvent` with the staff actor + target; impersonation is stamped in
   the **target's** tenant so that tenant's owner can see "a platform admin accessed this account."
5. **No standing admin session over tenant data.** Staff authenticate as normal users (their own
   account); the admin surface is gated per-request by the allowlist. There is no separate admin login.

**Constraints recorded:**
1. **The global filter is inviolable** — admin never turns it off; cross-tenant reads use the audited
   hatch, scoped to the target.
2. **Staff membership is out-of-band config** — never settable via the app, never a tenant role.
3. **Impersonation is short-lived + non-refreshable + audited** — no refresh token, minutes-not-hours
   expiry, an `impersonated_by` claim, and a loud audit record in the target's tenant.
4. **Admin is read-only over tenant data** — inspection + impersonation, not direct cross-tenant writes
   (a staff member who needs to change tenant data does it *through* impersonation, which is audited).

**Addendum (2026-07-02, ADMIN-3):** the admin surface gains **staff announcements** —
`POST /api/admin/tenants/{id}/announce` notifies every member of a tenant through the normal NOTIFY
fan-out (ADR-013; per-user prefs decide in-app vs outbox email) and records `admin.announcement.sent`
in the target tenant. This is the one sanctioned admin write: it creates **per-user notification rows
only** — tenant data stays read-only, the filter stays engaged (`EnterTenant`), and the action is as
loudly audited as inspection/impersonation. Also fixed en route: the web client's staff probe
(`AuthService.IsStaffAsync`) ran on the Bearer-less auth HttpClient, making `/admin` unreachable on
web; it now attaches the in-memory access token explicitly.
5. **No secrets/PII in admin responses or audit metadata** beyond identifiers — same rule as elsewhere.
*Rationale:* support tooling is necessary but dangerous; the safe way to build it is to reuse the audited
escape hatch and the audit log instead of adding new privileged paths, and to keep the staff grant in
deployment config where it can't be escalated from inside the app. Impersonation as a short-lived,
non-refreshable, audited token gives support what they need while bounding the blast radius and leaving a
trail the affected tenant can see. Depends on: audit (ADR-008 ✅), the escape hatch (ADR-003 ✅), RBAC
(ADR-009 ✅).
Stories + slice plan: `docs/stories/admin.md` (epic `ADMIN`).

*Amendment (v2 audit, 2026-07-01) — impersonation + tenant-detail reads use `EnterTenant`, not
`QueryAllTenants()` "only".* Decision point 2 above describes cross-tenant reads going through the
audited `QueryAllTenants()` hatch. As shipped, the **tenant list** uses non-scoped tables directly
(`ITenantRepository.ListAllAsync`, no hatch), while **tenant-detail inspection and impersonation
audit-writes enter the target tenant via `ITenantContext.EnterTenant`** (ADR-003 amendment 2026-06-25)
so the scoped reads/writes go through the normal filter engaged — rather than loosening it. The filter
is still never disabled; `EnterTenant` was chosen over the hatch precisely because it keeps scoping
*on*. The `docs/stories/admin.md` Gherkin/prose has been reconciled to name `EnterTenant`.

---

**ADR-015 — Public API + API keys: a config-gated, default-off programmatic surface authenticated by tenant-scoped API keys. (2026-07-01)**
Everything so far serves an interactive human (JWT/cookie session). A **public API** serves *machines* —
a customer's backend, a script, CI, an integration — which need a non-interactive credential. The user
initially parked this (no customer-facing API) and **reversed that on 2026-07-01**; it's built now, but
**off by default** since a public surface is a deliberate, security-relevant opt-in.

**Decision:**
1. **`ApiKey : ITenantScoped`** — store only the **hash** (like `RefreshToken`/`TenantInvitation`; reuse
   `ITokenHasher`), plus a non-secret `Prefix` for display, granted `Scopes`, optional `ExpiresAt`, and a
   `RevokedAt`. The raw key (`pk_…`) is shown **once** at creation, never again.
2. **A second authentication scheme** (`ApiKeyAuthenticationHandler`, scheme `"ApiKey"`) alongside JWT
   Bearer. A key in `X-Api-Key` (or `Authorization: Bearer pk_…`) resolves — **across tenants, pre-scope**
   (the key selects its tenant) — to a principal carrying the **`tenant_id` claim**, so the existing global
   query filter scopes the request with no extra wiring. Bad/expired/revoked ⇒ 401.
3. **Scopes gate routes.** Keys carry example scopes (`read`/`write`); a public route declares its
   requirement with **`.RequireApiScope(...)`** (→ 403 `insufficient_scope`). Scopes are a superset seam —
   an all-scope key = full access — so it's mechanism-first without committing to a scope taxonomy.
4. **Owner-only management.** `/api/apikeys` (create/list/revoke) is JWT-authed and gated by a new
   **`Permission.ManageApiKeys`** (owner-only by construction — owner gets every permission). Keys grant
   programmatic tenant access, so minting them is as sensitive as billing/role changes.
5. **Config-gated, STRONG gating.** `PublicApi:Enabled` (default false). When off, the API-key scheme
   isn't added and neither the management nor public routes are mapped — they return **404, they don't
   exist** (not merely 403). Minimal-API groups (not controllers) make the conditional mapping clean.

**Constraints recorded:**
1. **Only the hash is stored**; the raw key is revealed once. Revoked/expired keys never authenticate.
2. **API-key requests are tenant-scoped exactly like user requests** (same `tenant_id` claim → same global
   filter); a key can never reach another tenant's data.
3. **Default off** — a deployment opts into the public surface deliberately; the attack surface (a
   long-lived credential + published routes) doesn't exist until then.
4. **Reuses existing rails** — token hashing, the tenant filter, RBAC (`.RequirePermission`), and the
   minimal-API feature-group convention. No new crypto.
*Rationale:* a public API is the "others build on this" layer; doing it as a second auth scheme that mints
the same `tenant_id`-scoped principal means the entire tenant-isolation guarantee applies for free, and
strong config-gating means the template ships the capability **dormant** rather than exposing a surface no
one asked for. HOOKS (outbound webhooks) is the companion outbound half (ADR-016).
Stories + slice plan: `docs/stories/pubapi.md` (epic `PUBAPI`).

*Amendment (v2 audit, 2026-07-01) — PUBAPI-2 shipped: per-key rate limiting + a leak-free public
OpenAPI doc.* Hardening beyond PUBAPI-1: a **per-API-key rate-limit policy** (`RateLimiting.PublicApiPolicy`,
partitioned by key so one tenant's key can't exhaust another's budget) on the public routes, and a
**curated, leak-free public OpenAPI document** served **anonymously** at `GET /api/public/openapi.json`
that emits **only** the public routes (never the internal/management surface). Both live behind the same
`PublicApi:Enabled` gate (off ⇒ absent). Still open: key **rotation** and a real scope taxonomy. Tests:
`RateLimitingTests` (per-key isolation). See `docs/stories/pubapi.md`.

---

**ADR-016 — Outbound webhooks: tenant subscriptions delivered through the transactional outbox, HMAC-signed, config-gated default-off. (2026-07-01)**
The **outbound** half of the integration story (ADR-015 is inbound): let a tenant subscribe to events in
their data so *their* systems are notified (push) instead of polling. Also parked-then-un-parked on
2026-07-01; **off by default** for the same reason as PUBAPI (a new outbound surface is a deliberate opt-in).

**Decision:**
1. **`WebhookSubscription : ITenantScoped`** — a tenant registers a `Url` + the `EventTypes` it wants, with
   a per-subscription **signing secret** stored **encrypted** (Data Protection — the plaintext is needed to
   HMAC-sign, so it can't be hashed; same approach as the MFA secret), revealed once at creation.
2. **Delivery IS the outbox pointed outward** (ADR-007) — don't build a second delivery mechanism.
   `IWebhookPublisher.PublishAsync(eventType, data)` fans out to every active matching subscription,
   enqueuing **one `"webhook"` outbox message per subscription** (staged on the caller's unit of work, so
   it's atomic with the triggering change). The `WebhookOutboxHandler` signs + POSTs each; a **non-2xx
   throws**, so the outbox's existing **retry/backoff + dead-letter** apply for free.
3. **HMAC-SHA256 signatures.** Each POST carries `X-Webhook-Id` (event id, for receiver dedup — deliveries
   are at-least-once), `X-Webhook-Event`, and `X-Webhook-Signature: sha256=<hex>` over the raw body. The
   receiver recomputes with the shared secret to verify authenticity + integrity.
4. **Owner-only management** (`/api/webhooks`, new **`Permission.ManageWebhooks`**): register/list/remove,
   plus a **synchronous "send test"** (`/{id}/test`) that POSTs a `ping` and returns the endpoint's status,
   so the owner gets immediate feedback (real events are async via the outbox).
5. **Config-gated, STRONG gating.** `Webhooks:Enabled` (default false). Off ⇒ the management routes aren't
   mapped (404); the delivery handler is registered but dormant (no subscriptions ⇒ nothing to deliver).

**Constraints recorded:**
1. **Signing secret encrypted at rest**, revealed once; every delivery is signed so receivers can verify.
2. **At-least-once, out-of-order** delivery (outbox semantics) — receivers dedup on `X-Webhook-Id`.
3. **Reuses the outbox** — no bespoke retry/backoff/dead-letter; deliveries are durable + tenant-scoped.
4. **Default off** — the outbound surface doesn't exist until a deployment enables it.
*Rationale:* webhooks are the "our product notifies your systems" half of being a platform; building them
as the outbox pointed outward means durability, retries, and atomicity-with-the-change come for free, and
the only new parts are the subscription model + signed HTTP POST. A tenant-facing **delivery log**
(per-attempt history) is a natural HOOKS-2 follow-up — until then the outbox's own status/attempt/error
columns are the record.
Stories + slice plan: `docs/stories/hooks.md` (epic `HOOKS`).

*Amendment (v2 audit, 2026-07-01) — HOOKS-2 shipped: delivery log + replay.* The "natural follow-up"
above is now built. A **`WebhookDelivery`** record is written per delivery attempt (retries add rows):
event type/id, the exact `Body` sent, `success`, `status_code`, `error`, `created_at`. Owner endpoints
under `/api/webhooks` view the log and **replay** a delivery (re-enqueues the retained body through the
outbox). Like `OutboxMessage`, `WebhookDelivery` is deliberately **not** `ITenantScoped` (it's written
from the tenant-less outbox dispatcher); its `TenantId` is a plain filter column the read side scopes
on. See `docs/DATA_MODEL.md` and `docs/stories/hooks.md`.

**ADR-017 — Hosting: free-tier single-origin deployment — Render (API serving the WASM bundle) + Neon Postgres + Brevo. (2026-07-02)**
Resolves the hosting decision deferred in `docs/TECH_STACK.md` ("pick near deploy"). The driver set:
**$0/mo, no credit card, the refresh-token cookie must stay first-party, and the in-process background
jobs (outbox dispatcher / scheduler / lapse sweep) must not be silently broken.** Decided:

1. **Single origin.** The API container **also serves the published Blazor WASM bundle** (framework
   files + SPA fallback to `index.html`, with `/api/**` excluded from the fallback). One origin means
   the refresh cookie is always first-party — the entire third-party-cookie failure class (Safari ITP,
   Chrome's phase-out) vanishes, and per-environment CORS configuration disappears. **This does not
   weaken the clean-API-boundary rule (golden rule 2 / ADR-004):** the UI still consumes the API over
   HTTP only; the API merely serves its static files. Local dev keeps the separate `src/Web` dev
   server (hot reload), and the `BlazorClient` CORS policy remains for it + native clients.
2. **Render free** hosts the container (512 MB, TLS + subdomain included, deploy hooks, no card
   required). **Accepted trade-off, recorded:** free instances sleep after ~15 min idle — first
   request cold-starts (~30–60 s) and the outbox/scheduler pause while asleep (queued sends resume on
   wake). Acceptable for staging QA; **prod requires an always-on plan (~$7/mo) or equivalent — never
   ship paid users on a sleeping instance.** The image is plain Docker, so the exit cost is nil.
3. **Neon free** is the Postgres (17), used as plain Postgres (Neon Auth stays off — this template owns
   auth, ADR-002). Chosen over Supabase for this role: it is *just* Postgres (no redundant auth/storage
   platform beside our own), and it **auto-wakes in ~1 s** from autosuspend vs Supabase's 7-day idle
   pause needing a manual unpause. **Connection:** use the **direct** endpoint over TLS — a single
   instance keeps its own Npgsql pool, and this app polls (no `LISTEN/NOTIFY`) and uses no server-side
   prepared statements, so it doesn't need PgBouncer. (Neon's pooled `-pooler` endpoint is
   transaction-mode; the app is compatible with it but only benefits it at many-instance scale.) Bonus
   noted for later: Neon DB branching enables free per-preview-environment databases.
4. **Brevo** (free, 300 mails/day) is staging + prod SMTP through the existing `IEmailSender` — it was
   already the template's assumed real provider in the `.env` docs. **Consequence:** staging has no
   Mailpit, so email-based QA cases use real (plus-addressed) inboxes there, and the automated
   post-deploy smoke checks health/app-shell only, never email journeys.
5. **Environments follow the git model:** `develop` auto-deploys **staging** (behind CI + a
   post-deploy smoke gate); `main` deploys **prod** behind a required-approval GitHub environment —
   preserving "`main` is deploy-only". The template proves the machinery on staging; actual prod
   provisioning is each downstream app's first deployment step (runbook: `docs/DEPLOYMENT.md`).
6. **Proxy correctness, gated:** `UseForwardedHeaders` (for/proto) is added **config-gated, default
   off** — required behind Render's TLS-terminating proxy (else the per-IP passwordless rate limiter
   collapses into one shared bucket and OAuth redirect URIs generate as `http`), but an IP-spoofing
   vector if honored when *not* behind a proxy.

**Alternatives rejected:** **Vercel / Cloudflare Pages for the WASM** — split origins make the refresh
cookie cross-site (broken in Safari today, Chrome tomorrow) unless a custom domain unifies the two
hosts; with single-origin hosting a second platform is pure liability. **Railway** — excellent DX but
no longer free ($5/mo Hobby after the one-time trial credit). **Google Cloud Run** — a real free tier,
but CPU is throttled to ~zero between requests, which breaks the in-process outbox *subtly* (worse
than Render's honest sleep), and it requires a card. **Supabase as the DB** — workable, but free
projects pause after 7 idle days (manual unpause) and we would use ~10 % of the platform. **Oracle
Cloud Always Free VM** — the only truly-free *always-on* option; rejected for account-reclamation
risk, noted in the runbook as the self-host escape hatch. A **custom domain** (~$10/yr) is the
deliberate first paid upgrade (pretty URLs + DKIM deliverability); nothing in the architecture
depends on it.
Stories + slice plan: `docs/stories/deploy.md` (epic `DEPLOY`).

**ADR-018 — Native (MAUI) client: commit to full feature parity across Android/Windows/iOS/macOS, incl. automated native tests + signed distribution. (2026-07-02)**
Resolves the deferred "non-web framework commitment" from `docs/TECH_STACK.md`. The template already ships
**MAUI Blazor Hybrid** shells that reuse the shared RCL (`Shared.Ui`) and have native auth wired (OTP,
OAuth via system browser, MFA step-up MFA-4, secure-storage tokens) — so the native clients already render
every web screen. We commit to closing the remaining gap to **full parity**: verify every feature on
native, fix WebView-vs-browser deltas, test the native build + UI in CI, and produce **signed, shippable
artifacts** for all four platforms. Decided:

1. **MAUI is the native stack** (not Uno/Avalonia/PWA). Rationale: it reuses the exact C# Blazor
   components already built, so parity is verification + glue, not a second UI. Alternatives stay noted in
   `TECH_STACK.md` as fallbacks if MAUI's maturity disappoints.
2. **Parity means "what web does", not more.** OS push notifications, biometrics, and other native-only
   capabilities are **beyond parity** and out of scope for this epic (future epics if wanted). The
   in-app notification center (NOTIFY, polling) is the parity bar, not native push.
3. **Web-first still holds** (golden rule 5): features land + prove on web first; this epic keeps native
   *caught up*, it does not invert the order.
4. **Full-platform scope accepts real, recorded costs** (the user opted into "everything"): a **macOS CI
   runner** (to build/test/sign iOS + macCatalyst), an **Apple Developer account** ($99/yr), and
   **signing material managed as repo secrets** (Android keystore, Windows cert, Apple cert+profile,
   base64-encoded, never committed — same discipline as `.env`, ADR-001). Without the macOS runner +
   Apple account, the Apple-platform slices can't run — so they're sequenced last, after the
   Android/Windows path proves the machinery.
5. **Sequenced in waves** (guardrails → gap-fixes → verification → distribution): a build gate + a
   `docs/NATIVE_PARITY.md` audit first (scopes everything), then WebView-gap fixes, then a manual +
   automated native QA pass, then per-platform signing/packaging. Don't automate or distribute before the
   app is verified working.

**Accepted trade-off:** native UI tests (Appium / .NET MAUI UITest on emulators/simulators) are slower and
flakier than Playwright-web — kept to a small smoke suite with retries; the manual native QA pass is the
broader safety net. **The honest counterweight:** automated native E2E + store distribution are large and
partly per-app; committing the template to them (vs deferring) is a deliberate choice to make native a
first-class, shippable channel rather than an experiment.
Stories + slice plan: `docs/stories/native.md` (epic `NATIVE`).
