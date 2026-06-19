# Ways of Working

> The process layer: how work is sliced, how user stories and PRs are written, and naming
> conventions. This is constant across projects from this template; project-specific examples are
> marked. Referenced by `CLAUDE.md` so Claude Code follows it.

## Slices (the unit of build work)

A **slice** is a thin, vertical, end-to-end increment that delivers one coherent piece of user
value and leaves the app working. Vertical means it cuts through all layers as needed (API →
Core/derived rules → Infrastructure/EF → Shared.Ui components → Web), rather than building one
horizontal layer in isolation.

**Principles**
- **Vertical, not horizontal.** Prefer "user can toggle inventory availability" (touches DB, API,
  UI) over "build all the database tables."
- **Small enough to finish.** A slice should be completable and mergeable on its own — roughly a
  few focused sessions, not weeks. If it can't be described in one or two sentences, split it.
- **Working state after each slice.** Every merged slice keeps the app runnable. No half-wired
  features on the main branch.
- **Foundational slices first.** Some early slices are enabling (auth + tenant scaffolding, the
  data model + first migration). These are still vertical where possible.
- **One epic = a group of related slices.** User stories are written per-epic at the start of that
  epic (not all upfront).

**Slice lifecycle**
1. Pick the next slice (from `ROADMAP.md` once it exists).
2. Write/refine the user story/stories for it (see template below) under `docs/stories/`.
3. **Write the tests first (TDD).** Unit tests for Core logic; E2E tests for the user-facing flow.
4. Branch, implement until all tests are green; refactor.
5. Open a PR using the PR template; self-review against acceptance criteria.
6. Merge; app remains in a working state. Add ADRs to `DECISIONS.md` for any decisions made.

## User stories

Stories live in `docs/stories/`, **one file per epic** (e.g. `docs/stories/inventory.md`),
multiple stories per file. They translate `FEATURES.md` behavior into intent + testable
acceptance criteria. **Acceptance criteria use Gherkin (Given/When/Then).**

### Story template

```markdown
### <STORY-ID> — <short title>

**As a** <role / tenant member>
**I want** <capability>
**So that** <benefit>

**Context / notes:** <optional — links to FEATURES.md flow, DATA_MODEL rule, constraints>

**Acceptance criteria**

Scenario: <name of the scenario>
  Given <initial context>
  And <additional context>
  When <action>
  Then <expected outcome>
  And <additional outcome>

Scenario: <another scenario — include edge cases and the unhappy path>
  Given ...
  When ...
  Then ...

**Out of scope:** <what this story explicitly does NOT cover>
**Definition of done:** tests written first (TDD); all unit + E2E scenarios green; Core logic
unit-tested; E2E covers happy + key unhappy paths; tenant-scoping verified; merged, app working.
```

### Story ID & naming
- **Story ID format:** `<EPIC>-<n>` where `<EPIC>` is a short uppercase epic key and `<n>` is a
  sequential number. Example epic keys: `AUTH`, `TENANT`, plus project-specific ones.
  e.g. `AUTH-1`, `TENANT-3`.
- **Story file naming:** `docs/stories/<epic-lower>.md` (e.g. `docs/stories/auth.md`).
- Keep epic keys registered at the top of each story file so they don't drift.

## Branches, commits, PRs

### Conventional Commits
All commits and PR titles follow **Conventional Commits**:

```
<type>(<scope>): <description>
```

- **Types:** `feat`, `fix`, `docs`, `refactor`, `test`, `chore`, `perf`, `build`, `ci`.
- **Scope (optional but encouraged):** the area/epic, e.g. `feat(inventory): ...`,
  `fix(auth): ...`.
- **Description:** imperative, lowercase, no trailing period. e.g.
  `feat(inventory): add availability toggle endpoint`.
- Breaking changes: append `!` (`feat(api)!: ...`) and explain in the body.

### Branch naming
```
<type>/<epic-or-scope>-<short-desc>
```
e.g. `feat/inventory-availability-toggle`, `fix/auth-token-refresh`. Optionally include the story
ID: `feat/INV-3-availability-toggle`.

