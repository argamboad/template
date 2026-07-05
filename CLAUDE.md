# CLAUDE.md

> Operating manual for Claude Code on this project. Auto-loaded each session — keep it tight.
> Constant rules are pre-filled; fill the app-specific placeholders during conceptualization.

## What this project is
<!-- One-paragraph summary: what the app is and its core loop. -->
_TODO_ — full context in `docs/PROJECT_BRIEF.md`.

## Read before you act
- **Writing or modifying ANY code → `docs/audits/v2-2026-07/FOUNDATION_RULES.md` (v1.0, R1–R35) is
  binding.** It encodes the post-audit invariants (tenancy, second-factor/event replay, SSRF,
  fail-closed normalization, atomic quotas, per-user erasure, injected clocks, slice boundaries) as
  machine-enforced arch tests + CI gates. Comply; if a task seems to require violating a rule, stop and
  surface it. The frozen quality bar lives in `CONTRIBUTING.md`.
- Touching the schema or entities → read **`docs/DATA_MODEL.md`** first.
- Implementing a screen or flow → read **`docs/FEATURES.md`** first.
- Starting a build slice → read **`docs/WAYS_OF_WORKING.md`** (slices, story format, PR/commit
  conventions).
- Adding an app feature → follow the **clean-platform + vertical-slice convention** in
  `docs/WAYS_OF_WORKING.md` (and ADR-004); copy `src/Api/Features/Notes` as the reference, then
  delete the Notes sample.
- Wondering *why* something is the way it is → check **`docs/DECISIONS.md`** before changing it.
- Changing a settled decision → add a new dated ADR in `docs/DECISIONS.md`; don't silently
  reverse it.
- Rebranding (name, logo, colours, tagline) → follow **`docs/REBRANDING.md`** and complete every
  item. It explicitly covers the **transactional email templates** (`src/Infrastructure/Email/` —
  `BrandedEmail.cs` + `Assets/logo.png`), which are inline and easy to miss; a rebrand that skips
  them is incomplete.

## Golden rules — constant (do not violate)
1. **Tenant-scoped, not user-scoped.** App data belongs to the tenant; never leak across tenants.
   Tenant entities implement `ITenantScoped` and are filtered automatically by a global EF query
   filter (see ADR-003); genuinely cross-tenant/pre-auth reads use the sanctioned escape hatch
   `IRepository<T>.QueryAllTenants()`, and signature-/system-authenticated tenant-scoped writes
   (the billing webhook, admin impersonation) enter their tenant via `ITenantContext.EnterTenant`.
   `IgnoreQueryFilters()` is **banned in `src/Api/Features/**`** (fails CI). Only preferences are
   per-user.
2. **Clean API boundary.** The UI is a client of the API and never accesses the DB directly.
3. **Blazor UI components live in the shared RCL**, not inline in the web app — keeps non-web
   clients cheap.
4. **Derived values are computed, never stored** as stale flags (confirm the app's specific
   derived rules in `docs/DATA_MODEL.md`).
5. **Web-first for features.** The platform ships MAUI desktop + Android shells with auth wired
   (see `docs/MOBILE_TESTING.md`); build each app feature on web first and extend the native
   shells only once it works there.
6. **Latest stable versions only, never previews.**
7. **Test-Driven Development — always.** Write the failing test before the production code on
   every slice. Unit tests (xUnit) in `Core.Tests` / `Api.Tests`; E2E tests (Playwright/NUnit)
   in `E2E.Tests`. No slice merges without tests that drove it; Gherkin scenarios map 1:1 to tests.
8. **Work in vertical slices.** Each slice is end-to-end and leaves the app working; follow
   `docs/WAYS_OF_WORKING.md` for slices, Gherkin stories, Conventional Commits, and the PR
   template. Don't build sprawling multi-epic chunks — propose a split.

