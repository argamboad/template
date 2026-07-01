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
| **MFA / TOTP 2FA** — ✅ *COMPLETE* (MFA-1/2; redirect-path step-up = UI follow-up) (ADR-012, `stories/mfa.md`) | `MFA` | M | Security baseline; closes the ADR-C15 "TOTP promised, never built" gap | ready |

## Wave 3 — Extensibility & ops (open the platform up)

| Item | Epic | Size | Why | Deps |
|------|------|------|-----|------|
| **Public API + API keys** — ✅ *PUBAPI-1 DONE* (config-gated off; ADR-015, `stories/pubapi.md`) | `PUBAPI` | M | Programmatic access distinct from the user session | RBAC (W1) |
| **In-app notifications** — ✅ *COMPLETE* (NOTIFY-1/2; ADR-013, `stories/notify.md`) | `NOTIFY` | M | Follow-on to email; fan-out via the outbox | outbox ✅ |
| **Outbound webhooks** (customer-facing) | `HOOKS` | M | Integration story for *your* customers | outbox ✅ |
| **Admin back-office + impersonation** — ✅ *COMPLETE* (ADMIN-1/2; ADR-014, `stories/admin.md`) | `ADMIN` | M–L | Support tooling — all deps ready (audit ✅ + `EnterTenant` ✅); highest blast radius, do deliberately | RBAC, audit ✅, EnterTenant ✅ |

## Finish-the-epic (optional — when a real paid plan exists)

- **BILLING-5** (seat/usage quotas) — ✅ **DONE** (`IQuotaService`; seats on the invite path → 402,
  metered usage via monthly `UsageCounter`; limits in `PlanCatalog`, null = unlimited). · **BILLING-6**
  (trial/dunning) — ✅ **DONE** (`IBillingNotifier` dunning on past_due/canceled transitions +
  `SubscriptionLapseSweepJob` one-time lapse nudge, via NOTIFY). **BILLING epic complete (1–6).** Optional
  follow-ups: advance trial-ending nudge; a billing-dissolve `ITenantDataContributor`.

## Test & hardening debt (small, parallel cleanup)

- **E2E** (Playwright) for the platform features built unit/integration-first (billing flows, health, …).
- **stripe-mock** integration test (deferred in BILLING-2 over Testcontainers 4.12 friction).
- **Build-time ban on `IgnoreQueryFilters` in `src/Api/Features/**`** (audit task B9-1) — a one-test
  guardrail making the escape hatch unreachable from slice code.
- *(optional)* Declarative auto-audit SaveChanges interceptor (deferred from OBS-4; explicit
  `IAuditLog.Record` covers semantic events today).

## Deferred — don't build until forced

- **Redis distributed cache** (`CACHE`) — only when you outgrow a single node (breaks the "Postgres-only
  run cost" on purpose-deferred terms).
- **Not planned** (integrations, not core): full-text/vector search, marketing email/CRM, product-analytics
  pipeline. i18n expansion is already shipped (EN/ES; FR/DE/PT scaffolded — `docs/LOCALIZATION.md`).

---

## Recommended next

**The planned platform is COMPLETE.** Foundation (JOBS/BILLING/OBS) + Wave 1 (RBAC, FILES) + Wave 2
(GDPR, MFA) + Wave 3 (NOTIFY, ADMIN) — nine epics, ADRs 006–014, all merged. **Parked by choice:** PUBAPI
+ HOOKS (public/customer-facing API). **Deferred:** CACHE (until multi-node). **Remaining work is
UI-only** — the API-first surfaces (MFA enroll/step-up, GDPR export/erasure, notification bell menu, admin
console) need Blazor pages when wanted.
