# Roadmap

> The **sequenced** view of platform work: what's done, and what's planned next in priority waves.
> This is the *ordering*; the per-item design sketches live in `docs/PLATFORM_BACKLOG.md`, the decisions
> in `docs/DECISIONS.md`, and per-epic Gherkin stories under `docs/stories/`. Each planned item becomes
> an epic when picked up (ADR + story file + branch-per-slice — see `docs/WAYS_OF_WORKING.md`).
>
> **Sizes:** S ≈ a slice or two · M ≈ a small epic · L ≈ a multi-slice epic.

## Status (2026-06-26) — the three foundation pillars are DONE

| Pillar | Shipped | ADR / stories |
|--------|---------|---------------|
| **JOBS** | transactional outbox + inbox + scheduled-jobs host | ADR-007 / `stories/async-jobs.md` |
| **BILLING** (core loop) | entitlement gate → Stripe Checkout → webhook → Customer Portal | ADR-006 / `stories/billing.md` |
| **OBS** | structured logging, OpenTelemetry, health/readiness, append-only audit log | ADR-008 / `stories/observability.md` |
| *(emergent)* | `EnterTenant` tenancy primitive (ADR-003 amend); billing → platform-controller refactor (ADR-004/006 amend) | — |

**Key consequence:** building the platform infra first deliberately satisfied the dependencies for most
of the backlog — GDPR needed audit (✅), notifications/webhooks needed the outbox (✅), admin-impersonation
needed audit + a tenant-context primitive (both ✅). So the remaining work is largely **unblocked** and
sequenced below by value, not by dependency.

---

## Wave 1 — Keystones (unlock the rest) — ✅ COMPLETE

| Item | Epic | Size | Why first | Deps |
|------|------|------|-----------|------|
| **RBAC** (admin role + permission seam + roster UI) — ✅ *COMPLETE* (RBAC-1/2/3; ADR-009, `stories/rbac.md`) | `RBAC` | M | B2B table stakes; unblocks Admin + Public API | ready |
| **File storage** (`IFileStorage`: local / S3-compatible) — ✅ *COMPLETE* (FILES-1/2/3; ADR-010, `stories/files.md`) | `FILES` | S–M | Avatars, attachments, and the GDPR export artifact all need it | ready |

## Wave 2 — Compliance & security (enterprise table stakes)

| Item | Epic | Size | Why | Deps |
|------|------|------|-----|------|
| **Account & data lifecycle (GDPR)** — export + erasure — ✅ *COMPLETE* (GDPR-1/2; ADR-011, `stories/gdpr.md`) | `GDPR` | M–L | Legal exposure; reuses dissolve (add `ExportAsync` beside `WipeAsync` on contributors) | audit ✅, File storage (W1) ✅ |
| **MFA / TOTP 2FA** — ✅ *COMPLETE* (MFA-1..4 — JSON + redirect + native step-up all shipped, enforced on every sign-in path) (ADR-012, `stories/mfa.md`) | `MFA` | M | Security baseline; closes the ADR-C15 "TOTP promised, never built" gap | ready |

## Wave 3 — Extensibility & ops (open the platform up)

| Item | Epic | Size | Why | Deps |
|------|------|------|-----|------|
| **Public API + API keys** — ✅ *COMPLETE* (PUBAPI-1/2, config-gated off; ADR-015, `stories/pubapi.md`) | `PUBAPI` | M | Programmatic access distinct from the user session | RBAC (W1) |
| **In-app notifications** — ✅ *COMPLETE* (NOTIFY-1/2; ADR-013, `stories/notify.md`) | `NOTIFY` | M | Follow-on to email; fan-out via the outbox | outbox ✅ |
| **Outbound webhooks** — ✅ *COMPLETE* (HOOKS-1/2 — subscriptions + delivery log/replay; config-gated off; ADR-016, `stories/hooks.md`) | `HOOKS` | M | Integration story for *your* customers | outbox ✅ |
| **Admin back-office + impersonation** — ✅ *COMPLETE* (ADMIN-1/2; ADR-014, `stories/admin.md`) | `ADMIN` | M–L | Support tooling — all deps ready (audit ✅ + `EnterTenant` ✅); highest blast radius, do deliberately | RBAC, audit ✅, EnterTenant ✅ |