### PR naming
PR title = a Conventional Commit line, ideally referencing the story:
`feat(inventory): availability toggle (INV-3)`.

### PR template
Stored at `.github/pull_request_template.md` (auto-loaded by GitHub). Contents:

```markdown
## Summary
<what this PR does, in 1–3 sentences>

## Related
- Story: <STORY-ID> (link)
- ADRs added/affected: <ADR numbers or "none">

## Type
- [ ] feat  [ ] fix  [ ] docs  [ ] refactor  [ ] test  [ ] chore  [ ] perf

## Acceptance criteria
- [ ] All Gherkin scenarios for the story pass
- [ ] Edge / unhappy-path scenarios covered

## Checklist
- [ ] Tests written first (TDD) — no production code without a failing test
- [ ] Unit tests green (Core.Tests, Api.Tests)
- [ ] E2E tests green (E2E.Tests) — happy + key unhappy paths covered
- [ ] Vertical slice — app is in a working state
- [ ] Tenant-scoping enforced (no cross-tenant leakage)
- [ ] Core derived-rule logic unit-tested (if touched)
- [ ] UI components added to Shared.Ui (not inline in Web)
- [ ] No direct UI→DB access (goes through the API)
- [ ] Latest stable deps; no preview packages
- [ ] Docs updated (FEATURES / DATA_MODEL / DECISIONS) if behavior or decisions changed

## Notes
<anything reviewers/future-you should know>
```

## Testing strategy (TDD — constant)

**Test-Driven Development is the default on every slice.** Write the failing test first; only
then write the production code that makes it pass; then refactor. No production code is written
without a test that drove it.

### Red-Green-Refactor
1. **Red** — write a failing test derived from the Gherkin scenario.
2. **Green** — write the minimum production code to pass it.
3. **Refactor** — clean up without breaking the tests.

### Test layers

| Layer | Project | Framework | What it covers |
|-------|---------|-----------|----------------|
| Unit | `tests/Core.Tests` | xUnit | Domain logic, derived rules, entity invariants |
| Unit | `tests/Api.Tests` | xUnit | API endpoints, request/response shape, auth guards |
| E2E | `tests/E2E.Tests` | Playwright (NUnit) | Critical user flows through a real browser |

### Unit tests (`Core.Tests`, `Api.Tests` — xUnit)
- One test class per production class; file mirrors the source tree.
- Cover every derived rule, happy path, unhappy path, and tenant-scoping boundary.
- No real database in unit tests — use in-memory EF or mocks at the repository boundary.

### E2E tests (`E2E.Tests` — Playwright/NUnit)
- One test file per epic, mirroring `docs/stories/`.
- Tests inherit from Playwright's `PageTest`; use Page Object Model (`tests/E2E.Tests/Pages/`).
- Cover the Gherkin happy path + key unhappy paths through the real running UI.
- Run against the full stack: `docker compose up -d`, then start API and Web before running.
- Base URL configured via `PLAYWRIGHT_BASE_URL` env var or `playwright.runsettings`.

### First-time Playwright setup
```sh
dotnet build tests/E2E.Tests
pwsh tests/E2E.Tests/bin/Debug/net10.0/playwright.ps1 install
```

### Running tests
```sh
dotnet test tests/Core.Tests
dotnet test tests/Api.Tests
# E2E — requires docker compose + servers running
dotnet test tests/E2E.Tests -- RunSettings=tests/E2E.Tests/playwright.runsettings
```

## How Claude Code should use this
- Default to vertical slices; refuse to build sprawling multi-epic chunks in one go — propose a
  split instead.
- **Write tests first.** For every slice: unit tests before Core/API code; E2E tests before UI
  code. Gherkin scenarios map directly to test cases.
- Write the per-epic story file before starting an epic; use the story + Gherkin as the spec.
- Name branches, commits, and PRs per the conventions above.
- Fill the PR template; check every box honestly or note why N/A.
