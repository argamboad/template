# Stories — Stack flavors: the platform as a spec with interchangeable pieces (`FLAVORS`)

> One file per epic *program*: this file holds the pick-up-ready stories for `SPEC` (first, the
> enabler) and the flavor epics that follow it (`FRONT-REACT`, `BACK-NODE`, `BACK-GO`, `BACK-SPRING` +
> `FRONT-ANGULAR`, `BACK-FASTAPI`, `NATIVE-RN`, `FRONT-FLUTTER`, `DB-SQLSERVER`, `SCAFFOLD`). Sequencing
> and the support matrix live in `docs/ROADMAP.md` (flavors wave); the design sketch in
> `docs/PLATFORM_BACKLOG.md` §14. Stories use Gherkin acceptance criteria.
> **Status: 📋 PLANNED (2026-09-08)** — nothing started. **Decision records:** ADR-026 (this repo,
> draft below) + the spec repo's own ADR log (S-001…S-003, drafts below).

**Epic keys:** `SPEC`, `FRONT-REACT`, `BACK-NODE`, `BACK-GO`, `BACK-SPRING`, `FRONT-ANGULAR`,
`BACK-FASTAPI`, `NATIVE-RN`, `FRONT-FLUTTER`, `DB-SQLSERVER`, `SCAFFOLD`

**Why:** Vuelto proved the platform as a clone-and-rebrand template — for a .NET shop only. The
durable asset is the **contract**, not the C#: the constant decisions, the 76 foundation rules, the
data model, the feature flows, the Postman collection (the machine-enforced API contract, ADR-023),
the Gherkin stories, the 150-case QA plan, and the audit suite. This program turns that contract into
a stack-neutral **spec** with a **conformance kit**, then ships other stacks as **flavors** that pass
the same kit — and lets a downstream app compose pieces (".NET back, React front, SQL Server").