## Finish-the-epic (optional — when a real paid plan exists)

- **BILLING-5** (seat/usage quotas) — ✅ **DONE** (`IQuotaService`; seats on the invite path → 402,
  metered usage via monthly `UsageCounter`; limits in `PlanCatalog`, null = unlimited). · **BILLING-6**
  (trial/dunning) — ✅ **DONE** (`IBillingNotifier` dunning on past_due/canceled transitions +
  `SubscriptionLapseSweepJob` one-time lapse nudge, via NOTIFY). · **BILLING-7** (dissolve cleanup) —
  ✅ **DONE** (`BillingDataContributor` wipes the projection + cancels the provider sub via the outbox on
  tenant dissolve). **BILLING epic complete (1–7).** Optional follow-up: advance trial-ending nudge.

## Test & hardening debt (small, parallel cleanup)

- **E2E** (Playwright) for the platform features built unit/integration-first (billing flows, health, …).
- **stripe-mock** integration test (deferred in BILLING-2 over Testcontainers 4.12 friction).
- **Build-time ban on `IgnoreQueryFilters` in `src/Api/Features/**`** (audit task B9-1) — ✅ **DONE**
  (`tests/Api.Tests/ArchitectureTests.cs`): a one-test guardrail making the escape hatch unreachable
  from slice code.
- *(optional)* Declarative auto-audit SaveChanges interceptor (deferred from OBS-4; explicit
  `IAuditLog.Record` covers semantic events today).

## Deferred — don't build until forced

