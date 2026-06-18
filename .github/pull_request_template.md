## Summary
<!-- what this PR does, in 1–3 sentences -->

## Related
- Story: <STORY-ID>
- ADRs added/affected: <ADR numbers or "none">

## Type
- [ ] feat  [ ] fix  [ ] docs  [ ] refactor  [ ] test  [ ] chore  [ ] perf

## Acceptance criteria
- [ ] All Gherkin scenarios for the story pass
- [ ] Edge / unhappy-path scenarios covered

## Checklist
- [ ] Tests written first (TDD) — no production code without a failing test
- [ ] Unit tests green (`Core.Tests`, `Api.Tests`)
- [ ] E2E tests green (`E2E.Tests`) — happy + key unhappy paths covered
- [ ] Vertical slice — app is in a working state
- [ ] Tenant-scoping enforced (no cross-tenant leakage)
- [ ] Core derived-rule logic unit-tested (if touched)
- [ ] UI components added to Shared.Ui (not inline in Web)
- [ ] No direct UI→DB access (goes through the API)
- [ ] Latest stable deps; no preview packages
- [ ] Docs updated (FEATURES / DATA_MODEL / DECISIONS) if behavior or decisions changed

## Notes
<!-- anything reviewers/future-you should know -->
