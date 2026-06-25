# Stories — Observability & Audit Log

> One file per epic. Two complementary concerns shipped together: **operational observability**
> (structured logging, OpenTelemetry traces/metrics, health endpoints) and a tenant-scoped,
> append-only **audit log**. **Status: DEFERRED** — design decision and constraints in **ADR-008**.
> Stories use Gherkin acceptance criteria.
>
> **Audit ≠ logs.** Audit is durable, queryable, exportable **tenant data** (compliance). Logs/traces
> are operational telemetry (sampled, ephemeral). They do not substitute for each other.

**Epic key:** `OBS`

**Prerequisites (external, before any code):**
- None to build locally (console exporter is the dev default). Production needs an **OTLP-compatible
  collector/backend** (e.g. an OpenTelemetry Collector, or a vendor endpoint) — config-driven, off
  by default.
- Packages (latest stable, .NET 10, no previews — ADR-C10): `OpenTelemetry.Extensions.Hosting`,
  the ASP.NET Core / EF Core / HttpClient instrumentation packages, `OpenTelemetry.Exporter.OpenTelemetryProtocol`,
  and `Microsoft.Extensions.Diagnostics.HealthChecks` (+ the Npgsql health-check).

---

### OBS-1 — Structured logging with tenant/user enrichment

**As an** operator
**I want** JSON structured logs enriched with tenant and user context
**So that** I can filter and correlate activity per tenant when debugging

**Context / notes:** every log scope carries `tenant_id` (from the JWT `tenant_id` claim, via
[`HttpCurrentTenant`](../../src/Api/Services/HttpCurrentTenant.cs)) and `user_id`. Enrichment comes
from middleware that opens a logging scope per request. **No secrets/PII** beyond identifiers in log
state (never tokens, never card data).

**Acceptance criteria**

```gherkin
Scenario: Authenticated request logs carry tenant and user ids
  Given an authenticated request with a tenant_id claim
  When the request is logged
  Then the log entries include tenant_id and user_id in structured fields

Scenario: Anonymous request logs omit tenant/user cleanly
  Given an unauthenticated request
  When it is logged
  Then no tenant_id/user_id is emitted (no nulls leaking, no error)

Scenario: Secrets never appear in logs
  Given a request carrying a bearer token / OTP / webhook secret
  When it is logged
  Then those values are absent from log output
```

**Out of scope:** traces/metrics (OBS-2); audit (OBS-4).
**Definition of done:** tests first; enrichment present-when-authed / absent-when-anonymous tested;
a secret-redaction test; merged, app working; ADR-008 referenced.

---

### OBS-2 — OpenTelemetry traces & metrics

**As an** operator
**I want** distributed traces and runtime metrics exported via OTLP
**So that** I can see latency, errors, and throughput per route and per tenant

**Context / notes:** wire ASP.NET Core + EF Core + HttpClient instrumentation; tag the request span
with `tenant_id`/`user_id` so traces are filterable per tenant. Exporter is **config-driven**:
console in dev, OTLP when `OpenTelemetry:Otlp:Endpoint` is set (same config-presence pattern as the
OAuth providers / Stripe key). Off by default = no external dependency to run the template.

**Acceptance criteria**

```gherkin
Scenario: Requests produce spans tagged with tenant
  Given OpenTelemetry tracing is enabled
  When an authenticated request is served
  Then a span is recorded for the request with tenant_id/user_id attributes

Scenario: Exporter is opt-in by config
  Given no OTLP endpoint is configured
  When the app runs
  Then telemetry goes to the console exporter and the app starts with no external dependency
  And setting an OTLP endpoint switches export to OTLP without code changes
```

**Out of scope:** dashboards/alerting (backend concern); log shipping (OBS-1).
**Definition of done:** tests first where feasible (config switch, span attributes via an in-memory
exporter); merged, app working.

---

### OBS-3 — Health & readiness endpoints

**As an** operator / orchestrator
**I want** liveness and readiness probes
**So that** deployments and load balancers know when the app is up and able to serve

**Context / notes:** `/health` (liveness — process up) and `/health/ready` (readiness — DB
reachable, via the Npgsql health check). **Unauthenticated**, status-only — must not leak
internals (no connection strings, versions, or component names beyond healthy/unhealthy).