- **Redis distributed cache** (`CACHE`) — only when you outgrow a single node (breaks the "Postgres-only
  run cost" on purpose-deferred terms).
- **Not planned** (integrations, not core): full-text/vector search, marketing email/CRM, product-analytics
  pipeline. i18n expansion is already shipped (EN/ES; FR/DE/PT scaffolded — `docs/LOCALIZATION.md`).

---

## Recommended next

**All 11 planned epics + their UI are COMPLETE.** Foundation (JOBS/BILLING/OBS) + Wave 1 (RBAC, FILES) +
Wave 2 (GDPR, MFA) + Wave 3 (NOTIFY, ADMIN, PUBAPI, HOOKS) — ADRs 006–016, all merged. PUBAPI + HOOKS
were un-parked and shipped on 2026-07-01 (config-gated **default-off**). The API-first surfaces (MFA
enroll/step-up, GDPR export/erasure, notification bell menu, admin console) all have their Blazor UI now.
**Open items:** HOOKS-3 (a Blazor webhook/API-key management UI) and API-key rotation. **Deferred:**
CACHE (Redis — until multi-node).

**`THEME` — per-user dark mode → ✅ COMPLETE (2026-07-10).** Light/Dark/System on Bootstrap 5.3's
`data-bs-theme`, following the two preference playbooks: device-local pre-paint bootstrap
(`theme.js` + `localStorage["app_theme"]`, the NATIVE-5 culture seam minus the Preferences half)
and server sync (`User.Theme`, `PUT /api/auth/theme`, `theme` JWT claim, cold-start layout
reconcile — the `locale` playbook). See `docs/stories/theme.md`.

**`PREFS` — per-user preference sync → ✅ COMPLETE (2026-07-14).** Fixed the QA-I18N-02 failure
(locale was never persisted server-side — the only switcher lived on the anonymous login page) and
the theme-only-after-reload instability: Settings → Preferences card (signed-in home for both
switchers), reconcile on every sign-in via the `AuthService.SignedIn` event, device-choice adoption
when the server value was never set, one-reload locale apply (WASM satellite assemblies), and
"system" stored verbatim so Auto propagates. ADR-022; see `docs/stories/prefs.md`.

**`BILLING-9` — seat re-check at invitation accept → ✅ COMPLETE (2026-07-14).** Closed the quota gap
where a downgrade (dunning lapse, cancel, ADR-021 comp revert) left pending invitations that could
each still join and grow the tenant past its new cap: `AcceptAsync` now refuses over-cap tenants
(402 `seat_limit_reached`, "household full" state on `/join`; accepts at exactly the cap stay
allowed and a refused token self-heals on re-upgrade). ADR-006 addendum; see `docs/stories/billing.md`.

**`RLS` — Postgres row-level-security tenancy backstop → ✅ COMPLETE (decided + built 2026-07-06,
ADR-020 + addendum).** DB-level second wall under the ADR-003 query filter: FORCEd fail-closed
policies on every `ITenantScoped` table, `RlsSessionInterceptor` GUC propagation, tags/EnterTenant
for the sanctioned cross-tenant paths, the integration harness running RLS-ENFORCED as a
non-privileged role, the migration-parity CI gate, and the two-role prod topology (+ posture
guard) documented in `DEPLOYMENT.md` §7. Staging is live-enforced with no config change; prod
activation (`STATUS.md` §5) enables the guard.

## Next up: DEPLOY (planned 2026-07-02)

The one untested dimension left: the app has only ever run on localhost + CI. Epic **`DEPLOY`**
(ADR-017, `stories/deploy.md`) takes it to a real **staging** environment on an all-free-tier stack —
**Render free** (one container, the API serving the WASM bundle **single-origin**, which kills the
cross-site refresh-cookie failure class outright) + **Neon** Postgres (session pooler) + **Brevo**
SMTP — with a repeatable prod recipe. Three slices: **DEPLOY-1** single-origin hosting + config-gated
forwarded headers (pure code, harness-tested); **DEPLOY-2** Dockerfile + compose parity + staging
bring-up + a `docs/DEPLOYMENT.md` runbook; **DEPLOY-3** the deploy pipeline (develop → staging auto
with a post-deploy smoke gate; main → prod behind environment approval) + a staging section in the QA
plan. Free-tier trade-offs are recorded decisions, not surprises (instance sleep pauses the outbox —
staging-acceptable, never prod; real SMTP means email QA cases stay manual on staging).
**DEPLOY is now ✅ COMPLETE** (all three slices; staging live + auto-deploy with a version-gated smoke).

## NATIVE — full MAUI parity (planned 2026-07-02) → ✅ COMPLETE (2026-07-14)

The MAUI Blazor-Hybrid shells reuse the shared RCL, so they already render every web screen and have
native auth wired — but native isn't built in CI and the full feature surface is
inherited-but-unverified. Epic **`NATIVE`** (ADR-018, `stories/native.md`) commits to **full
parity across Android/Windows/iOS/macOS**, in three platform waves: **guardrails** (CI build gate + a
`docs/NATIVE_PARITY.md` audit), **gap-fixes** (WebView deltas — downloads, external links, back button,
culture/theming), and **verification** (a per-feature native QA pass + automated emulator/simulator
smoke). **Distribution (signed AAB / MSIX / IPA / pkg + store submission) is downstream-app work per
ADR-024 (decided 2026-07-14)** — signing identity is per-app; the platform ships the
first-native-release checklist (`NEW_APP_GUIDE.md` Phase 9) instead of artifacts, and the epic
completes at NATIVE-6. Recorded platform cost: the **macOS CI runner** (the Apple Developer account +
signing material moved to the downstream list). Parity means "what web does" — OS push/biometrics are
beyond scope; web-first still holds (this keeps native *caught up*).

**Epic closed 2026-07-14:** the NATIVE-6 Android + Windows device pass came back green (the Apple
§13b smoke had passed 2026-07-06), completing the last platform slice. NATIVE-12 (OAuth
process-death resilience, PR #172) was added post-close as a QA-finding fix slice.

## Terminal state (reached 2026-07-14) — the platform roadmap is done

Every planned epic is complete and verified on web + all four native platforms; staging deploys
continuously from develop. Two scope decisions closed the tail: **native distribution** (signing,
installers, stores — ADR-024) and **production activation** (ADR-017 amendment: staging is the
platform's terminal environment) are **downstream-app work**, executed per app via
`NEW_APP_GUIDE.md` Phases 8–9. The **v3 delta audit** (2026-07-15 → 07-27, 62 tasks, PRs
#147–#191) then hardened the finished platform rather than extending it — FOUNDATION_RULES v2.0
is the resulting quality bar. What remains here is by-choice backlog (HOOKS-3 UI, API-key
rotation, CACHE, FR/DE/PT — see above), maintenance: toolchain drift, QA findings (§14a re-runs
+ QA-AND-15 are the open device items), and keeping docs/CI honest as downstream apps report back —
and the **post-terminal cost wave** below, which makes the finished platform cheaper to keep green.

---

## Post-terminal wave — cost & maintenance (planned 2026-09-08)

The platform is feature-complete, so post-terminal work is about **running it cheaper and keeping it
honest**, not extending it. First item: GitHub Actions minutes.

| Item | Epic | Size | Why | Deps |
|------|------|------|-----|------|
| **Local + self-hosted CI** — run the gates on the maintainer's own machines, with hosted runners as a toggle-back fallback (design: `PLATFORM_BACKLOG.md` §13; **pick-up-ready stories: `stories/localci.md`**, ADR-025 draft inside) | `LOCALCI` | M | macOS jobs bill at 10× and Windows at 2×; one develop push (Apple build + smoke) can cost more than everything else that month. A local gate runner also shortens the pre-push loop. | none — repo stays private |

**Slices** (each independently valuable — stop after any):

| Slice | What | Saves |
|-------|------|-------|
| **LOCALCI-1** — switchable runners | Every `runs-on` in `ci.yml` reads a per-OS repo variable with the hosted label as fallback; self-hosted runner agents on the Windows desktop (+ optional Linux via WSL/Docker) and the MacBook. Set the variable → the job runs at home for 0 minutes; delete it → snaps back to hosted. Deploy jobs stay hosted. | ~all of the 10×/2× minutes |
| **LOCALCI-2** — local gate runner | Land the parked `ci-local.ps1` (branch `ci/local-gates`: native mirror of build-test / qa-artifacts / secret-scan / license-scan, opt-in E2E / Docker / native-Windows) behind a drift tripwire so the mirror can't silently diverge from `ci.yml`; `WAYS_OF_WORKING.md` "run the gates locally first". | red pushes (each re-bills every job) |
| **LOCALCI-3** — trigger diet | `native-paths` already skips the Apple/smoke legs on docs-only pushes; extend the same paths gate to every non-deploy job and consider `workflow_dispatch` + weekly schedule for the Apple smoke. | the long tail |

**Order:** 1 → 2 → 3 (measured: ≈190 billed min per develop code push today, ≈157 of them macOS/Windows). LOCALCI-1 is the money; 2 and 3 are quality-of-life. Binding constraints: the
repo stays **private** while runners are attached; queued Apple jobs wait for the Mac to be online
(24 h expiry, one-click re-run); self-hosted runners are not clean machines, so `global.json` + the
lockfiles + the CLAUDE.md bump-together playbook remain the toolchain-drift authority.

---

## Pre-launch gates — GATES (built 2026-09-11) ✅

Post-terminal, and not a feature so much as an admission about *sequence*: a downstream app is ready
to show people well before it is ready to charge them or to meet strangers. Two deployment-config
switches, both shipped closed, both cleared on launch day (ADR-027, `docs/stories/gates.md`):

- **GATES-1 — `Billing:Enabled`, default off.** The billing controllers and the provider webhook are
  removed from the MVC application model at startup, so the routes 404 rather than refuse; every tenant
  resolves to Free through the catalog's existing fail-closed fallback, and the upgrade wording
  disappears from the seat-limit error and the full-household page in both languages. Knock-on: the
  production Stripe-key guard now fires only when billing is on, since a gated-off deployment has no
  reachable webhook for the fake provider to back. Free seats moved 3 → 5.
- **GATES-2 — a signup green list, empty means open.** It decides who may *found* a household; a valid
  pending invitation admits its addressee only when that invitation's tenant **owner** is green-listed.
  Enforced at the single account-creation choke point, on creation only — an existing account always
  signs in.

Built on the platform first; **the port to `vuelto` and `jigger-jot` is the open item**, the same
shape LOCALCI-3 took.

---

## Flavors wave — the platform as a spec with interchangeable stacks (planned 2026-09-08)

**The idea:** Vuelto proved the platform works as a clone-and-rebrand template — for a .NET shop. A JS,
Java, Go, or Python developer gets nothing from it today. The durable asset is not the C# but the
**contract** around it (constant decisions, the 76 foundation rules, data model, feature flows, the
Postman collection as API contract, the Gherkin stories, the QA plan, the audit suite). This wave
turns that contract into a stack-neutral **spec** with a **conformance kit**, and then ships other
stacks as **flavors** that pass the same kit. Design detail: `PLATFORM_BACKLOG.md` §14; **pick-up-ready
stories for every epic (SPEC slices 1–5 with the R1–R76 K/S/P/X classification, the slice ladder per
backend, the screen ladder per frontend, ADR-026 + S-001…S-003 drafts): `stories/flavors.md`.**

**Decisions taken 2026-09-08 (record as ADRs when `SPEC` is picked up):**
- **Spec-first, repo per piece.** A `spec` repo (docs + conformance kit + the conformance matrix
  workflow) is the source of truth; each flavor is its own template repo pinning a `SPEC_VERSION`;
  a `scaffold` CLI composes backend + frontend + DB pieces into one fresh app repo. Not a monorepo:
  "use this template" must yield exactly one stack, and every stack brings its own toolchain.
- **Frontends: React, Angular, Flutter** (plus the existing Blazor). Vue rejected (audience overlaps
  React, no structural edge). One shared Playwright suite via a **test-id contract** in the spec
  serves every DOM frontend; Flutter is the deliberate exception (own journey suite).
- **Backends: Node/NestJS, Go, Spring, FastAPI** (plus the existing ASP.NET Core), in that order of
  fit. Go ranks above Python: pgx + sqlc maps onto the outbox/RLS design with no framework in the way,
  and a static binary fits the free-tier hosting better than anything else.
- **Databases are tiered, not free-choice** — the tenancy defense lives here. **Tier A** (native
  row-level security + queue-safe claim + transactional DDL): Postgres family, SQL Server, Oracle
  on request, CockroachDB/Yugabyte by conformance proof. **Tier B** (no RLS, app-filter only,
  downgrade signed in the app brief): MySQL/MariaDB. **Isolation-shaped** (database per tenant is the
  wall): MongoDB, SQLite via Turso/D1, DynamoDB via IAM leading keys. Mongo Shared = Tier B, signed.
  Ask-for-Mongo playbook: offer Postgres JSONB first.
- **Native/desktop come in two tiers per frontend family** (table below): a **hybrid shell** (the web
  UI inside a native shell — MAUI Blazor Hybrid's model; shares the web code and the Playwright suite)
  and **true native** (own UI toolkit; own journey suite).

| Epic | Size | What | Deps |
|------|------|------|------|
| `SPEC` | L | Extract the stack-neutral spec from this repo; restate R1–R76 as **outcomes** (HTTP-observable → conformance kit; code-structural → per-flavor arch tests); test-id contract; conformance kit (Newman + Playwright + adversarial tenant tests) passing against `platform-dotnet`; conformance-matrix workflow; DB-tier ADR | none — first |
| `FRONT-REACT` | L | React + TS on Vite, TanStack Query/Router; hybrid shells Capacitor (mobile) + Tauri (desktop); retrofit Blazor pages to the test-id contract so ONE Playwright suite runs both | SPEC |
| `BACK-NODE` | L | NestJS on Fastify + Drizzle + Zod; dependency-cruiser arch tests; first full backend port; the JS flavor pairs with FRONT-REACT | SPEC |
| `BACK-GO` | L | chi + pgx + sqlc + goose; repository layer = first tenant wall (no ORM filter) + go-arch-lint ban on raw DB access; scratch image for Render free | SPEC |
| `BACK-SPRING` + `FRONT-ANGULAR` | L + L | Spring Boot (Java/Kotlin) + Hibernate filters + Flyway + ArchUnit; Angular ships **with** it (enterprise/Java shops expect the pair; closest paradigm to Blazor → most mechanical port) | SPEC; demand |
| `BACK-FASTAPI` | L | FastAPI + SQLAlchemy 2 + Alembic + Pydantic; import-linter | SPEC; demand |
| `NATIVE-RN` | M | Expo / React Native as the **true-native mobile** option for the React family (shares the TS API client + query hooks with FRONT-REACT); desktop stays Tauri | FRONT-REACT |
| `FRONT-FLUTTER` | L | One Dart codebase → iOS/Android/Windows/macOS/Linux/web. **Per-app, mobile-first only**: web-first (golden rule 5) is waived in that app's brief; own integration-test journey suite | SPEC; demand |
| `DB-SQLSERVER` | M | Second Tier-A DB for the .NET and Spring flavors: security-policy RLS + `SESSION_CONTEXT` interceptor (set per connection open — pooled-connection trap), READPAST/UPDLOCK claim, poll-only dispatcher, DDL dialect for the RLS parity gate | SPEC; demand |
| `SCAFFOLD` | M | `perezosoft new --backend <x> --frontend <y> --db <z>` composes pinned pieces + runs the rebrand | ≥ 2 backends and ≥ 2 frontends exist |

**Support matrix (only these rows get CI in the conformance matrix; the spec allows any Tier-A DB ×
backend × frontend):**

| # | Backend | Frontend | DB | Status |
|---|---|---|---|---|
| 1 | ASP.NET Core | Blazor WASM + MAUI hybrid | Postgres | ✅ exists (`platform-dotnet`, the reference) |
| 2 | ASP.NET Core | React + Capacitor/Tauri | Postgres | proves the frontend seam |
| 3 | NestJS | React | Postgres | the JS flavor |
| 4 | Go | React | Postgres | |
| 5 | Spring Boot | React, Angular | Postgres | Angular ships with Spring |
| 6 | FastAPI | React | Postgres | |
| 7 | ASP.NET Core, Spring | any | SQL Server | alternate Tier-A DB |
| 8 | any | Flutter | any | mobile-first apps only |

**Mobile + desktop options per frontend family:**

| Family | Web | Hybrid shell (shares web code + Playwright) | True native (own suite) | Desktop |
|---|---|---|---|---|
| Blazor | Blazor WASM | MAUI Blazor Hybrid ✅ | — | MAUI ✅ |
| React | Vite SPA | Capacitor | Expo / React Native (`NATIVE-RN`) | Tauri (Electron rejected: size) |
| Angular | SPA | Capacitor (Ionic's home turf) | — (NativeScript rejected: niche) | Tauri |
| Flutter | Flutter web (second-class) | n/a — native by construction | Flutter | Flutter |

**Order:** SPEC → FRONT-REACT → BACK-NODE → BACK-GO → (Spring + Angular) → FastAPI → RN/Flutter/SQL
Server/Scaffold as demand appears. **Stop rule:** a flavor is only "supported" while its matrix cell is
green at the current spec version; otherwise it is demoted to "community" in the spec README.

