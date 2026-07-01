# Stories — Admin back-office + impersonation (`ADMIN`)

> One file per epic. A **platform-staff** surface (outside the tenant model) to inspect any tenant and
> "sign in as" a user for support — the **highest-blast-radius** feature, built entirely on existing
> guardrails (the audited `QueryAllTenants()` escape hatch, ADR-003; the audit log, ADR-008). Design +
> constraints in **ADR-014**. Stories use Gherkin acceptance criteria. **Status: 🔲 in progress.**

**Epic key:** `ADMIN`

**Prerequisites (external, before any code):**
- **Platform-staff allowlist** in config (`Admin:StaffEmails`, via `.env`/env vars) — out-of-band, not
  settable from the app.
- Reuses: audit (ADR-008 ✅), the audited escape hatch (ADR-003 ✅), JWT issuance, RBAC (ADR-009 ✅).
  No new packages.

**Guardrails (ADR-014):** the global tenant filter is **never loosened** (cross-tenant reads use the
audited hatch, scoped to the target); staff is **config-only** (never a tenant role / app toggle);
impersonation is **short-lived + non-refreshable + audited**; admin is **read-only** over tenant data.

---

### ADMIN-1 — Platform-staff gate + cross-tenant inspection

**Status: ✅ Implemented** (`feat/admin-1-staff-inspection`). `PlatformAdminSettings` (`Admin:StaffEmails`,
`.env`-documented) + `IPlatformStaffService.IsStaffAsync` (resolves email, checks the allowlist
case-insensitively, fails closed) + `AdminApiControllerBase.RequireStaffAsync` (401/403 gate).
`AdminController` (`GET /api/admin/tenants` list via `ITenantRepository.ListAllAsync` — non-scoped
tables, no hatch; `GET /api/admin/tenants/{id}` detail **enters the target tenant** via
`ITenantContext.EnterTenant` so scoped reads go through the normal filter, and records
`admin.tenant.viewed` **in that tenant**). Read-only; the global filter is never loosened. Tests
`tests/Api.Tests/Admin/` (`PlatformStaffServiceTests` allowlist; `AdminControllerTests` non-staff→403,
list-with-counts, detail+in-tenant-audit, unknown→404).

**As a** platform staff member
**I want** to list and inspect any tenant
**So that** I can support and debug across the whole platform

**Context / notes:** `PlatformAdminSettings` (`StaffEmails`) + `IPlatformStaffService.IsStaffAsync(userId)`
(resolves the caller's email, checks the allowlist, case-insensitive) + an **`AdminOnly`** gate that 403s
non-staff. `AdminController` (staff-gated): `GET /api/admin/tenants` (list — id, name, member count,
created) and `GET /api/admin/tenants/{id}` (detail — members + roles, subscription status, counts), using
the audited `QueryAllTenants()` hatch (ADR-003) for tenant-scoped data. **Every access audited**
(`admin.tenant.viewed`). Read-only.

**Acceptance criteria**

```gherkin
Scenario: Staff can list tenants
  Given I am on the platform-staff allowlist
  When I GET /api/admin/tenants
  Then I see every tenant (id, name, member count) — across the global filter, via the audited hatch

Scenario: Non-staff are refused
  Given I am a normal user (not on the allowlist)
  When I call any /api/admin endpoint
  Then I get 403 Forbidden

Scenario: Staff membership is config-only
  Given the allowlist is set in config
  Then it cannot be changed through any app endpoint (no self-serve staff grant)

Scenario: Admin reads are audited
  When staff inspect a tenant
  Then an AuditEvent records the staff actor and the tenant viewed

Scenario: The global filter is never loosened
  Then admin cross-tenant reads use QueryAllTenants() (audited hatch), not a disabled filter
```

**Out of scope:** cross-tenant **writes** (admin is read-only; changes go through impersonation, ADMIN-2);
a metrics/analytics dashboard; a staff-management UI (allowlist is config).
**Definition of done:** tests first; staff-gate allow/deny (403), list + detail via the hatch, config-only
staff, audit on access; merged, app working; ADR-014 referenced.

---

### ADMIN-2 — Impersonation ("sign in as")

**Status: 🔲 Planned.**

**As a** platform staff member
**I want** a short-lived "sign in as" for a user
**So that** I can reproduce and fix an issue from their point of view

**Context / notes:** `POST /api/admin/impersonate/{userId}` (staff-gated) mints a **short-lived** access
token for the target (their claims + an **`impersonated_by`** claim = the staff user id) with **no refresh
token** — so it expires on its own and can't be extended. The token scopes via the target's `tenant_id`
claim (no filter bypass). The action is **loudly audited** (`admin.impersonation.started`) **in the
target's tenant**, so that tenant sees a platform admin accessed the account. Returns
`{ access_token, expires_in }`.

**Acceptance criteria**

```gherkin
Scenario: Staff impersonates a user
  Given I am staff
  When I POST /api/admin/impersonate/{userId} for an existing user
  Then I get a short-lived access token carrying that user's identity and an impersonated_by claim
  And no refresh token is issued (it can't be extended)

Scenario: Impersonation is audited in the target's tenant
  When I start impersonation
  Then an AuditEvent records me as actor, the target user, in the target's tenant

Scenario: Non-staff cannot impersonate
  Given I am a normal user
  When I try to impersonate anyone
  Then I get 403 Forbidden

Scenario: Unknown target
  When I impersonate a non-existent user
  Then I get 404 and no token is issued
```

**Out of scope:** a "stop impersonating / return to admin" flow (the short-lived token just expires);
restricting which users can be impersonated (e.g. not other staff) — a policy a real deployment may add;
a UI banner (client concern).
**Definition of done:** tests first; staff-only (403), token carries target identity + `impersonated_by`
+ short expiry + no refresh, unknown target 404, audit in the target's tenant; merged, app working;
ADR-014 referenced.

---

## Slice plan (implementation map)

Ordered, each a mergeable vertical slice. TDD throughout.

1. ✅ **Staff gate + inspection (ADMIN-1).** — DONE. `PlatformAdminSettings` (config allowlist) +
   `IPlatformStaffService` + `AdminApiControllerBase.RequireStaffAsync` (403); `AdminController`
   list (non-scoped tables) + detail (via `EnterTenant`, audited in-tenant). Read-only; global filter
   never loosened. (Chose `EnterTenant` over `QueryAllTenants` — keeps the filter engaged, scoped.)
2. 🔲 **Impersonation (ADMIN-2).** `POST /api/admin/impersonate/{userId}` → short-lived,
   non-refreshable, `impersonated_by`-tagged access token; audited in the target's tenant.

**Known sharp edges (from ADR-014):** the global filter is **inviolable** (audited hatch only); staff is
**config-only** (never a role/app toggle); impersonation is **short-lived + non-refreshable + audited**;
admin is **read-only** over tenant data; **no secrets/PII** in responses or audit metadata.
