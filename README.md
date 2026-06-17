# Multi-Tenant SaaS Project Template

A reusable starting point for new multi-tenant SaaS apps on a fixed stack
(ASP.NET Core API + Blazor WASM + shared RCL + PostgreSQL/EF Core + ASP.NET Core Identity, with
MAUI Blazor Hybrid for mobile + Win/macOS desktop deferred). It lets a new project skip
re-deciding the stack and jump straight to discussing **what the app does**.

## What's in here

```
_TEMPLATE_PRIMER.md     ← paste into a new Claude chat to start conceptualizing a project
CLAUDE.md               ← Claude Code operating manual skeleton (constant rules pre-filled)
docs/                   ← doc skeletons (constant parts filled, app-specific = TODO)
  PROJECT_BRIEF.md
  FEATURES.md
  DATA_MODEL.md
  TECH_STACK.md         ← almost entirely reusable; only re-verify versions
  DECISIONS.md          ← pre-seeded with constant ADRs (C1–C11); add app ADRs from 001
  stories/
src/ , tests/           ← ready-to-scaffold solution layout (Claude Code fills these)
```

## How to use it

**Step 1 — Conceptualize (in Claude chat).**
Start a new chat and paste `_TEMPLATE_PRIMER.md`. Describe your app. Claude runs the
conceptualization session — clarifying questions, recommendations, ADRs, scope discipline — and
fills in the doc skeletons. The stack is already decided, so the conversation is about the app.

**Step 2 — Create the repo (when the thinking layer is done).**
Once concept + features + data model + decisions are settled, clone this `saas-template/` tree as
your new repo (it already has `CLAUDE.md` at root, `docs/`, and the `src/`+`tests/` layout).

**Step 3 — Build (in Claude Code).**
Point Claude Code at the repo. It re-verifies current stable versions, scaffolds the projects into
`src/`, builds the EF Core models + first migration from `DATA_MODEL.md`, and proceeds slice by
slice — writing per-epic user stories into `docs/stories/` as it goes.

## What's constant vs. per-project

- **Constant (don't re-decide):** the stack, the clean-API-boundary + RCL architecture,
  multi-tenancy (Tenant ≠ User, tenant-scoped data, per-user preferences), the doc/ADR method,
  the "latest stable, never previews" version policy, and MAUI-deferred for non-web clients.
- **Per-project (designed fresh):** the concept, features, data model entities, domain-specific
  derived rules, the tenant's real-world label, scope, seed data, and hosting.

## Important caution

**Do not copy app-specific data-model decisions between projects.** Things like single-table
inheritance, snapshot-vs-reference semantics, or any particular derived rule are designed for one
app's domain and can quietly mislead another. Only the items listed as "constant" carry forward.
Re-verify tool/library versions at the start of every project — this template ages.