**Acceptance criteria**

```gherkin
Scenario: Liveness returns healthy when the process is up
  When I GET /health
  Then I receive 200 with a minimal healthy status

Scenario: Readiness reflects the database
  Given the database is reachable
  When I GET /health/ready
  Then I receive 200
  And when the database is unreachable I receive 503

Scenario: Health endpoints leak no internals
  When I GET /health or /health/ready
  Then the body contains only status, no connection details or stack info
```

**Out of scope:** per-dependency dashboards; auth on probes.
**Definition of done:** tests first; healthy/unhealthy/redaction integration-tested; merged, app
working.

---

### OBS-4 — Tenant-scoped audit log

**As a** tenant owner / compliance reviewer
**I want** a durable trail of security-relevant actions in my tenant
**So that** I can answer "who did what, when" for membership and billing changes

**Context / notes:** an append-only `AuditEvent : ITenantScoped` (`actor_user_id`, `action`,
`entity_type`, `entity_id`, `metadata` jsonb, `created_at`). Captured two ways: an EF `SaveChanges`
interceptor (sibling of
[`TenantStampingInterceptor`](../../src/Infrastructure/Persistence/TenantStampingInterceptor.cs)) for
declarative entity changes, and an explicit `IAuditLog.Record(...)` for semantic events (member
invited/removed, role changed, subscription changed, tenant dissolved). **Append-only** — no update
or delete from application code. Auto-filtered per tenant by the global query filter (ADR-003), so a
tenant only ever reads its own trail.

**Acceptance criteria**

```gherkin
Scenario: A member role change is audited
  Given I am an owner and I change a member's role
  When the change commits
  Then an AuditEvent is recorded with my user id as actor, the action, and the target

Scenario: Audit is tenant-scoped
  Given audit events exist for two tenants
  When I read the audit log
  Then I see only my own tenant's events (global query filter), never another tenant's

Scenario: Audit is append-only
  Given an existing AuditEvent
  When application code attempts to modify or delete it
  Then the operation is rejected / not supported

Scenario: No secrets in audit metadata
  Given an audited action involving a token or card
  When the event is written
  Then the metadata contains identifiers only, never the secret/PAN
```

**Out of scope:** an audit-viewer UI (a later slice); export/retention for GDPR (see
`docs/PLATFORM_BACKLOG.md` — Account & Data Lifecycle); SIEM streaming.
**Definition of done:** tests first; recording (interceptor + explicit), tenant-scoping isolation,
append-only enforcement, and secret-exclusion unit/integration-tested on the Postgres Testcontainer;
merged, app working.

---

## Slice plan (implementation map — when undeferred)

Ordered, each a mergeable vertical slice. TDD throughout.

1. **Structured logging (OBS-1).** Request-scope enrichment middleware reading
   `HttpCurrentTenant`/claims; JSON console formatter (built-in `ILogger` or Serilog —
   decide at build, record in the PR). Secret-redaction test.
2. **OpenTelemetry (OBS-2).** `AddOpenTelemetry().WithTracing(...).WithMetrics(...)`; instrument
   ASP.NET Core + EF + HttpClient; span enrichment for tenant/user; config-gated OTLP exporter
   (console default). Validate span attributes with an in-memory exporter.
3. **Health checks (OBS-3).** `AddHealthChecks().AddNpgSql(...)`; map `/health` + `/health/ready`;
   redaction-safe responses.
4. **Audit log (OBS-4).** `Core/Entities/AuditEvent.cs : ITenantScoped` + EF config + migration;
   `AuditInterceptor` (sibling of `TenantStampingInterceptor`, wired in `AppDbContext.OnConfiguring`
   so tests enforce it too); `IAuditLog.Record(...)`; append-only guard; an
   `AuditDataContributor : ITenantDataContributor` for dissolve.

**Known sharp edges (from ADR-008):** keep audit (durable tenant data) and logs (telemetry)
separate; health endpoints must not leak internals; never put secrets/PII in spans or audit
metadata; **dissolve vs retention** — wiping a tenant deletes its audit trail, which conflicts with
any legal-hold/retention requirement. If retention is needed, the dissolve contributor must
export-then-wipe — deferred to the GDPR/Account-Lifecycle backlog item.
