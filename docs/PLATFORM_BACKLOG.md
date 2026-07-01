# Platform Backlog — future foundation slices

> Capabilities a SaaS foundation commonly needs that are **not yet built** and **not yet decided**
> into an ADR. This is the parking lot: enough design per item to pick it up later without
> re-discovering the shape. When one is taken on, write its ADR in `DECISIONS.md`, its stories in
> `docs/stories/<epic>.md` (Gherkin), and remove/strike it here.
>
> **For the priority *ordering* of these items (waves + sizes + which deps are already satisfied), see
> `docs/ROADMAP.md`.** This file holds the per-item design detail behind that sequence.
>
> The **three prioritized moves already have ADRs + story files** and are *not* in this list:
> - Billing & subscriptions + entitlements + quotas → **ADR-006**, `docs/stories/billing.md`
> - Outbox / inbox / scheduled jobs → **ADR-007**, `docs/stories/async-jobs.md`
> - Observability + audit log → **ADR-008**, `docs/stories/observability.md`
>
> Everything below depends on nothing here being done first unless noted. Build web-first (ADR-C9),
> vertical slices, TDD (CLAUDE.md golden rules).

## Priority order (suggested)

| # | Item | Epic key | Why it's ranked here | Hard deps |
|---|------|----------|----------------------|-----------|
| 1 | ~~Account & data lifecycle (GDPR)~~ → **✅ DONE** (ADR-011, `stories/gdpr.md`) | `GDPR` | Legal exposure the moment you have EU users; reuses tenant scoping | Audit (ADR-008) for export completeness |
| 2 | ~~RBAC beyond owner/member~~ → **being built** (ADR-009, `stories/rbac.md`) | `RBAC` | Most B2B asks for an admin tier almost immediately | none |
| 3 | ~~File / blob storage~~ → **✅ DONE** (ADR-010, `stories/files.md`) | `FILES` | Avatars/attachments/exports all block on it | none |
| 4 | ~~MFA / TOTP 2FA~~ → **✅ DONE** (ADR-012, `stories/mfa.md`) | `MFA` | Security baseline; ADR-C15 promised TOTP that was never built | none |
| 5 | ~~In-app notifications~~ → **✅ DONE** (ADR-013, `stories/notify.md`) | `NOTIFY` | Natural follow-on to transactional email | Outbox (ADR-007) ideal |
| 6 | Outbound webhooks (customer-facing) | `HOOKS` | Integration story for *your* customers | Outbox (ADR-007) required |
| 7 | Public API + API keys | `PUBAPI` | Programmatic access distinct from the user session | RBAC helps |
| 8 | Admin back-office + impersonation | `ADMIN` | Support/debugging at scale | Audit (ADR-008) required |
| 9 | Distributed cache (Redis) | `CACHE` | Only once you scale past one node | none (defer hard) |

---

## 1. Account & data lifecycle (GDPR) — `GDPR` → **✅ DONE (ADR-011)**
> **Shipped** — tenant data export (owner-only → signed URL) + account erasure (delete-my-account,
> single-owner-safe, audited). Design in **ADR-011**, slices in `docs/stories/gdpr.md` (GDPR-1/2, merged).
> Sketch below retained for historical context.

**What:** self-serve **data export** ("download my data") and **erasure** (right to be forgotten) at
both user and tenant granularity; a documented data-retention posture.
**Why:** legal requirement with real penalties; also a credible trust/sales feature.
**Sketch / hooks:** the template already has the machinery — `ITenantDataContributor`
(`HasDataAsync`/`WipeAsync`) is exactly the per-feature enumeration needed. Add an `ExportAsync`
sibling to the contributor so each feature contributes to a tenant export the same way it contributes
to wipe. Erasure of a **user** (vs a whole tenant) needs an owner-reassignment rule (can't erase the
sole owner without dissolving/transferring). **Tension with audit (ADR-008):** erasure vs
legal-hold — export-then-wipe; decide retention windows here.
**Deps:** audit log (ADR-008) so export is complete; the dissolve flow as the wipe backbone.

## 2. RBAC beyond owner/member — `RBAC` → **TAKEN ON (ADR-009)**
> **Now an active epic** — design decided in **ADR-009**, stories + slice plan in
> `docs/stories/rbac.md`. The sketch below is retained for context; the ADR supersedes it.

**What:** at least owner / **admin** / member, plus a permission-check seam finer than the current
two-role `TenantMembership.Role`.
**Why:** B2B tenants delegate administration; two roles run out fast.
**Resolved (ADR-009):** ordered roles `owner > admin > member`; a `Permission` enum + a static
`RolePermissions` matrix in **Core** as the single source of truth; enforcement via a
`RequirePermission(...)` controller helper **and** a `.RequirePermission(...)` minimal-API filter
(sibling of `RequireEntitlement`, → 403). "Exactly one owner" preserved; role read live from
membership (no JWT claim). Pairs with `ADMIN` and `PUBAPI`.

## 3. File / blob storage — `FILES` → **✅ DONE (ADR-010)**
> **Shipped** — `IFileStorage` (local disk + S3-compatible), tenant-scoped keys, signed download URLs.
> Design in **ADR-010**, slices in `docs/stories/files.md` (FILES-1/2/3, all merged). Sketch retained
> for historical context.

