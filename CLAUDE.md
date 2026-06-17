# CLAUDE.md

> Operating manual for Claude Code on this project. Auto-loaded each session — keep it tight.
> Constant rules are pre-filled; fill the app-specific placeholders during conceptualization.

## What this project is
<!-- One-paragraph summary: what the app is and its core loop. -->
_TODO_ — full context in `docs/PROJECT_BRIEF.md`.

## Read before you act
- Touching the schema or entities → read **`docs/DATA_MODEL.md`** first.
- Implementing a screen or flow → read **`docs/FEATURES.md`** first.
- Starting a build slice → read **`docs/WAYS_OF_WORKING.md`** (slices, story format, PR/commit
  conventions).
- Wondering *why* something is the way it is → check **`docs/DECISIONS.md`** before changing it.
- Changing a settled decision → add a new dated ADR in `docs/DECISIONS.md`; don't silently
  reverse it.

## Golden rules — constant (do not violate)
1. **Tenant-scoped, not user-scoped.** App data belongs to the tenant; enforce `tenant_id`
   filtering on every query; never leak across tenants. Only preferences are per-user.
2. **Clean API boundary.** The UI is a client of the API and never accesses the DB directly.
3. **Blazor UI components live in the shared RCL**, not inline in the web app — keeps non-web
   clients cheap.
4. **Derived values are computed, never stored** as stale flags (confirm the app's specific
   derived rules in `docs/DATA_MODEL.md`).
5. **Don't build non-web clients now.** Mobile + desktop (MAUI Blazor Hybrid) are deferred; web
   first.
6. **Latest stable versions only, never previews.**
7. **Work in vertical slices.** Each slice is end-to-end and leaves the app working; follow
   `docs/WAYS_OF_WORKING.md` for slices, Gherkin stories, Conventional Commits, and the PR
   template. Don't build sprawling multi-epic chunks — propose a split.

## Golden rules — app-specific
<!-- Add the domain rules that must never be violated (e.g. the cocktail app's "makeable is
     always derived"). These come out of DATA_MODEL.md's derived rules. -->
_TODO_

## Tech stack (see `docs/TECH_STACK.md`)
- **Versions:** latest stable on the current .NET line — **re-verified for this project as: _TODO_.**
- **Backend:** ASP.NET Core Web API behind a clean API boundary.
- **Web frontend:** Blazor WebAssembly; UI components in a shared **RCL** (hard rule).
- **DB:** PostgreSQL via **EF Core (Npgsql)**; schema/migrations generated from `docs/DATA_MODEL.md`.
- **Auth:** ASP.NET Core Identity; tenant scoping layered on top.
- **Non-web clients:** MAUI Blazor Hybrid (mobile + Win/macOS desktop) — *deferred, don't build now.*

## Scope discipline
Before building anything, check the **"OUT" list in `docs/PROJECT_BRIEF.md`**. Don't implement
deferred items without an explicit decision.

## Conventions
- Code term for the tenant is **tenant** (app-facing label: _TODO_).
- _TODO: app-specific conventions (naming, lookup tables, etc.)_

## Status / not yet decided
- Seed data (if any) — _TODO_.
- Concrete schema (EF Core migrations) — generated from `docs/DATA_MODEL.md`.
- **User stories: generated per-epic at build time**, under `docs/stories/` (one file per epic).
- Deferred sub-decisions: non-web framework commitment, hosting, Identity details (see
  `docs/TECH_STACK.md`).

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
| `docs/stories/` | User stories per epic — generated at build time |
| `.github/pull_request_template.md` | PR checklist (auto-loaded by GitHub) |
| `SCHEMA.sql` / migrations | Concrete schema — generated from DATA_MODEL.md |