## Golden rules — app-specific
<!-- Add the domain rules that must never be violated (e.g. the cocktail app's "makeable is
     always derived"). These come out of DATA_MODEL.md's derived rules. -->
_TODO_

## Tech stack (see `docs/TECH_STACK.md`)
- **Versions:** latest stable on the current .NET line — **re-verified 2026-06-17: .NET SDK 10.0.301, ASP.NET Core / EF Core 10.0.9, Npgsql.EF 10.0.2, PostgreSQL 17.**
- **Backend:** ASP.NET Core Web API behind a clean API boundary.
- **Web frontend:** Blazor WebAssembly; UI components in a shared **RCL** (hard rule).
- **DB:** PostgreSQL via **EF Core (Npgsql)**; schema/migrations generated from `docs/DATA_MODEL.md`.
- **Auth:** custom JWT access tokens + rotating refresh tokens — **not** ASP.NET Core Identity
  (see ADR-002); tenant scoping layered on top via a global query filter.
- **Non-web clients:** MAUI Blazor Hybrid (mobile + Win/macOS desktop) — *deferred, don't build now.*

## Auth rules (constant)
- **Secrets are never in appsettings.** In dev they live in the gitignored repo-root **`.env`**
  (loaded by the API via DotNetEnv; the single local source of truth — see ADR-001); in
  production they come from real environment variables. Keys use the `Section__Sub` form.
  `.env.example` (committed) documents them. Never commit `.env`.
- **New OAuth provider = one line.** Add `.AddXxx()` in `ServiceCollectionExtensions`. Don't
  restructure anything else.
- **Passwordless sign-in uses the `LoginToken` entity + `PasswordlessService`** (NOT Identity
  token providers). Magic links and email OTP are single-use, hashed, and time-limited; lifetimes
  are in config (`Auth:MagicLink:TokenLifespanMinutes`, `Auth:Otp:*`).
- **`IEmailSender` (Core abstraction) is the only way to send email.** Never reference MailKit
  directly outside `Infrastructure/Email/`.
- **JWT Bearer auth is configured** in `Program.cs` (validates the app-issued access token, scheme
  `JwtBearerDefaults.AuthenticationScheme`). The token carries a `tenant_id` claim that drives
  tenant query scoping.

## Scope discipline
Before building anything, check the **"OUT" list in `docs/PROJECT_BRIEF.md`**. Don't implement
deferred items without an explicit decision.

## Conventions
- Code term for the tenant is **tenant**; the reference implementation's app-facing label is
  **Household** (`/api/household`, `HouseholdController`). Rename per app — see `docs/REBRANDING.md`.
- _TODO: app-specific conventions (naming, lookup tables, etc.)_

## Status / not yet decided
- Seed data (if any) — _TODO_.
- Concrete schema (EF Core migrations) — generated from `docs/DATA_MODEL.md`.
- **User stories: generated per-epic at build time**, under `docs/stories/` (one file per epic).
- Deferred sub-decisions: non-web framework commitment (see `docs/TECH_STACK.md`). Hosting is now
  **decided** (ADR-017: Render free single-origin + Neon + Brevo) — built by epic `DEPLOY`.

## Doc map
| File | Purpose |
|------|---------|
| `CLAUDE.md` (root) | This file — operating manual, auto-loaded |
| `docs/PROJECT_BRIEF.md` | Why/what/scope (lean PRD) + OUT list |
| `docs/FEATURES.md` | User flows & behavior |
| `docs/DATA_MODEL.md` | Entities, relationships, derived rules |
| `docs/TECH_STACK.md` | Stack choices + rationale |
| `docs/DECISIONS.md` | ADR log (the "why") |
| `docs/WAYS_OF_WORKING.md` | Slices, story format, commit/PR conventions |
| `docs/REBRANDING.md` | Every brand touchpoint to replace per app — **incl. the email templates** |
| `docs/LOCALIZATION.md` | i18n setup (EN/ES live) + how to add a language |
| `docs/MOBILE_TESTING.md` | Run/sign-in on the Android emulator (adb reverse, OAuth) |
| `docs/QA_TEST_PLAN.md` | Manual QA plan — step-by-step tests across web + all four native platforms (117 cases: smoke + regression + §13c native release checklist) |
| `docs/ROADMAP.md` | Sequenced plan — pillars done (JOBS/BILLING/OBS) + the next waves (RBAC, files, GDPR, MFA, …) |
| `docs/STATUS.md` | 2026-07-04 status snapshot + operator guides — native QA pass, Apple first-run smoke (MacBook walkthrough), prod activation; SaaS-readiness assessment |
| `docs/PLATFORM_BACKLOG.md` | Per-item design sketches for the future foundation slices (the detail behind ROADMAP) |
| `docs/stories/` | User stories per epic — generated at build time |
| `docs/stories/billing.md` | epic `BILLING` ✅ COMPLETE — entitlements + Checkout + webhook + Portal (1–4) + seat/usage quotas (5, `IQuotaService`) + trial/dunning (6, `IBillingNotifier` + lapse sweep via NOTIFY) + dissolve cleanup (7, `BillingDataContributor` cancels the provider sub + wipes the projection) + billing page (8, `GET /api/billing` summary + `/billing` UI, fake-provider E2E upgrade loop); ADR-006 |
| `docs/stories/async-jobs.md` | epic `JOBS` ✅ COMPLETE — outbox+dispatcher, inbox, scheduler (ADR-007) |
| `docs/stories/observability.md` | epic `OBS` ✅ COMPLETE — logging, OpenTelemetry, health, append-only audit log (ADR-008) |
| `docs/stories/rbac.md` | epic `RBAC` ✅ COMPLETE — `admin` role + permission seam (RBAC-1) + owner-only role change (RBAC-2) + admin-aware roster UI (RBAC-3); ADR-009 |
| `docs/stories/files.md` | epic `FILES` ✅ COMPLETE — `IFileStorage` local/S3, tenant-scoped keys, signed URLs (FILES-1 abstraction, FILES-2 download, FILES-3 S3); ADR-010 |
| `docs/stories/gdpr.md` | epic `GDPR` ✅ COMPLETE — tenant data export + account erasure on the contributor/dissolve/file-storage machinery (GDPR-1 export, GDPR-2 erasure); ADR-011 |
| `docs/stories/mfa.md` | epic `MFA` ✅ COMPLETE — authenticator TOTP; Otp.NET, secret encrypted, hashed recovery codes (MFA-1 enroll/manage, MFA-2 JSON-path step-up, MFA-3 OAuth/magic-link redirect step-up, MFA-4 native step-up — enforced on **every** sign-in path); ADR-012 |
| `docs/stories/notify.md` | epic `NOTIFY` ✅ COMPLETE — per-user in-app notification center + delivery prefs, fan-out via the outbox (NOTIFY-1 center, NOTIFY-2 prefs+email); ADR-013 |
| `docs/stories/admin.md` | epic `ADMIN` ✅ COMPLETE — config-gated platform-staff surface: cross-tenant inspection + short-lived audited impersonation + staff announcements via NOTIFY fan-out (ADMIN-1 gate/inspect, ADMIN-2 impersonate, ADMIN-3 announce — audited in-tenant, per-user rows only); ADR-014 |
| `docs/stories/pubapi.md` | epic `PUBAPI` — public API + tenant API keys, **config-gated default-off** (PUBAPI-1 ✅ — hash-only keys, API-key auth scheme → `tenant_id`-scoped principal, owner mgmt, scoped `/api/public`; PUBAPI-2 ✅ — per-key rate limit + anonymous public OpenAPI doc `/api/public/openapi.json`); ADR-015 |
| `docs/stories/hooks.md` | epic `HOOKS` — outbound webhooks, **config-gated default-off** (HOOKS-1 ✅ — `WebhookSubscription` encrypted secret, `IWebhookPublisher` fan-out → outbox → HMAC-signed POST w/ retry, owner `/api/webhooks` + send-test; HOOKS-2 ✅ — delivery log + replay); ADR-016 |
| `docs/stories/e2e.md` | epic `E2E` ✅ COMPLETE — Playwright journeys (suite 7→26 tests): E2E-1 RBAC roster; E2E-2 billing seat-quota 402 UX; E2E-3 notification bell/prefs (list/mark-read covered via ADMIN-3 announcements); E2E-4 magic-link sign-in (happy + single-use); E2E-5 membership lifecycle (transfer/leave/dissolve/delete-account); BILLING-8 added the fake-provider upgrade-loop journey. Health = DEPLOY-3 smoke, not a browser test |
| `docs/stories/deploy.md` | epic `DEPLOY` ✅ COMPLETE — staging/prod on the free tier: DEPLOY-1 single-origin (API serves the WASM) + config-gated forwarded headers; DEPLOY-2 Dockerfile + compose parity + `render.yaml` + `docs/DEPLOYMENT.md` (staging live, all 4 sign-in paths verified); DEPLOY-3 CI deploy pipeline (develop→staging auto + version-gated smoke, main→prod gated) + QA §1.5; ADR-017 |
| `docs/DEPLOYMENT.md` | Deployment runbook (DEPLOY-2/3) — Render + Neon + Brevo free-tier bring-up; `Dockerfile` + `render.yaml` reference; required env incl. the Production Stripe-key guard; §6 CI-gated auto-deploy |
| `docs/stories/native.md` | epic `NATIVE` 🚧 — full MAUI parity (Android/Windows/iOS/macOS): NATIVE-1 ✅ CI build gate (all 4 TFMs; Apple legs on develop pushes — 10× macOS minutes; Maui lockfile excluded by design) + NATIVE-2 ✅ parity audit → gaps G1–G6 + NATIVE-4b ✅ join-by-invite-code on /join (G5 closed; E2E suite 26→28) + NATIVE-5 ✅ culture bootstrap (G6 closed: ICulturePersistence seam, MAUI Preferences + MauiProgram bootstrap; Windows-verified via WebView2 CDP) + NATIVE-3 ✅ downloads (G1 closed: Content-Disposition attachment + IFileDownloadLauncher — web same-tab download, native OS share sheet; E2E suite 28→29); + NATIVE-4 ✅ (G2 refresh-on-resume AppResumeNotifier + G3 Android back handler) — Wave 2 COMPLETE, all six gaps closed; NATIVE-6 QA plan authored (117 cases: DSK-08..14, AND-07..13, iOS/mac first-run smoke, release checklist) + G7 Apple-boot fix (iOS/macCatalyst crashed at startup — WebAuthenticator initiator generalized + Info.plist schemes); next: USER runs the device pass (Android/Windows now; Apple column PINNED until the user has Apple hardware) + NATIVE-7 ✅ COMPLETE non-Apple (both smokes GREEN in CI: Windows WebView2-CDP + Android emulator playwright-core _android; native-paths gate skips Apple/smoke legs on docs-only pushes; deploy-staging concurrency); iOS-sim leg pinned; NATIVE-6 QA pass + NATIVE-7 emulator smoke; NATIVE-8/9/10 signed AAB/MSIX/IPA/pkg + NATIVE-11 store submission; ADR-018 |
| `docs/NATIVE_PARITY.md` | NATIVE-2 audit — WebView-vs-browser deltas × platform × screen (✅/⚠️/🔍 verdicts); gap register G1–G6 → Wave-2 slices; maintainer rules (index.html sync, emailed links land on web, forceLoad = leaves the app) |
| `.github/pull_request_template.md` | PR checklist (auto-loaded by GitHub) |
| `src/Infrastructure/Persistence/Migrations/` | Concrete schema — EF Core migrations generated from DATA_MODEL.md |
