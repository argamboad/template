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
- Rebranding (name, logo, colours, tagline) → follow **`docs/REBRANDING.md`** and complete every
  item. It explicitly covers the **transactional email templates** (`src/Infrastructure/Email/` —
  `BrandedEmail.cs` + `Assets/logo.png`), which are inline and easy to miss; a rebrand that skips
  them is incomplete.

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
- **Versions:** latest stable on the current .NET line — **re-verified 2026-06-17: .NET SDK 10.0.301, ASP.NET Core / EF Core / Identity 10.0.9, Npgsql.EF 10.0.2, PostgreSQL 17.**
- **Backend:** ASP.NET Core Web API behind a clean API boundary.
- **Web frontend:** Blazor WebAssembly; UI components in a shared **RCL** (hard rule).
- **DB:** PostgreSQL via **EF Core (Npgsql)**; schema/migrations generated from `docs/DATA_MODEL.md`.
- **Auth:** ASP.NET Core Identity; tenant scoping layered on top.
- **Non-web clients:** MAUI Blazor Hybrid (mobile + Win/macOS desktop) — *deferred, don't build now.*

## Auth rules (constant)
- **OAuth credentials are never in appsettings.** Use `dotnet user-secrets` in dev; environment
  variables in production. See `docs/TECH_STACK.md` for the commands.
- **New OAuth provider = one line.** Add `.AddXxx()` in `ServiceCollectionExtensions`. Don't
  restructure anything else.
- **Magic links use `MagicLinkTokenProvider`.** Purpose constant: `MagicLinkTokenProvider.Purpose`.
  Token lifetime is in config (`Auth:MagicLink:TokenLifespanMinutes`).
- **`IEmailSender` (Core abstraction) is the only way to send email.** Never reference MailKit
  directly outside `Infrastructure/Email/`.
- **JWT Bearer auth is not pre-configured.** Add it in the auth story slice — scheme choice is
  app-specific.

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
| `docs/REBRANDING.md` | Every brand touchpoint to replace per app — **incl. the email templates** |
| `docs/MOBILE_TESTING.md` | Run/sign-in on the Android emulator (adb reverse, OAuth) |
| `docs/stories/` | User stories per epic — generated at build time |
| `.github/pull_request_template.md` | PR checklist (auto-loaded by GitHub) |
| `SCHEMA.sql` / migrations | Concrete schema — generated from DATA_MODEL.md |