**The decided set (2026-09-08 — don't re-debate; rationale in `PLATFORM_BACKLOG.md` §14):**

| Layer | Flavors | Rejected |
|---|---|---|
| Web frontends | Blazor (exists) · **React** · **Angular** (ships with Spring) · **Flutter** (per-app, mobile-first only) | Vue, Svelte, Next.js |
| Mobile | hybrid shells: MAUI Blazor Hybrid (exists), **Capacitor** (React/Angular) · true native: **Expo/React Native** (React family), Flutter | NativeScript |
| Desktop | MAUI (exists) · **Tauri** (React/Angular) · Flutter | Electron, RN-Windows/macOS |
| Backends | ASP.NET Core (exists) · **Node/NestJS** · **Go** · **Spring** (Java/Kotlin) · **FastAPI** | Django, Rust |
| Databases | **Tier A** Postgres family, SQL Server (Oracle/Db2 on request; CockroachDB/Yugabyte by proof) · **Tier B** MySQL family (signed downgrade) · **Isolation-shaped** Mongo/SQLite-per-tenant/DynamoDB | free choice |

**What `SPEC` extracts — the inventory as of `89a22ec` (2026-09-08), so the first session doesn't
have to re-count:**

| Asset | Count / location | Becomes |
|---|---|---|
| Top-level docs | 19 in `docs/*.md` + `_PLATFORM_PRIMER.md` + `CLAUDE.md` | split: stack-neutral → `spec/`, stack-bound → stays per flavor |
| Constant decisions | ADR-C1…C15 in `DECISIONS.md` | C1, C2, C10, C11, C12, C14 (method), C15 (auth strategy) → spec constants; C3–C9, C13 → per-flavor *bindings* (C8 already superseded by ADR-002) |
| Foundation rules | R1–R76 (R14, R33 retired) across `audits/v2…/FOUNDATION_RULES.md` + `audits/v3…/FOUNDATION_RULES_v2.md` | restated as **outcomes**, classified K/S/P/X (table in SPEC-2) |
| API contract | Postman collection: **79 requests in 11 folders** (`docs/postman/`), Swagger `v1` + `public` docs | the kit's Newman run; OpenAPI exported per flavor |
| Endpoints | 58 controller actions + 3 minimal-API routes (Notes sample) | endpoint catalogue with roles/gates/error codes (from the request descriptions) |
| Entities | 20 in `src/Core/Entities/` (`DATA_MODEL.md`) | stack-neutral data model + the tenant-axis list |
| Screens | 8 pages + 7 components in `src/Shared.Ui/` (`Login`, `AuthCallback`, `AuthError`, `Home`, `Household`, `Join`, `Settings`, `Billing`, `AdminConsole`; `AppHeader`, `NotificationBell`, `NotificationPrefsCard`, `MfaCard`, `LanguageSwitcher`, `ThemeSwitcher`) | screen catalogue + test-id contract |
| JS contracts | `theme.js` (pre-paint theme), `bfcache-guard.js`, `mfa-qr.js` | behavioural requirements every frontend must meet |
| Strings | 252 keys, EN + ES (`AppStrings.resx`, `.es.resx`) | `spec/strings/<culture>.json` — one converter script |
| Test ids | **95 `data-testid` values** already in `Shared.Ui`; the E2E suite already selects by test id | frozen into `spec/testids.json` (SPEC-4) |
| Journeys | 13 Playwright classes, 35 `[Test]` journeys (`tests/E2E.Tests/`) | the kit's journey suite |
| Adversarial cases | QA-ADV-01…24 (`QA_TEST_PLAN.md` §14a); most are curl-shaped | automated as HTTP kit tests (SPEC-3) |
| Manual QA | 150 cases, §1–§17 | stack-neutral cases → spec; §12/§13 native → per hybrid/native piece |

**Rejected alternatives (recorded so they aren't re-explored):** a monorepo of flavors ("use this
template" must yield one stack; per-stack toolchains collide; this repo's history/audit trail stays);
rewriting the journey suite in TypeScript before there is a second frontend (ship the kit as a Docker
image instead — see SPEC-3); free-choice databases (the tenancy defense lives in the DB).

---

## ADR drafts — paste at pickup

**ADR-026 (this repo) — The platform becomes the reference implementation of a stack-neutral spec;
other stacks ship as conformance-passing flavors; pieces are composed, not forked. (2026-09-08; planned)**
*Context.* Terminal state reached (ADR-024/017 amendment); Vuelto validated clone-and-rebrand for .NET.
Other-stack developers get nothing; a downstream app cannot mix pieces. *Decision.* (1) A separate
`spec` repository holds the stack-neutral contract + conformance kit + conformance matrix and is
semver-tagged; this repository is `platform-dotnet`, the **reference implementation**, and gates the
spec (nothing merges to `spec` until this repo passes it). (2) Each flavor is its own template repo
pinning a `SPEC_VERSION`; a `scaffold` CLI composes pieces. (3) The frontends/backends/databases are
the decided set above; databases are tiered (S-001). (4) A flavor is *supported* only while its matrix
cell is green at the current spec version. *Consequences.* Rule wording moves from mechanism to
outcome (S-003); the test-id contract becomes a hard requirement for every DOM frontend; the Blazor
pages are retrofitted to it; maintenance cost scales with N flavors — the matrix is what keeps it
affordable.

**S-001 (spec repo) — Databases are tiered by the strength of the second tenant wall.** Tier A: native
row-level security + queue-safe claim + transactional DDL (Postgres family; SQL Server; Oracle/Db2 on
request; Postgres-wire engines by conformance proof). Tier B: no native RLS — application filter only;
allowed only with a signed downgrade in the app brief (MySQL/MariaDB/TiDB). Isolation-shaped: the
wall is physical or credential-scoped (database per tenant — Mongo, SQLite/Turso/D1; DynamoDB IAM
leading keys); Mongo Isolated = Tier A, Mongo Shared = Tier B. Ask-for-Mongo playbook: Postgres JSONB
first. Every tier is proven by the same adversarial tenant tests; the kit's **RLS probe** and **claim
probe** decide Tier A membership.

**S-002 (spec repo) — Frontend and native tiers; web-first waiver.** DOM frontends (Blazor, React,
Angular) share ONE journey suite through the test-id contract. Native comes as *hybrid shell*
(web UI in a native shell — MAUI Blazor Hybrid, Capacitor, Tauri; shares the suite) or *true native*
(Expo/RN, Flutter; own journey suite mirroring the same Gherkin). Flutter is per-app, mobile-first
only: golden rule 5 (web-first) is waived in that app's brief, never in the spec.

**S-003 (spec repo) — Rules are stated as outcomes; conformance is layered.** Every rule is one of:
**K** (kit — HTTP/UI-observable, run identically against every flavor), **S** (structural — each
flavor proves it with its stack's arch-test tool), **P** (process — CONTRIBUTING, reviewed), **X**
(retired as stack-bound; re-expressed per flavor where an analogue exists). A flavor's README carries
the S-rule → test mapping; the matrix runs K.

---

## Epic `SPEC` — extract the contract, build the kit, wire the matrix

Repo: `github.com/argamboad/spec` (private until the first external flavor; rename the GitHub org later
if wanted — GitHub redirects). Layout:

```
spec/
  README.md                 # what conformance means; the support policy; the version protocol
  SPEC_VERSION protocol.md  # how flavors pin + claim versions
  decisions/                # S-001… (ADR log of the spec itself) + the neutral constants (C1,C2,C10-12,C14,C15)
  contract/
    project-brief.md  features.md  data-model.md  flows.md  ways-of-working.md  rebranding.md
    localization.md   qa-test-plan.md (stack-neutral cases only)  audit-suite.md
    rules.md                # R1–R76 restated as outcomes, each tagged K/S/P/X (SPEC-2)
    endpoints.md            # generated from the Postman collection (SPEC-1)
    screens.md              # screen catalogue + behaviours (theme pre-paint, bfcache guard, MFA QR)
    testids.json            # the test-id contract (SPEC-4)
    strings/en.json es.json # converted from resx (SPEC-1)
  kit/
    postman/                # the collection + environments (moved here; platform-dotnet consumes it back)
    journeys/               # the Playwright suite (C#/NUnit, unchanged) + a runner
    adversarial/            # QA-ADV-01…24 as HTTP tests (SPEC-3)
    probes/                 # DB-tier probes: rls-probe, claim-probe (SPEC-3)
    Dockerfile              # spec-kit:<version> — .NET SDK + Newman + Playwright browsers; `docker run spec-kit --base-url …`
  matrix/
    flavors.json            # supported combos: {backend, frontend, db, repo, compose, spec_version}
    .github/workflows/matrix.yml
  tools/
    resx2json.py  postman2endpoints.py  testids-lint.py
```

### SPEC-1 — Bootstrap the spec repo and extract the stack-neutral contract

**As a** platform maintainer **I want** the contract to live in one stack-neutral repo **So that**
every flavor implements the same thing and this repo becomes its reference, not its owner.

**Steps**
1. `gh repo create argamboad/spec --private`; copy `WAYS_OF_WORKING.md` conventions (branch per slice,
   Conventional Commits, PR template); add `SPEC_VERSION` = `0.1.0`.
2. **Move, then rewrite** into `contract/`: `PROJECT_BRIEF.md` (structure only — the OUT list is
   per app), `FEATURES.md` (constant flows §"Constant flows" — 121 lines — verbatim; strip Blazor
   words), `DATA_MODEL.md` (entities, relationships, derived rules; mark the tenant axis: every entity
   with `TenantId` and whether it is `ITenantScoped` — from `src/Core/Entities/`), `FLOWS.md`
   (sequence diagrams: auth, tenancy, outbox, billing webhook, dissolve — keep; replace class names with
   role names), `LOCALIZATION.md` (the culture model, not resx), `REBRANDING.md` (touchpoint list,
   stack-neutral half), `QA_TEST_PLAN.md` (§4–§11, §14, §14a, §14b, §15 — stack-neutral; §12/§13
   native cases go to the hybrid/native pieces), `audits/AUDIT_SUITE.md` (the 5-phase method).
   Stack-bound docs **stay here**: `TECH_STACK.md`, `ARCHITECTURE.md`, `DEPLOYMENT.md`,
   `MOBILE_TESTING.md`, `NATIVE_PARITY.md`, `STATUS.md`, `NEW_APP_GUIDE.md` (becomes the .NET flavor's
   onboarding; the spec gets a neutral `NEW_APP_GUIDE` skeleton).
3. **Split the constants**: `decisions/constants.md` = C1, C2, C10, C11, C12, C14 (TDD + "unit +
   E2E" — tools per flavor), C15 (auth strategy: OAuth Google/Microsoft extensible + magic link + OTP +
   custom JWT/refresh per ADR-002, MFA per ADR-012). C3, C4, C5, C6, C7, C9, C13 become **bindings**
   in `platform-dotnet/docs/BINDINGS.md` ("this flavor binds C3 to ASP.NET Core, C6 to Postgres…") —
   every flavor ships a `BINDINGS.md` with the same table.
4. `tools/postman2endpoints.py`: walk the collection → `contract/endpoints.md` table (folder, verb,
   path, auth/role, config gate, error codes — the request descriptions already state these per
   ADR-023). Assert the count is 79 the first time (drift canary).
5. `tools/resx2json.py`: `AppStrings.resx` + `.es.resx` → `strings/en.json`, `es.json` (252 keys;
   preserve `{0}` placeholders as ICU `{0}`); a test that both files have identical key sets.
6. `contract/screens.md`: one section per screen/component with its behaviours (from FEATURES +
   the js contracts: theme pre-paint before first stylesheet, "system" follows OS live; bfcache
   `pageshow persisted` → reload; MFA QR rendered client-side from the otpauth URI; notification bell
   unread count + clear-read never deletes unread; impersonation banner; locale mismatch → persist +
   one reload). No Blazor words.
7. `platform-dotnet` PR: `docs/postman/` becomes a **git subtree/submodule of `spec/kit/postman`**
   (decide: subtree — no submodule friction for downstream clones); `PostmanParityTests` (R74) reads
   the same path; `postman-sync` workflow path updated. CLAUDE.md doc map updated; the doc-map test
   (R75) still passes because every remaining `docs/*.md` is listed.

**Acceptance criteria**
```gherkin
Scenario: The contract is stack-neutral
  Given contract/**.md in the spec repo
  When grepped for "Blazor|EF Core|Npgsql|xUnit|NUnit|MAUI|Razor|C#|\.NET"
  Then zero matches outside decisions/bindings-guidance.md (which explains what a binding is)

Scenario: The API contract did not change by moving
  Given the Postman collection moved to spec/kit/postman
  When platform-dotnet's PostmanParityTests and the postman-sync workflow run
  Then both are green and the workspace mirror receives the same 79 requests

Scenario: Strings round-trip
  Given strings/en.json and es.json generated from the resx files
  When resx2json runs again on develop
  Then the output is byte-identical (idempotent) and both files have the same 252 keys
```
**Definition of done:** spec repo exists with `contract/` complete, `SPEC_VERSION 0.1.0` tagged;
`platform-dotnet` consumes the collection from the subtree; every constant decision is either a spec
constant or a binding; CI green on both repos.

### SPEC-2 — Restate R1–R76 as outcomes and classify them

**As a** flavor author **I want** each rule to say *what must be true*, tagged with *who proves it*
**So that** I can implement it in my stack and know whether the kit or my arch tests are the judge.

**Method:** for each rule, write `Outcome:` (stack-neutral sentence), `Proof:` (K = kit test id,
S = "arch test in flavor; reference: `<C# test name>`", P = CONTRIBUTING item, X = analogue guidance).
Reference test names come from `tests/Api.Tests/` (`ArchitectureTests`, `Architecture/*Guard*`,
`EnforcementGateTests`, `TenantInvariantTests`, `Rls/`, `Outbox/`, …). **The classification, decided
now so the session executes instead of debating:**

| Tag | Rules | Notes |
|---|---|---|
| **K** (kit) | R1 (bad-signature webhook → 4xx, no state change), R3 (SSRF: loopback/link-local target → 400), R12 (post-erasure export is empty), R16, R18 (error-body schema: shared shape, never exception text), R17 (all-invalid scope set → 400, absent → default), R21/R53 (gated surfaces 404 when unconfigured), R28 (second factor single-use; accepted timestep rejected), R29/R56 (webhook older → ignored, strictly-older only), R30 (N concurrent at cap → exactly limit succeed), R32 (cross-tenant update/delete by id → 404), R37 (**RLS probe**: as the app DB role with no tenant set, every tenant table returns 0 rows; with tenant A set, only A), R41 (rapid A/B alternation on pooled connections never leaks), R43 (dissolve leaves zero rows on every tenant table), R45 (staff action → audit event visible via admin inspect, attributed to staff — QA-ADV-05), R46 (security notification lands on both channels despite prefs — QA-ADV-09), R47/R49 (wrong-code cap holds across IPs, atomically under parallel attempts — QA-ADV-10), R48 (parallel redemptions → exactly one session — QA-ADV-16), R50, R54 (JWT for tenant A, owner of B → owner-only action on A → 403), R55 (empty selector ⇒ none), R59 (same status replayed → one notification), R62 (client-serving host: `/` no-store, `/_framework|/assets` immutable), R64 (startup refuses mismatched Stripe mode — probe via `/health/ready` under env), R71, R72, R73 (journeys), R74 (the collection IS the kit) | ≈ 33 rules; each becomes `kit/adversarial/<rule>.spec` or a journey |
| **S** (structural, per flavor) | R2, R4, R5, R6, R7, R8, R9, R11, R13, R15 (no ambient clock), R19, R20 (config keys documented), R22, R25 (lockfile + locked install), R26 (license gate), R27, R34, R35, R36, R38, R39, R40, R42 (new tenant entity ships its policy in the same migration — Tier A DBs), R44, R51 (hashed credential material at rest), R52, R57, R58, R60, R61 (one toolchain pin source), R63, R65, R67, R68 (host parity per hybrid shell), R70 (component tests exist), R75, R76 | each flavor's README maps rule → test; tool per stack: NetArchTest · dependency-cruiser + eslint-boundaries · ArchUnit · import-linter · go-arch-lint |
| **P** (process) | R10 (new-slice checklist), R23, R24 (ADR amendment in-PR), R31 (TDD: failing test first; happy + denied + cross-tenant per slice), R66 (single-maintainer deps reviewed), R69 | `spec/CONTRIBUTING.md` + each flavor's PR template |
| **X** (stack-bound, re-express) | R41's *interceptor cache* mechanics (Npgsql) → per-DB guidance; R60 (`native-paths` regex) → "CI names every file class that affects a native binary"; R68 → "every hybrid shell loads the identical behaviour-contract script set"; R70 → "a component-test chassis exists for the UI library" | keep the outcome, drop the mechanism |

**Acceptance criteria**
```gherkin
Scenario: Every rule is classified and every K rule has a kit test id
  Given contract/rules.md
  When tools/rules-lint.py runs
  Then all 74 live rules carry exactly one of K/S/P/X, every K names a kit test file that exists,
       every S names a reference test in platform-dotnet that exists (checked by path)

Scenario: The reference passes its own S rules
  Given platform-dotnet's tests
  When Api.Tests run
  Then every S-rule reference test is present and green (no rule points at a deleted test)
```
**Definition of done:** `contract/rules.md` complete; lint green; `platform-dotnet/docs/RULES_MAP.md`
lists S-rule → test (the first `BINDINGS`-style map every flavor must ship).

### SPEC-3 — The conformance kit (Newman + journeys + adversarial + probes) as one Docker image

**As a** flavor CI **I want** `docker run spec-kit:<v> --base-url <api> --web-url <web> --mailpit
<url> --db <conn>` **So that** conformance is one command with no toolchain of its own.

**Decisions:** keep the journey suite in C#/NUnit (35 journeys, unchanged — rewriting it in TS buys
nothing until a second frontend exists; the image hides the SDK); Newman runs the collection with the
`local` environment (it already chains OTP via Mailpit, token rotation, admin, PUBAPI/HOOKS);
adversarial tests are **TypeScript on Vitest + undici** (small, no framework, readable by every
flavor author) — they are the new code; probes are SQL scripts run through the flavor's DB connection.

**Steps**
1. `kit/Dockerfile`: `mcr.microsoft.com/dotnet/sdk:10.0.400` base (pinned like `global.json`) +
   Node LTS + Newman + Playwright Chromium (`playwright.ps1 install --with-deps chromium`). Entrypoint
   `kit.sh` with the four flags; exit code = worst of the four stages; JUnit XML to `/out`.
2. **Newman stage:** `newman run kit/postman/Perezosoft.postman_collection.json -e local --env-var
   baseUrl=…`; the chaining scripts already assert status codes + capture secrets. Add a `kit`
   environment file (Mailpit URL, staff email `e2e-staff@example.com`, the same env the `e2e` CI job
   sets: `Auth__RateLimit__PasswordlessPermitLimit=1000`, `Admin__StaffEmails__0`).
3. **Journeys stage:** `dotnet test kit/journeys` with `PLAYWRIGHT_BASE_URL`, `MAILPIT_BASE_URL`,
   `E2E_API_BASE_URL` (exactly today's `tests/E2E.Tests` — moved by `git subtree split`, history kept;
   `platform-dotnet` consumes it back the same way as the collection). Selectors must be test-id
   only (SPEC-4 lint).
4. **Adversarial stage** — one file per case, mapped to the K rules; ported from `QA_TEST_PLAN.md`
   §14a's curl steps (they are already precise): QA-ADV-01 cross-tenant read/export/erase → 404s;
   -02 same on `api_keys`/`webhook_subscriptions`/`webhook_deliveries` with PUBAPI+HOOKS on; -03 export
   contains every tenant table (from `data-model.md`'s tenant axis) and no secret material; -04 dissolve
   deletes keys/secrets/counters/deliveries (probe the DB after); -05/-06/-07/-08 impersonation
   attribution / staff-action wall / role ceiling / prefs untouched; -09 security notification beats
   prefs; -10 MFA lockout across IPs; -11 recovery code single-use; -12 webhook replay/out-of-order/
   same-second; -13 first-ever bad-state webhook no false-notify; -14 comp/revert canceled tenant;
   -15 concurrent last-seat acceptance → one 200 one 402; -16 double redemption → one session; -17
   rotated-refresh replay revokes all; -20 clear-read keeps unread; -21 security headers; -22 forged
   `X-Forwarded-For` doesn't bypass the per-IP limit. (-18/-19 are journeys; -23/-24 are native → the
   native pieces.) Plus the R-only cases not in QA: R30 quota race, R54 cross-tenant role, R55 empty
   selector, R62 cache headers.
5. **Probes stage** (Tier A membership): `probes/rls-probe.sql` — connect as the flavor's *app* role,
   for every tenant table: no tenant set → `SELECT count(*) = 0`; tenant A set → only A's rows; insert
   with tenant B while A set → rejected. `probes/claim-probe` — 2 workers claim from a 100-row outbox
   concurrently → each row processed exactly once. Dialect files per engine (`postgres.sql`,
   `sqlserver.sql`); a flavor declares its engine in `flavor.json`.
6. **Run it against `platform-dotnet`** via the existing `e2e` job shape (compose: API + Web + Postgres
   + Mailpit) → must be fully green before `SPEC_VERSION 1.0.0` is tagged. Any red = a real finding
   in the reference (fix here first, like the v3 backfill did).

**Acceptance criteria**
```gherkin
Scenario: One command, four stages, one verdict
  Given the reference stack is up (API, Web, Postgres, Mailpit)
  When `docker run --network host spec-kit:1.0.0 --base-url http://localhost:5238 --web-url http://localhost:5169 --mailpit http://localhost:8025 --db "<app-role conn>"` runs
  Then newman, journeys, adversarial, probes each write JUnit XML to /out and the exit code is 0

Scenario: A tenancy regression is caught by the kit, not by C# tests
  Given a build of the reference with the RLS policy dropped on one tenant table
  When the kit runs
  Then the rls-probe fails naming the table, and QA-ADV-01 fails, while the C# unit tests may still pass

Scenario: The kit is toolchain-free for the flavor
  Given a machine with only Docker
  When the kit runs against any flavor's compose stack
  Then no .NET, Node, or browser install is required on the host
```
**Definition of done:** image published to GHCR (`ghcr.io/argamboad/spec-kit:<semver>`); green
against the reference; `platform-dotnet` CI adds a `conformance` job that runs the image instead of
its own `e2e` job (same compose; the job is the proof the reference gates the spec); `SPEC_VERSION
1.0.0` tagged.

### SPEC-4 — The test-id contract

**As a** frontend flavor author **I want** the list of every test id the journeys touch, with meaning
**So that** one journey suite drives Blazor, React, Angular, and any later DOM frontend unchanged.

**Steps**
1. Seed `contract/testids.json` from the 95 existing values (`grep -rhoE 'data-testid="[^"]+"'
   src/Shared.Ui | sort -u`): `{ "login-email": { "screen": "login", "kind": "input", "meaning":
   "email address for passwordless sign-in" }, … }`. Naming stays `<screen-or-component>-<element>`
   (already the convention: `login-*`, `join-*`, `member-*`, `notif-*`, `billing-*`, `admin-*`,
   `mfa-*`, `invite-*`, `theme-switcher`, `language-switcher`, `sign-out`, `tenant-badge`,
   `impersonation-banner`).
2. **Lint A (journeys):** every selector in `kit/journeys` is `GetByTestId(...)` or a role/text
   query on an element *inside* a test-id root; any `Locator("css…")` fails the lint. Fix the few
   stragglers in this repo first (the suite already selects by test id widely).
3. **Lint B (frontends):** `tools/testids-lint.py <src-dir>` asserts every id in the contract appears
   at least once in the frontend's source (Blazor: `.razor`; React: `.tsx`; Angular: `.html`); ids in
   the source not in the contract fail (no private ids — add to the contract or drop). Run in each
   frontend's CI.
4. Contract rule: an id's **meaning** is stable across versions; renaming = a spec minor bump + a
   port issue per frontend; a new screen adds ids in the same spec PR as its journey.
5. Add the ids the journeys need but the DOM lacks (none expected — the E2E suite is green today; the
   lint will tell).

**Acceptance criteria**
```gherkin
Scenario: The reference frontend satisfies the contract
  Given contract/testids.json with the 95 seeded ids
  When testids-lint runs on src/Shared.Ui
  Then zero missing, zero unknown

Scenario: A journey cannot depend on markup
  Given a journey using page.Locator(".btn-primary")
  When lint A runs
  Then it fails naming the file and line

Scenario: A second frontend needs no journey changes
  Given FRONT-REACT implements every id
  When the kit's journey stage runs against React
  Then the 35 journeys pass without a code change in kit/journeys
```
**Definition of done:** contract seeded; both lints in CI (spec + platform-dotnet); the journeys
stage passes against Blazor via test ids only.

### SPEC-5 — Conformance matrix, support policy, version protocol

**As a** maintainer of N flavors **I want** one board that says which flavor is green at which spec
version **So that** "supported" is a fact the CI computes, not a claim.

**Steps**
1. `matrix/flavors.json`: `[{ "id": "dotnet-blazor-postgres", "repo": "argamboad/perezosoft-platform",
   "compose": "docker-compose.kit.yml", "backend": "dotnet", "frontend": "blazor", "db": "postgres",
   "engine": "postgres", "spec_version": "1.0.0" }, …]`. Row 1 is the reference.
2. `matrix.yml` (in `spec`): on `workflow_dispatch` + weekly `schedule` + on spec tags: for each row,
   checkout the repo at its default branch, `docker compose -f <compose> up -d --wait`, run the kit
   image at the row's `spec_version`, publish a per-row JUnit + a summary table to the job summary and
   to `matrix/STATUS.md` (committed by the workflow — the only write it has). Row jobs use
   `${{ vars.CI_LINUX_RUNNER || 'ubuntu-latest' }}` (LOCALCI-1 convention) — runs are long.
3. **Version protocol** (`SPEC_VERSION protocol.md`): patch = wording/kit fixes (no port needed);
   minor = new outcome/test id/endpoint (port issue per flavor, auto-opened by the tag workflow using
   `gh issue create` in each flavor repo — the one cross-repo write, via a fine-grained PAT scoped to
   issues); major = a changed outcome. A flavor claims a version by bumping its `SPEC_VERSION` file;
   its own CI must be green at that version first; the matrix confirms.
4. **Support policy** (`README.md`): *Supported* = matrix cell green at the current spec minor;
   *Lagging* = green at the previous minor (port issue open); *Community* = older or red for > 60 days
   (still listed, not recommended by the scaffold). The reference is exempt from demotion — it gates
   the spec, so it is green by construction.
5. Reference fan-out hook: a spec change **starts** in `platform-dotnet` (implement + pass the kit),
   then lands in `spec` with the tag; never the other way round.

**Acceptance criteria**
```gherkin
Scenario: The board reflects reality
  Given flavors.json with the reference row only
  When matrix.yml runs
  Then matrix/STATUS.md shows dotnet-blazor-postgres ✅ at 1.0.0 with a link to the run

Scenario: A minor bump opens port issues
  Given spec is tagged 1.1.0 with a new K test
  When the tag workflow runs
  Then every flavor repo except the reference gets an issue "Port spec 1.1.0" with the changelog

Scenario: A lagging flavor is demoted, not hidden
  Given a flavor red at 1.1.0 for 61 days
  When the weekly matrix runs
  Then STATUS.md lists it as Community and the scaffold's --list marks it "not recommended"
```
**Definition of done:** matrix green with the reference row; protocol + policy written; tag workflow
opens issues; `LOCALCI`-style runner variables honoured.

**`SPEC` order & size:** SPEC-1 → SPEC-2 → SPEC-4 → SPEC-3 → SPEC-5 (the contract before the kit;
test ids before journeys move). Size L: ≈ 2 slices each for 1/2/3, 1 each for 4/5. **Exit criterion for
the whole epic:** `SPEC_VERSION 1.0.0` tagged, the reference passes the kit in its own CI, matrix
row 1 green.

---

## Epic `FRONT-REACT` — the first non-Blazor frontend, against the .NET reference API

Repo `argamboad/frontend-react`. Stack: **Vite + React + TypeScript**, **TanStack Router** (file
routes) + **TanStack Query**, **Bootstrap 5** (kept on purpose: the theme contract is `data-bs-theme`,
the dark-token block in `app.css` ports as-is, and `REBRANDING.md` stays valid), **i18next** fed by
`spec/contract/strings/*.json`, **Zod** for API response parsing, **Playwright** = the kit's journeys
(no suite of its own), **Vitest + Testing Library** for component tests (R70). Hybrid shells:
**Capacitor** (Android/iOS) and **Tauri** (Windows/macOS) in the same repo under `native/`.

**Read first:** `spec/contract/screens.md`, `testids.json`, `endpoints.md`, `flows.md` (auth +
tenancy sequences), and in this repo `docs/stories/theme.md`, `prefs.md`, `mfa.md`, `notify.md`,
`native.md` (G1–G7 + NATIVE-12 — the lessons transfer to Capacitor/Tauri one-to-one).

| Slice | Delivers | Reference to port from |
|---|---|---|
| **FR-1** bootstrap + auth client | Vite app; API client with **access token in memory + refresh rotation** (`/api/auth/refresh`, single in-flight refresh promise — the v3 backfill found a stale-refresh-cache bug in Blazor's `AuthService`; write the test for it first), `SignedIn` event, **bfcache guard** (`pageshow persisted` → reload), OAuth redirect start + `AuthCallback`/`AuthError` pages, magic link landing, OTP form, MFA step-up (JSON path), `Login` screen with every `login-*` test id; **theme pre-paint** script in `index.html` before the stylesheet (`theme.js` contract, `light|dark|system`) | `src/Shared.Ui/Pages/Login.razor`, `AuthCallback.razor`, `AuthError.razor`; `src/Web` `AuthService`; `wwwroot/js/theme.js`, `bfcache-guard.js` |
| **FR-2** shell + prefs | `AppHeader` (nav-admin/billing, `tenant-badge`, `sign-out`, `impersonation-banner`), `ThemeSwitcher`, `LanguageSwitcher`, **preference reconcile on every sign-in** (server wins; never-set adopts device; never while impersonating; locale mismatch → persist + ONE reload — PREFS-1/ADR-022), `PUT /api/auth/theme` + locale endpoints | `AppHeader.razor`, `ThemeSwitcher.razor`, `LanguageSwitcher.razor`, `MainLayout` reconcile; `stories/prefs.md` |
| **FR-3** household | `Home`, `Household` (roster: `member-*`, promote/demote/remove, `transfer-*`, `leave-*`, rename, invitations `invite-*`, `pending-invite-row`), `Join` (token + `join-code-*` paste path, `join-household-full` 402 state, `join-needs-signin`) | `Household.razor`, `Join.razor`; `stories/rbac.md`, `billing.md` (BILLING-9) |
| **FR-4** settings | `Settings`: `MfaCard` (enroll: `mfa-secret` + QR from otpauth URI client-side, `mfa-confirm-code`, `mfa-saved-codes` typeable recovery codes, `mfa-status-on`), `NotificationPrefsCard` (`notif-prefs-*`), `preferences-card`, `export-data`/`export-download` (same-tab download), `delete-account` | `Settings.razor`, `MfaCard.razor`, `NotificationPrefsCard.razor`; `wwwroot/js/mfa-qr.js`; `stories/gdpr.md`, `mfa.md` |
| **FR-5** notifications + billing | `NotificationBell` (`notif-*`: panel, count, item, delete, clear-read (never unread), clear-all, empty), `Billing` (`billing-*`: plan/status/seats/renews, upgrade → Checkout, portal, success/cancel return pages, owner-only, ended, error) | `NotificationBell.razor`, `Billing.razor`; `stories/notify.md`, `billing.md` (8) |
| **FR-6** admin console | `/admin` gated by staff claim (`admin-forbidden`), tenant rows + inspect, impersonate (banner, prefs untouched), announce (`admin-announce-*`) + broadcast (`admin-broadcast-*`), MFA reset (`admin-mfa-reset*`), comp/revert (`admin-comp-pro`/`admin-revert-free`, 409 when Stripe-backed) | `AdminConsole.razor`; `stories/admin.md`, ADR-021 |
| **FR-7** conformance | `testids-lint` green (95/95), kit journeys green against React + the .NET API, component tests for every screen (R70), `BINDINGS.md` + `RULES_MAP.md`, `SPEC_VERSION` claimed | — |
| **FR-8** hybrid shells | **Capacitor** Android + iOS and **Tauri** Windows + macOS: OAuth via system browser + app-scheme deep link (port G7's initiator generalisation + NATIVE-12 process-death resilience: persist the OAuth state before leaving the app, resume from a cold start), refresh-on-resume (G2), Android back handling (G3), downloads via OS share sheet (G1 → `IFileDownloadLauncher` analogue), culture bootstrap (G6), Release build fails on a localhost API base (R67), boot-to-login smoke per platform in CI mirroring `native-smoke-*` | `stories/native.md`, `docs/NATIVE_PARITY.md`, `docs/MOBILE_TESTING.md`, `ci.yml` smoke jobs |

**Acceptance criteria (epic level — slice Gherkin = the 35 journeys + the screen behaviours in
`screens.md`, so they are not duplicated here)**
```gherkin
Scenario: The React frontend is conformant without touching the kit
  Given frontend-react at SPEC_VERSION 1.0.0 and the .NET reference API
  When the kit runs with --web-url pointed at the Vite build
  Then journeys 35/35 pass, testids-lint reports 0 missing / 0 unknown, and no file in kit/ changed

Scenario: Theme has no light flash
  Given a user whose saved theme is dark
  When the app cold-loads
  Then the first painted frame already has data-bs-theme="dark" (Playwright: screenshot at domcontentloaded)

Scenario: A refresh race issues one refresh, not two
  Given an expired access token and two concurrent API calls
  When both 401 and retry
  Then exactly one POST /api/auth/refresh is sent and both calls succeed with the rotated token

Scenario: Sign-out leaves nothing readable via Back
  Given a signed-in user who signs out
  When the browser Back button restores the page from bfcache
  Then the page reloads and shows the login screen

Scenario: Hybrid shell survives process death mid-OAuth
  Given the Android Capacitor app starts Google sign-in and the OS kills the app while in the browser
  When the redirect returns
  Then the app cold-starts, completes the sign-in from persisted state, and lands in the household
```
**Out of scope:** Expo (own epic), design changes (parity first — same Bootstrap look), Angular.
**Definition of done:** FR-1…FR-8 merged; matrix row `dotnet-react-postgres` green; hybrid shell smokes
in the repo's CI; `REBRANDING.md`-equivalent for React in the repo; `NEW_APP_GUIDE` React section.
**Size:** L (8 slices; FR-8 alone ≈ 2).

---

## Epic `BACK-NODE` — the first full backend port

Repo `argamboad/backend-node`. Stack: **NestJS on Fastify**, **Drizzle** (node-postgres), **Zod**
(via `nestjs-zod`), **jose** (JWT), **otpauth** (TOTP), **nodemailer** (behind an `EmailSender` port —
the only mail dependency, R-style), **stripe** SDK, **@opentelemetry** SDK, **Pino** logging,
**Vitest** + **Testcontainers** for integration tests, **dependency-cruiser** + `eslint-plugin-boundaries`
for S rules, **pnpm** with a committed lockfile and `--frozen-lockfile` (R25). Node LTS pinned in
`.nvmrc` + Dockerfile + CI from one file (R61).

**Architecture mapping (keep the names so the docs transfer):** `src/core` (entities as Drizzle
schema + domain services) · `src/infrastructure` (persistence, RLS session, outbox, email, files,
billing provider) · `src/api` (Nest modules = controllers; `features/` = vertical slices with the
`Notes` exemplar) · `src/shared` (contracts). Tenancy: request-scoped `TenantContext` from the
`tenant_id` JWT claim → a Drizzle **transaction wrapper** that runs `SELECT set_config('app.tenant_id',
$1, true)` first (the platform's GUC name — `RlsDdl.TenantGuc` — reuse it so the same RLS DDL applies);
`enterTenant(id, fn)` for signature/system-authenticated writes (billing webhook, impersonation);
`queryAllTenants()` only in `*DataContributor` (R38 via dependency-cruiser rule). The RLS DDL is
**generated by the same script** as the reference (port `RlsDdl.StatementsFor` to `tools/rls-ddl.ts`
and diff its output against the C# output in a test — zero policy drift between flavors).

**Slice ladder (the platform's own epic order — read each story file before its slice):**

| Slice | Ports | Reference |
|---|---|---|
| BN-1 | skeleton, config (`Section__Key` env, typed + validated at start, gated features default off — R21/R22/R53), health/readiness/version, OpenAPI at `/swagger` + `/api/public/openapi.json` shape, error shape (R16/R18), migrations (Drizzle Kit) + drift gate (R-ish: `drizzle-kit check`) | `Program.cs`, `.env.example`, `EnforcementGateTests` |
| BN-2 | AUTH: passwordless (magic link + OTP: single-use, hashed, time-limited, atomic redemption R48), OAuth Google + Microsoft (one-line provider add), JWT access + rotating refresh with replay → revoke-all (QA-ADV-17), sessions, rate limits split per QA (R-rate), native auth code exchange | `stories/*auth*`, `AuthController`, `NativeAuthController`, `PasswordlessService`, `TokenService`, `SessionService` |
| BN-3 | TENANT: household, memberships, roles owner/admin/member (RBAC-1/2), invitations (token + code, seat re-check at accept → 402 `seat_limit_reached`), transfer/leave/dissolve (`DataContributor` pattern), erasure per user AND per tenant (R12/R43/R44) | `HouseholdController`, `HouseholdInvitationsController`, `AccountController`, `stories/rbac.md`, `gdpr.md` |
| BN-4 | RLS: `tools/rls-ddl.ts`, policy-in-same-migration gate (R42), session interceptor + cache-invalidate-on-revert (R41 analogue), two-role topology (migrator/app), `EnterTenantScopingTests` port; **run the kit's rls-probe** | `RlsDdl.cs`, `RlsSessionInterceptor.cs`, `Rls/` tests, ADR-020, `DEPLOYMENT.md` §7 |
| BN-5 | JOBS: outbox (claim with `FOR UPDATE SKIP LOCKED`, attempt/dead-letter on any failure R57), inbox (idempotency), scheduler (NOTIFY wake + poll), `SKIP LOCKED` probe green | `Outbox/`, `Inbox/`, `Scheduling/`, `stories/async-jobs.md`, ADR-007 |
| BN-6 | OBS: structured logs, OTel traces/metrics/logs export, append-only audit log (DB trigger blocks update/delete) with attribution incl. impersonation (R45/R52) | `stories/observability.md`, ADR-008 |
| BN-7 | BILLING: entitlements, Checkout, webhook (signature R1, strictly-newer R29/R56, first-event no false-notify), portal, seat/usage quotas atomic (R30/R58), trial/dunning transitions (R59), dissolve cleanup, `GET /api/billing`, admin comp/revert 409 rule | `stories/billing.md` 1–9, ADR-006/021 |
| BN-8 | MFA: TOTP enroll (secret encrypted at rest R51), recovery codes hashed + single-use, step-up on **every** sign-in path (JSON + redirect + native), per-user IP-independent cap (R47/R49), state not mutated before confirm (R50) | `stories/mfa.md`, ADR-012 |
| BN-9 | NOTIFY + ADMIN: notification center + prefs, security notifications on both channels (R46), delete/clear semantics (R55, QA-ADV-20); staff gate, inspect, impersonation (role ceiling, no staff actions, prefs untouched), announce/announce-all via outbox fan-out | `stories/notify.md`, `admin.md`, ADR-013/014/021 |
| BN-10 | FILES + PUBAPI + HOOKS (both gated default-off): `FileStorage` local/S3 with tenant-scoped keys + signed URLs; API keys hash-only + per-key rate limit + public OpenAPI; webhook subscriptions (encrypted secret) → outbox → HMAC POST with retry, delivery log + replay, SSRF guard (R3) | `stories/files.md`, `pubapi.md`, `hooks.md`, ADR-010/015/016 |
| BN-11 | Conformance: Newman 79/79, adversarial all green, probes green, `RULES_MAP.md` (every S rule → a dependency-cruiser/Vitest test), `BINDINGS.md`, Dockerfile (distroless Node) + compose + `render.yaml`, `SPEC_VERSION` claimed, matrix row `node-react-postgres` | — |

**Acceptance criteria (epic level)**
```gherkin
Scenario: The Node backend is indistinguishable from the reference to the kit
  Given backend-node at SPEC_VERSION 1.0.0 with Postgres + Mailpit
  When the kit runs with --base-url at the Nest API and --web-url at frontend-react
  Then newman 79/79, journeys 35/35, adversarial all green, rls-probe + claim-probe green

Scenario: RLS DDL cannot drift between flavors
  Given tools/rls-ddl.ts and the reference RlsDdl.StatementsFor
  When the parity test renders both for the same tenant-table list
  Then the SQL is identical modulo whitespace

Scenario: A slice author cannot bypass tenancy
  Given a feature module importing queryAllTenants outside a DataContributor
  When dependency-cruiser runs
  Then the build fails naming the module (R38)
```
**Definition of done:** BN-1…BN-11 merged; matrix row green; `docs/` in the repo mirror the flavor's
bindings; the Node `NEW_APP_GUIDE` section. **Size:** L (11 slices; ≈ 40–60 % of the original build).

---

## Epic `BACK-GO` — same ladder, repository-first tenancy

Repo `argamboad/backend-go`. Stack: **chi**, **pgx v5** + **sqlc**, **goose** migrations, **golang-jwt**,
**pquerna/otp**, **go-mail**, **stripe-go**, **OTel Go SDK**, **slog**, **testcontainers-go**,
**go-arch-lint** + `depguard`, distroless/scratch image. Go version pinned in `go.mod` `toolchain`
directive = Dockerfile = CI (R61).

**What differs from Node (everything else follows the BN ladder 1:1):**
- **No ORM filter ⇒ the repository layer is the first tenant wall.** Every sqlc query on a tenant
  table takes `tenant_id` as a parameter **and** runs inside `WithTenant(ctx, fn)` which sets the GUC
  in the transaction; `internal/repo` is the only package allowed to import `pgx` (go-arch-lint rule =
  R38/R39 analogue); `QueryAllTenants` is a separate package importable only by `*datacontributor`.
- **sqlc as the drift gate:** `sqlc vet` + a generated-code-is-committed check replace
  `has-pending-model-changes`; goose migrations include the RLS DDL from the same generator (port to
  `tools/rlsddl`), parity-tested against the reference output.
- **NOTIFY via `pgx` `WaitForNotification`** in the scheduler; outbox claim identical SQL.
- Errors: one `ErrorResponse` type; `errors.Is` mapping in one middleware (R16/R18).
- Slice ladder BG-1…BG-11 = BN-1…BN-11 with the references above; BG-11's matrix row is
  `go-react-postgres`.

```gherkin
Scenario: The wall holds without an ORM
  Given a handler that queries a tenant table through internal/repo with tenant A in context
  When the row belongs to tenant B
  Then the repo returns not-found AND the rls-probe confirms the DB would have hidden it anyway

Scenario: Raw database access outside the repository layer is impossible
  Given a package outside internal/repo importing github.com/jackc/pgx/v5
  When go-arch-lint runs
  Then the build fails
```
**Size:** L. **Deps:** SPEC; FRONT-REACT for the matrix row.

---

## Epic `BACK-SPRING` + `FRONT-ANGULAR` — the enterprise pair (demand-driven)

**Spring:** Spring Boot (Java 21 LTS or Kotlin), Spring Web MVC, **Hibernate `@Filter`** for the first
wall (≡ EF global filter) with a `TenantFilterAspect` enabling it per request, **Flyway** for
migrations (RLS DDL from the shared generator, parity-tested), `DataSource` wrapper setting the GUC
per transaction, **Spring Security** with a custom JWT filter (NOT Spring's OAuth2 login for the
app session — the platform's own token model, ADR-002), `spring-boot-starter-mail` behind an
`EmailSender` port, `stripe-java`, Micrometer + OTel, **ArchUnit** for every S rule, **Testcontainers**.
Ladder BS-1…BS-11 = BN's. Gradle with dependency locking (R25), `.sdkmanrc` + Dockerfile + CI pin
(R61).

**Angular:** Angular (latest stable), standalone components, signals, Angular Router, `HttpClient`
interceptor for the refresh-rotation client (same single-in-flight rule as FR-1), **Bootstrap 5** +
`data-bs-theme` (same theme contract), `@angular/localize` fed from `spec/strings`, Karma → **Vitest**
(Angular's current default) for component tests (R70), Capacitor + Tauri under `native/`. Screen
ladder FA-1…FA-8 = FR-1…FR-8. Note for the porter: Angular's DI/services/typed forms make this the
most mechanical Blazor port — port `Shared.Ui` component by component.

```gherkin
Scenario: The enterprise pair passes the same kit
  Given backend-spring + frontend-angular at the current spec version
  When the kit runs
  Then newman 79/79, journeys 35/35 (test ids only), adversarial + probes green
```
**Size:** L + L. **Deps:** SPEC; demand (a customer or downstream app asking for Java).

---

## Epic `BACK-FASTAPI` (demand-driven)

FastAPI, **SQLAlchemy 2** (async, `asyncpg`) + **Alembic** (RLS DDL from the shared generator),
Pydantic v2 settings (`Section__Key` env), **PyJWT**, **pyotp**, `aiosmtplib` behind an `EmailSender`
port, `stripe`, OTel Python, **pytest** + Testcontainers, **import-linter** contracts for S rules,
**uv** with a committed lockfile (R25), Python pinned in `.python-version` = Dockerfile = CI (R61).
First wall: a `TenantSession` that injects `WHERE tenant_id = :t` through a SQLAlchemy `do_orm_execute`
event (the closest analogue to EF's global filter) + `set_config` on transaction begin. Ladder
BF-1…BF-11 = BN's. Known friction to budget for: async DB sessions + Testcontainers fixtures; keep the
outbox worker a separate process (same as the reference's hosted service).

**Size:** L. **Deps:** SPEC; demand.

---

## Epic `NATIVE-RN` — true-native mobile for the React family

Repo `argamboad/native-rn`. **Expo** (managed workflow, EAS builds), **expo-router**, **TanStack
Query** (shared hooks + the API client extracted from `frontend-react` into a small `@perezosoft/
client-ts` package — the first shared package between two pieces; publish to GitHub Packages),
`expo-auth-session` for OAuth via the system browser (process-death resilience from NATIVE-12: persist
state in `expo-secure-store` before leaving), `expo-secure-store` for the refresh token, `expo-local-
authentication` optional, i18n from `spec/strings`, theme from the OS appearance + the account
preference (same reconcile rules). **Own journey suite** (S-002): **Maestro** flows mirroring the
Gherkin of the 35 journeys that make sense on mobile (no admin console; billing via portal link).
Slices: RN-1 auth + shell · RN-2 household/join · RN-3 settings/MFA/notifications · RN-4 Maestro suite
+ EAS CI + release checklist (port QA §13c). Desktop stays Tauri (no RN-Windows/macOS).

```gherkin
Scenario: Cold-start resume after OAuth process death
  Given the OS kills the app while the system browser shows Google sign-in
  When the redirect deep-links back
  Then the app resumes the sign-in from secure-store state and lands in the household
```
**Size:** M. **Deps:** FRONT-REACT (the shared client package).

---

## Epic `FRONT-FLUTTER` — per-app, mobile-first only (demand-driven)

Repo `argamboad/frontend-flutter`. Flutter (stable channel), **Riverpod**, **go_router**, **dio** with
the refresh-rotation interceptor, `flutter_appauth`/`url_launcher` + app links for OAuth (same
resilience rules), `flutter_secure_storage`, `intl` ARB files generated from `spec/strings`, Material 3
theming mapped to `light|dark|system`. **Own journey suite:** `integration_test` + **Patrol** mirroring
the Gherkin; the test-id contract maps to `ValueKey`s with the same names (`Key('login-email')`) so the
*names* stay shared even though the driver differs. Targets: iOS/Android first-class; Windows/macOS/
Linux built; **Flutter web built but documented as second-class** (S-002 waiver recorded in the app's
brief). Slices FF-1…FF-4 mirror RN's plus FF-5 desktop builds + release checklist.

**Size:** L. **Deps:** SPEC; a mobile-first downstream app asking for it.

---

## Epic `DB-SQLSERVER` — second Tier-A database for the .NET and Spring flavors

**Slices**
- DS-1 (this repo): EF Core SQL Server provider behind a `Database:Provider` switch; migrations
  regenerated per provider (two migration assemblies); `RlsDdl` gains a **dialect**: `CREATE SCHEMA
  Security; CREATE FUNCTION Security.tenantPredicate(@TenantId uniqueidentifier) RETURNS TABLE WITH
  SCHEMABINDING AS RETURN SELECT 1 AS ok WHERE @TenantId = CAST(SESSION_CONTEXT(N'tenant_id') AS
  uniqueidentifier) OR CAST(SESSION_CONTEXT(N'rls_bypass') AS nvarchar(3)) = N'on'; CREATE SECURITY
  POLICY Security.tenantPolicy ADD FILTER PREDICATE … ON <table>, ADD BLOCK PREDICATE … AFTER INSERT,
  … AFTER UPDATE, … BEFORE UPDATE, … BEFORE DELETE ON <table> WITH (STATE = ON)` per tenant table;
  the parity gate (R37/R42) runs per dialect.
- DS-2: `RlsSessionInterceptor` SQL Server variant — set `sp_set_session_context @key=N'tenant_id',
  @value=…, @read_only=1` on **every connection open** (session-scoped; the pool's reset clears it —
  the interceptor is the wall, the reset is the net); bypass key the same way; **the pooled-connection
  adversarial test** (R41 analogue: alternate tenants across pooled connections 1,000× → zero leaks)
  written first.
- DS-3: outbox claim `WITH (UPDLOCK, READPAST, ROWLOCK)`; dispatcher wake = poll only (no NOTIFY);
  audit-log append-only via `DENY UPDATE, DELETE` to the app login + an `INSTEAD OF` trigger belt;
  quota upsert via `MERGE` with the unique-violation number `2627` (R58 analogue).
- DS-4: kit probes gain `sqlserver.sql`; compose profile `db=sqlserver` (`mcr.microsoft.com/mssql/
  server:2022-latest` — pin a tag); matrix rows `dotnet-blazor-sqlserver`, later `spring-*-sqlserver`;
  `DEPLOYMENT.md` gains an Azure SQL free-tier section.

```gherkin
Scenario: Session context never leaks across pooled connections
  Given two tenants alternating requests on a pool of size 2 for 1,000 iterations
  When each request reads its tenant table
  Then no response ever contains the other tenant's rows (and the rls-probe passes on the SQL Server dialect)

Scenario: A set-based update is filtered without query tags
  Given tenant A's session and an ExecuteUpdate over the notifications table
  When it runs
  Then only A's rows change (the SESSION_CONTEXT predicate applies regardless of how the statement was built)
```
**Size:** M. **Deps:** SPEC (probes); demand.

---

## Epic `SCAFFOLD` — compose pieces into one app repo

Repo `argamboad/scaffold`. A small **Node CLI** (`npx @perezosoft/create` or `perezosoft new`):
`--backend dotnet|node|go|spring|fastapi --frontend blazor|react|angular|flutter --db postgres|
sqlserver --name <App> --tenant-label <Household>`; validates the combo against `spec/matrix/
flavors.json` (supported/lagging/community, refuses red), fetches each piece at the pinned spec
version (`gh repo clone` at tag → `git subtree add` into `apps/<piece>` or a flat layout for
single-repo flavors like `platform-dotnet`), runs the piece's **rebrand script** (each piece ships
`tools/rebrand` implementing its `REBRANDING.md` — name, tenant label, logo, colours, email templates),
writes the root `README`, `CLAUDE.md` (from the spec's neutral skeleton + each piece's section),
compose file wiring backend ↔ frontend ↔ db ↔ Mailpit, and a first CI workflow that runs the kit.
Slices: SC-1 CLI + combo validation · SC-2 fetch/compose/rebrand · SC-3 generated CI + kit run ·
SC-4 `--list` + docs.

```gherkin
Scenario: A supported combo becomes a green app in one command
  Given the matrix shows node-react-postgres supported at 1.2.0
  When `perezosoft new --backend node --frontend react --db postgres --name Vuelto2` runs
  Then a repo exists with both pieces rebranded, `docker compose up` boots, and the generated CI runs the kit green

Scenario: A community combo is refused with the reason
  Given fastapi-angular-postgres is not a matrix row
  When the CLI is asked for it
  Then it exits non-zero listing the nearest supported combos
```
**Size:** M. **Deps:** ≥ 2 backends and ≥ 2 frontends supported.

---

## Order, sizing, stop points

| # | Epic | Size | Exit criterion | Stop here? |
|---|---|---|---|---|
| 1 | `SPEC` | L | `SPEC_VERSION 1.0.0`; reference passes the kit in its own CI; matrix row 1 green | Yes — the platform is now a spec with a reference; even alone this hardens the reference (the kit catches what C# unit tests can't) |
| 2 | `FRONT-REACT` | L | matrix row `dotnet-react-postgres` green; hybrid smokes | Yes — the exchangeable-frontend claim is proven |
| 3 | `BACK-NODE` | L | row `node-react-postgres` green | Yes — the JS flavor exists end-to-end |
| 4 | `BACK-GO` | L | row `go-react-postgres` green | Yes |
| 5 | `BACK-SPRING` + `FRONT-ANGULAR` | L + L | rows `spring-react-postgres`, `spring-angular-postgres` | demand |
| 6 | `BACK-FASTAPI` | L | row `fastapi-react-postgres` | demand |
| 7 | `NATIVE-RN` · `FRONT-FLUTTER` · `DB-SQLSERVER` · `SCAFFOLD` | M · L · M · M | per epic | demand |

**Before starting `SPEC`:** decide the GitHub org (keep `argamboad/*` or create `perezosoft/*`; GitHub
redirects renames, so either is safe); confirm the repo stays private until the first external flavor;
land `LOCALCI-1` first if the matrix runs will be frequent (they are long).

## Checklist (every flavor epic)
- [ ] Tests written first (kit red → green per slice; S-rule arch tests before the code they guard)
- [ ] `BINDINGS.md` (constants → this stack) and `RULES_MAP.md` (S rule → test) present
- [ ] Config: `Section__Key` env, typed + validated at start, gated features default off, `.env.example`
      complete and CI-checked (R20/R21/R22/R53)
- [ ] Toolchain pinned from one source agreed by CI + Dockerfile (R61); lockfile committed + locked
      install (R25); license gate (R26); no ambient clock (R15)
- [ ] Tenancy: first wall (filter/repository) + second wall (Tier A: RLS from the shared DDL generator,
      parity-tested) + hatch bans (R38/R39) + erasure completeness per user and per tenant (R12/R43)
- [ ] Kit green: newman 79/79, journeys 35/35 (DOM frontends: test ids only), adversarial, probes
- [ ] Dockerfile + compose + hosting recipe (Render free / equivalent) + `render.yaml`-style file
- [ ] `SPEC_VERSION` claimed only after the flavor's own CI is green at that version; matrix row added
- [ ] Docs: the flavor's `NEW_APP_GUIDE` section, `REBRANDING` + rebrand script, `QA` native cases if
      it ships shells, CLAUDE.md for the repo
- [ ] Never merge before the branch CI finishes green
