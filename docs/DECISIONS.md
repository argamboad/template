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