**What:** an `IFileStorage` Core abstraction (put/get/delete/signed-url) with a local-disk dev impl
and an S3-compatible prod impl.
**Why:** avatars, attachments, and the GDPR export artifact all need somewhere to live.
**Resolved (ADR-010):** `IFileStorage` (streaming) mirrors the `IEmailSender` shape; **tenant-scoped
keys** (`{tenantId}/…`) validated server-side (traversal/cross-tenant rejected); **config-gated** impls
(local-disk default, S3-compatible when configured — Stripe-vs-Fake switch); **signed time-limited
download URLs** (native presigned for cloud; `ITimeLimitedDataProtector` token + `GET /api/files/{token}`
for local). Slices FILES-1 (abstraction+local) → FILES-2 (signed download) → FILES-3 (S3).

## 4. MFA / TOTP 2FA — `MFA` → **✅ DONE (ADR-012)**
> **Shipped** — authenticator TOTP enrollment/management + login step-up (JSON paths). Design in
> **ADR-012**, slices in `docs/stories/mfa.md` (MFA-1/2, merged). Follow-up: redirect-path step-up
> (needs the MFA client page). Sketch below retained for historical context.

**What:** authenticator-app TOTP as a second factor (and recovery codes).
**Why:** security baseline for any serious SaaS. **Note:** ADR-C15 originally claimed TOTP via
`AddDefaultTokenProviders()` — that was **superseded by ADR-002 and never implemented**, so this is a
genuine gap, not a re-do.
**Sketch / hooks:** TOTP secret per user (encrypted at rest via the existing Data Protection setup),
enrollment + verify endpoints on the custom auth stack, a step-up check at login in
[`AuthController`](../src/Api/Controllers/AuthController.cs). Recovery codes are single-use hashes
(reuse the `LoginToken` hashing pattern).
**Deps:** none.

## 5. In-app notifications — `NOTIFY` → **✅ DONE (ADR-013)**
> **Shipped** — per-user notification center + delivery preferences, fan-out (in-app + email) through the
> outbox. Design in **ADR-013**, slices in `docs/stories/notify.md` (NOTIFY-1/2, merged). A bell-menu UI
> is an API-first follow-up. Sketch below retained for historical context.

**What:** a per-user notification center + read/unread + per-user delivery preferences (in-app vs
email).
**Why:** the usual next ask after transactional email; preferences are legitimately per-user (the one
sanctioned per-user data carve-out, ADR-C2).
**Sketch / hooks:** `Notification` entity (per-user, **not** tenant-shared — preference-like);
fan-out via the **outbox** (ADR-007) so a domain event can produce both an email and an in-app
notification through one reliable path.
**Deps:** outbox (ADR-007) strongly preferred.

## 6. Outbound webhooks (customer-facing) — `HOOKS`
**What:** let *your* tenants subscribe to events from their data (endpoint registration, signed
deliveries, retries, a delivery log).
**Why:** the integration/extensibility story for customers.
**Sketch / hooks:** this is the **outbox** pointed outward — reuse the dispatcher, add HMAC signing,
per-subscription retry/backoff, and a deliveries table. Tenant-scoped subscriptions.
**Deps:** outbox (ADR-007) — required, don't build a second delivery mechanism.

## 7. Public API + API keys — `PUBAPI`
**What:** programmatic access authenticated by tenant-scoped **API keys**, distinct from the
JWT/cookie user session.
**Why:** scripts, integrations, and CI need non-interactive auth.
**Sketch / hooks:** `ApiKey : ITenantScoped` (store only a hash — reuse the `token_hash` pattern from
`RefreshToken`/`TenantInvitation`), an auth handler that resolves the key to a tenant + scopes, and
an OpenAPI/Swagger surface for the public routes. Scope keys to the same entitlement/quota checks as
the UI (BILLING-1/5). Rate-limit per key (extend [`RateLimiting`](../src/Api/Configuration/RateLimiting.cs)).
**Deps:** RBAC/scopes help; quotas (BILLING-5) for per-key limits.

## 8. Admin back-office + impersonation — `ADMIN`
**What:** a super-admin surface (cross-tenant, **platform-staff only**) to inspect tenants and
"sign in as" a user for support.
**Why:** support and debugging at scale.
**Sketch / hooks:** a platform-staff role **outside** the tenant model; cross-tenant reads go through
the audited `QueryAllTenants()` escape hatch (ADR-003) — never loosen the global filter. Impersonation
enters the target tenant via **`ITenantContext.EnterTenant`** (ADR-003 amendment 2026-06-25) so the
session is properly scoped rather than bypassing the filter; mint a scoped, **short-lived, audited**
token and **loudly audit-log** it (ADR-008) — this is the highest-blast-radius feature in the platform;
treat it accordingly.
**Deps:** audit log (ADR-008) — required; RBAC.

## 9. Distributed cache (Redis) — `CACHE`
**What:** `IDistributedCache` backed by Redis for hot reads / cross-node shared state.
**Why:** only once you run more than one API node.
**Sketch / hooks:** introduce behind `IDistributedCache` so call sites don't care; keep keys
tenant-prefixed. **Defer hard** — it breaks the "Postgres-only run cost" property (ADR-C13), so don't
add it until horizontal scale actually forces it. The outbox dispatcher's `SKIP LOCKED` design
(ADR-007) deliberately avoids needing it for multi-node correctness.

---

## Not planned (explicitly out unless a need appears)
- **Full-text / vector search** — Postgres FTS covers a lot before reaching for a search engine.
- **Marketing email / CRM** — distinct from transactional `IEmailSender`; an integration, not core.
- **Analytics / product telemetry pipeline** — OBS-2 covers ops telemetry; product analytics is a
  separate (often third-party) concern.
- **i18n expansion** — already shipped (EN/ES; FR/DE/PT scaffolded), see `docs/LOCALIZATION.md`.
