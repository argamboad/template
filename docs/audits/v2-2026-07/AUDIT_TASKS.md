# AUDIT_TASKS.md — v2 Remediation Plan (Phase 5 consolidation gate)

> **Status: IN PROGRESS.** Approved 2026-07-01. **V2-B1 (Critical) through V2-B7 (all Highs) are IMPLEMENTED**, test-first, on branch `fix/v2-high-remediation` (off `develop`); build clean (warnings-as-error), Core 42/42, Api 371/371 green. Remaining: **V2-B8–B11** (test-completeness, debt/SOLID, docs, enforcement). This plan consolidates Phases 1–4 (all pinned to `84c7ad8`). Order mirrors the archived `audits/v1-2026-06/AUDIT_TASKS.md`: **keystone first, enforcement last.** Each task lists severity, the rule/finding it satisfies, whether it **touches core**, the **tests that must exist first** (TDD), a **done-when**, and a **verify** command.
>
> **Batch commits (fix/v2-high-remediation):** B1 `c74b359` · B2 `10b1b9b` · B3 `28dcce5` · B4 `eec8fdb` · B5 `bbc1196` · B6 `483e516` · B7 `72cba09`.

## Gate results (Phase 5)

- **Four reports share SHA `84c7ad8`** — gate OK.
- **`RULE_CONFLICTS.md`: 0 true conflicts.** Two flagged items resolved in `FOUNDATION_RULES.md` §"Phase-5 conflict resolutions" (R5⊕R33 merge; TR-8/DOC-17 doctrine wording). No two Critical invariants are irreconcilable → no human escalation required on rules.
- **Every proposed fix below was cross-checked against the full ruleset** (Phase 5 step 2): none re-introduces another phase's flagged problem. Notable checks — the GAP-1 fix (R1) must not break the "boots with zero setup" dev promise (kept: fake provider still allowed in Development); the SSRF fix (R3) routes through one seam so it can't fork; the AuthController split (SOLID-2) keeps routes stable so no client/contract regression.

## v1 regressions to restore — **NONE**

STEP 0 verified all 31 v1 remediations Held at `84c7ad8` (0 regressed, 0 superseded). There is no regression batch. *(Per the suite's "standing gate for every regressed fix" rule: N/A — nothing regressed. The new enforcement batch V2-B10 nonetheless adds gates that would catch future regressions of the areas touched here.)*

## Decisions required before some tasks (human/ADR calls)

These are intent questions the audit cannot settle; each blocks only its own task, not the batch.

| # | Question | Blocks | Recommended default |
|---|---|---|---|
| D1 | GAP-1 fix shape: fail-fast at startup (throw if no Stripe key outside Development) **or** don't-map the webhook controller when fake? | V2-B1 | ✅ **DECIDED (2026-07-01): fail-fast at startup**; keep fake in Development. |
| D2 | Billing projection: apply only strictly-newer provider events, or accept last-writer-wins? | V2-B4 | **Strictly-newer** (recency guard) — the docstrings already claim it. |
| D3 | Quota guarantee strict (never exceed under concurrency)? | V2-B5 | **Yes** — the "Atomically" contract says so. |
| D4 | Invitation acceptance: email-bound or bearer-capability? | V2-B9 (LOGIC-S3) | Leave **bearer** (document it); add email-binding only if product wants it. |
| D5 | MFA challenge clock: accept the DataProtection ambient-clock limitation or re-implement with injected clock? | V2-B3 | **Accept + document** (no exploit); revisit if a TimeProvider overload appears. |
| D6 | UTC-monthly quota reset for non-UTC tenants — intended? | V2-B5 (LOGIC-B8) | **Document as intended**; tenant-tz reset is a future option. |
| D7 | PUBAPI/HOOKS = controllers (amend epics) or minimal-API platform features (amend ADR-004)? | V2-B8/B9 (DEBT-6/TR-7) | ✅ **DECIDED (2026-07-01): amend ADR-004** to sanction config-gated minimal-API platform surfaces; move files to `src/Api/Endpoints/`. |

---

## Progress tracker

| Batch | Theme | Tasks | Highest sev | Touches core | Status |
|---|---|---|---|---|---|
| **V2-B1** | Billing webhook auth (keystone) | B1-1…B1-3 | **Critical** | yes | ✅ Done (`c74b359`) |
| **V2-B2** | Write-side tenant guard | B2-1…B2-2 | High | yes | ✅ Done (`10b1b9b`) |
| **V2-B3** | MFA step-up anti-replay | B3-1…B3-2 | High | yes | ✅ Done (`28dcce5`) |
| **V2-B4** | Billing event ordering | B4-1…B4-2 | High | no (Api/Core) | ✅ Done (`eec8fdb`) |
| **V2-B5** | Correctness: SSRF, fail-open, quota, clocks | B5-1…B5-5 | High | mixed | ✅ Done (`bbc1196`) |
| **V2-B6** | GDPR per-user erasure seam | B6-1 | High | yes (Core seam) | ✅ Done (`483e516`) |
| **V2-B7** | Harness de-couple from Notes | B7-1…B7-2 | High | tests only | ✅ Done (`72cba09`) |
| **V2-B8** | Test-completeness (test-first) | B8-1…B8-6 | Critical (tests) | tests only | ⬜ Pending |
| **V2-B9** | Debt & SOLID | B9-1…B9-7 | High | yes | ⬜ Pending |
| **V2-B10** | Docs reconcile | B10-1…B10-8 | High | no | ⬜ Pending |
| **V2-B11** | Enforcement & Definition of Solid | B11-1…B11-9 | — | tests/CI | ⬜ Pending |

**Recommended order:** V2-B1 → B2 → B3 → B4 → B5 → B6 → B7 → (B8 interleaved as each fix's tests are its own precondition) → B9 → B10 → **B11 last**. B7 before B8's tenancy tests (they need the test-only fixture entity). B11 locks in everything.

---

## V2-B1 — Billing webhook authenticity (KEYSTONE · do first)
The one Critical. Fixes the default-config unauthenticated cross-tenant write.

- [x] **B1-1 · Gate `FakeBillingProvider` to Development** — Critical · (GAP-1, R1) · **touches core**
  - Test-first: `BillingWebhook_FakeProvider_RegisteredOnlyInDevelopment` — build DI with no Stripe key under a non-Development `IHostEnvironment` ⇒ startup **throws** (per D1).
  - Fix: in `ServiceCollectionExtensions.cs:96-99`, register `FakeBillingProvider` only when `environment.IsDevelopment()`; otherwise, absent a Stripe key, throw at startup.
  - Done-when: non-Dev + no key ⇒ startup exception; Dev unchanged; existing billing tests green.
  - Verify: `dotnet test tests/Api.Tests --filter FullyQualifiedName~Billing`
- [x] **B1-2 · Log rejected/forged webhook signatures** — Low · (GAP-5, R19) · touches core
  - Test-first: `BillingWebhook_ForgedSignature_LogsWarning_WithSourceContext`.
  - Fix: warning log (+ optional audit/metric) in the `InvalidSignature` path of `BillingWebhookController`/`BillingWebhookHandler`, incl. source IP.
  - Verify: `dotnet test tests/Api.Tests --filter FullyQualifiedName~BillingWebhook`
- [x] **B1-3 · (D1 confirmed) document the non-Dev-without-Stripe posture** — Low · docs · no core
  - Done-when: `TECH_STACK.md`/ADR-006 note that production requires a real provider; a dated ADR-006 amendment records the environment gate.
  - **Exit check (V2-B1):** an unauthenticated `POST /api/billing/webhook` cannot mint a subscription in a non-Dev build; forged signatures are logged.

## V2-B2 — Write-side tenant guard (UPDATE/DELETE)
- [x] **B2-1 · Interceptor rejects foreign UPDATE/DELETE** — High · (ADV-1, R32) · **touches core**
  - Test-first: `Interceptor_ForeignTenant_Update_And_Delete_AreScoped` — a row loaded via the hatch then `Modified`/`Deleted` under a different current tenant throws.
  - Fix: extend `TenantStampingInterceptor` (`:53`) to inspect `Modified`/`Deleted` `ITenantScoped` entries and throw when `TenantId != currentTenantId` on a tenant context (system/no-tenant context exempt).
  - Verify: `dotnet test tests/Api.Tests --filter FullyQualifiedName~Interceptor`
- [x] **B2-2 · Extend Features arch-ban to `QueryAllTenants`** — Medium · (ADV-2, R5) · tests only
  - Fix: add `QueryAllTenants` to the banned-substring set for `src/Api/Features/**` in `ArchitectureTests.cs`, excluding `*DataContributor.cs`.
  - Done-when: a request-path slice calling `QueryAllTenants()` fails the build; contributors still pass.
  - Verify: `dotnet test tests/Api.Tests --filter FullyQualifiedName~Architecture`
  - **Exit check (V2-B2):** careless slice write-leaks are blocked at CI and at runtime in both directions.

## V2-B3 — MFA step-up anti-replay
- [x] **B3-1 · Consume the step-up challenge on first success** — High · (LOGIC-S1, R28) · **touches core**
  - Test-first: `MfaVerify_ReplayedChallengeAndCode_IsRejectedOnSecondUse`.
  - Fix: make the challenge single-use (cache a jti/hash on success, reject reuse — mirror `SingleUseCacheToken<T>`).
  - Verify: `dotnet test tests/Api.Tests --filter FullyQualifiedName~Mfa`
- [x] **B3-2 · Reject replayed TOTP timestep** — High · (LOGIC-S1/S2, R28) · touches core (Core entity + service)
  - Test-first: same code accepted twice within the window ⇒ second rejected.
  - Fix: persist last-accepted TOTP timestep on `UserMfa`; in `TryVerifyTotp` capture the used step (not `out _`) and reject steps ≤ last accepted; optionally tighten enrollment-confirm window (D5 note: challenge clock left as-is, documented).
  - Migration: adds a column to `UserMfa` (drift test will require it).
  - Verify: `dotnet test tests/Api.Tests --filter FullyQualifiedName~Mfa`
  - **Exit check (V2-B3):** one captured `{challenge, code}` yields at most one session.

## V2-B4 — Billing event ordering
- [x] **B4-1 · Recency guard on the subscription projection** — High · (LOGIC-B1, R29) · Api/Core, no interceptor change
  - Test-first: `BillingWebhook_StaleRedelivery_DoesNotClobberNewerStatus`.
  - Fix: persist a provider `updated`/sequence timestamp on `Subscription`; in `UpsertSubscriptionAsync` apply only strictly-newer events (per D2). Migration adds the column.
  - Verify: `dotnet test tests/Api.Tests --filter FullyQualifiedName~BillingWebhook`
- [x] **B4-2 · Dunning fires on true transitions only** — Medium · (LOGIC-B2/B6) · Api
  - Fix: once B4-1 lands, `MaybeNotifyDunningAsync` reads the recency-ordered status; add a stale→earlier-period guard so the lapse sweep (`SubscriptionLapseSweepJob`) can't be wedged.
  - Test-first: interleaved active/past_due redelivery does not double-notify; a real re-lapse after renewal still nudges.
  - Verify: `dotnet test tests/Api.Tests --filter FullyQualifiedName~Lapse`
  - **Exit check (V2-B4):** out-of-order/redelivered billing events never regress state or mis-fire dunning.

## V2-B5 — Correctness: SSRF, fail-open, quota, clocks
- [x] **B5-1 · SSRF validator for outbound webhook URLs** — High · (GAP-2, CON-1, GAP-3, R3) · Api/Infra
  - Test-first: `WebhookUrl_SsrfTargets_AreRejected` (loopback/link-local/RFC-1918/ULA/metadata + DNS-rebinding), `WebhookUrl_Http_IsRejectedOutsideDevelopment`.
  - Fix: one `SafeHttpClient`/validator seam used by both the sync test (`WebhookEndpoints.cs`) and async `WebhookSender`; https-only outside Dev; return/store a generic error, not `ex.Message`.
  - Verify: `dotnet test tests/Api.Tests --filter FullyQualifiedName~Webhook`
- [x] **B5-2 · Reject all-invalid scopes/event-types** — Medium · (SOLID-3, R17) · Api
  - Test-first: `ApiKeyScopes_AllInvalidInput_IsRejected_NotGrantedAll`, `WebhookEventTypes_AllInvalidInput_IsRejected_NotGrantedAll`.
  - Fix: distinguish `null` (default-all) from provided-but-all-invalid (400) in `ApiKeyService.NormalizeScopes` + `WebhookSubscriptionService.NormalizeEventTypes`.
  - Verify: `dotnet test tests/Api.Tests --filter "FullyQualifiedName~ApiKey|FullyQualifiedName~Webhook"`
- [x] **B5-3 · Atomic quota consumption** — High · (LOGIC-B7, R30) · Api/Infra
  - Test-first: `Quota_TryConsume_ConcurrentAtLimit_DoesNotOverConsume` (two concurrent consumers at limit-1 ⇒ exactly one succeeds; first-of-period race doesn't 500).
  - Fix: conditional `ExecuteUpdateAsync` guarded by `Count+amount<=limit` (check rows-affected) + upsert-on-conflict for the create path (per D3).
  - Verify: `dotnet test tests/Api.Tests --filter FullyQualifiedName~Quota`
- [x] **B5-4 · Inject `TimeProvider` into `CookieService` + `S3FileStorage`** — Medium · (LOGIC-B3, GAP-4, R15) · Api/Infra
  - Test-first: `S3Download_PresignedUrlExpiry_UsesInjectedClock`; cookie expiry matches the token's injected-clock expiry.
  - Fix: replace ambient `DateTimeOffset.UtcNow`/`DateTime.UtcNow` at `CookieService.cs:36` + `S3FileStorage.cs:81` with the injected clock.
  - Verify: `dotnet test tests/Api.Tests --filter "FullyQualifiedName~Cookie|FullyQualifiedName~S3"`
- [x] **B5-5 · (D6) document UTC-monthly quota reset** — Low · docs · no core
  - **Exit check (V2-B5):** no SSRF target reachable; no full-access grant from a typo; quota cap holds under concurrency; expiries are clock-injected.

## V2-B6 — GDPR per-user erasure seam
- [x] **B6-1 · Introduce `IUserDataContributor`** — High · (SOLID-1, R12) · **touches core (new Core seam)**
  - Test-first: `Erasure_NewUserKeyedEntity_IsAlsoWiped_ViaContributor` + an arch test (R12) asserting every `UserId`-bearing entity outside the identity-core allowlist has a registered contributor.
  - Fix: add `IUserDataContributor { WipeAsync(userId, ct) }` to `Core.Abstractions`; move MFA + NOTIFY per-user deletes into contributors; `AccountErasureService` keeps identity-core rows + the contributor loop.
  - Verify: `dotnet test tests/Api.Tests --filter FullyQualifiedName~Erasure`
  - **Exit check (V2-B6):** a new user-keyed entity cannot ship without a contributor (red build), so GDPR erasure stays complete.

## V2-B7 — Harness de-couple from the DELETE-ME slice
- [x] **B7-1 · Test-only `ITenantScoped` fixture entity** — High · (TR-1, R9) · tests only
  - Fix: add a harness-owned `TestWidget` (test project) as the tenant-scoped fixture; migrate `RepositoryScopingTests`, `TenantStampingInterceptorTests`, `EnterTenantScopingTests`, `OutboxProcessorTests`, `Gdpr/*` off `Note`/`NotesDataContributor`; keep one `NotesSliceTests` proving the sample.
  - Done-when: deleting `src/Api/Features/Notes/` + its entity leaves the tenancy/GDPR/outbox tests green.
  - Verify: `dotnet test tests/Api.Tests` (then, in a scratch branch, delete Notes and re-run to confirm)
- [x] **B7-2 · Derive fixture TRUNCATE from the model** — Medium · (TR-3, R11, R34) · tests only
  - Fix: `PostgresFixture.cs:62` builds its reset list from `AppDbContext.Model.GetEntityTypes()`.
  - Verify: `dotnet test tests/Api.Tests`
  - **Exit check (V2-B7):** the sample slice is deletable per the docs without breaking the platform's own guards.

## V2-B8 — Test-completeness (test-first specs → tests)
Each spec from `LOGIC_AND_TEST_REPORT.md` Part B not already created by B1–B7. Land as failing-then-green.
- [ ] **B8-1 · Write-side tenancy negatives per epic** — Critical(test) — `ApiKey_TenantA_CannotRevokeTenantBKey`, `Webhook_TenantA_CannotDeleteOrReplayTenantBSubscription`, `Subscription_TenantA_CannotReadOrWriteTenantBSubscription`, `Notification_TenantAUser_CannotListTenantBNotifications`.
- [ ] **B8-2 · MFA step-up on every path (integration)** — High — the four `MfaStepUp_*Path*` tests (OTP, magic-link, OAuth callback, native exchange).
- [ ] **B8-3 · Migration `Down` rollback** — High — `Migrations_Down_RevertCleanly_ToInitial`.
- [ ] **B8-4 · Fail-open / SSRF / stale-webhook / clock** — covered by B5-1/2/4 + B4-1 tests; verify no duplication.
- [ ] **B8-5 · E2E journeys** — (D7-adjacent) specify + wire the missing Playwright journeys (OAuth, magic-link, tenant-isolation, RBAC three-tier, MFA enroll+step-up, GDPR export/erasure, admin impersonation, i18n) behind a bootable CI job; billing E2E blocked on a fake-provider E2E seam (note the dependency, don't silently skip).
- [ ] **B8-6 · Harness gaps** — extend `ServiceHarness` to cover MFA/notifications/files/webhooks/api-keys so slices touching them don't hand-assemble.
  - **Exit check (V2-B8):** every Critical/High spec in Part B exists and is green (or E2E explicitly tracked in CI).

## V2-B9 — Debt & SOLID
- [ ] **B9-1 · Split `AuthController`** — High · (SOLID-2) · touches core — into `MfaController`/`AccountController`/`NativeAuthController` along the section comments; routes unchanged (assert via existing auth tests).
- [ ] **B9-2 · One config-binding pattern** — Medium · (DEBT-1, R22) — typed options `.BindConfiguration().ValidateOnStart()`; reuse the single `JwtSettings` instance.
- [ ] **B9-3 · `ClaimsPrincipal.GetUserId()` helper** — Medium · (DEBT-2) — delete the 6 copies.
- [ ] **B9-4 · Per-epic `Add*()/Map*()` extensions + `IEntityTypeConfiguration<>`** — Medium · (DEBT-3/4, R34) — shrink `Program.cs` + `OnModelCreating`.
- [ ] **B9-5 · Unify RBAC 403 (filter) + shared `ErrorResponse`** — Medium · (DEBT-5, SOLID-7, R18) — `[RequireTenantPermission]` filter; drop `IErrorResponseFactory`.
- [ ] **B9-6 · Move PUBAPI/HOOKS per D7; extract `TenantDissolutionService`; branded notification email** — Medium · (DEBT-6/7/8, TR-7).
- [ ] **B9-7 · Lower-value smells** — Low · (DEBT-9/10/11, SOLID-5/6/8, LOGIC-S3 per D4) — DTO homes, locale constant, dissolve dup, result types, `AuthSchemes` constant, invitation email-binding decision.
  - **Exit check (V2-B9):** the patterns every slice copies are single-sourced; no god controller.

## V2-B10 — Docs reconcile
- [ ] **B10-1 · `DATA_MODEL.md`: add the 4 live entities** — High · (DOC-10) — `ApiKey`, `WebhookSubscription`, `WebhookDelivery`, `UsageCounter`; retitle built-vs-future; fix the dissolve-hook guidance to `ITenantDataContributor` (DOC-11); add `LapseNotifiedAt` (DOC-12).
- [ ] **B10-2 · CLAUDE.md golden rule 1 + `DATA_MODEL.md:13`: `QueryAllTenants()`/`EnterTenant`, not `IgnoreQueryFilters()`** — Medium · (DOC-17, R23).
- [ ] **B10-3 · `WAYS_OF_WORKING.md` slice recipe** — Medium · (DOC-18) — `MapTenantFeatureGroup`, `ExportKey`+`ExportAsync`, the `.RequirePermission`/`.RequireEntitlement` filters, the full touchpoint checklist.
- [ ] **B10-4 · `docs/stories/ui.md` (retrospective) + ROADMAP note** — Medium · (DOC-22).
- [ ] **B10-5 · ROADMAP/BACKLOG/FEATURES done-markers** — Medium · (DOC-1/2/3/4/5/6/7/8/9).
- [ ] **B10-6 · ADR amendments** — Low · (DOC-13 ADR-014 EnterTenant + story fix; DOC-14 ADR-016 HOOKS-2; DOC-15 ADR-015 PUBAPI-2; DOC-20 ADR-008(b); DOC-21 ADR-006 stripe-mock; ADR-004 drift per D7/TR-2/7/8).
- [ ] **B10-7 · CLAUDE.md doc-map PUBAPI-2; `TECH_STACK.md` missing packages; QA_TEST_PLAN no-UI list** — Low · (DOC-16/23/19).
- [ ] **B10-8 · 403 message strings "owner or admin"** — Low · (DOC-24) — `HouseholdInvitationsController` + resx.
  - **Exit check (V2-B10):** the auto-loaded manuals compile-if-followed and match code; no "done" marker lies.

## V2-B11 — Enforcement & Definition of Solid (LAST · locks everything)
Add each machine rule as an arch test / analyzer / CI step (backlog `AUDIT_RECONCILIATION.md` §7 E1–E22 + R32/R34/R35/R28/R29/R30 tests).
- [ ] **B11-1 · Arch tests:** R2 (TenantId⇒scoped/allowlist), R4 (controller base), R5 (Features ban both hatches), R6 (MapTenantFeatureGroup), R7/R8 (feature namespace isolation), R9 (no Notes in platform tests), R15 (no ambient UtcNow), R34 (fixture=model), R35 (route/table uniqueness).
- [ ] **B11-2 · Correctness pins:** R28 (MFA replay), R29 (stale webhook), R30 (atomic quota), R17 (fail-open), R32 (write UPDATE/DELETE) — as standing tests.
- [ ] **B11-3 · R12 user-data-contributor coverage test** (after B6).
- [ ] **B11-4 · R13 ExportKey uniqueness test.**
- [ ] **B11-5 · CI doc-sync (R23) + config-key⇄.env (R20) + secret scan (gitleaks) + MailKit-outside-Email ban.**
- [ ] **B11-6 · Supply chain:** R25 (CPM + lockfile + `--locked-mode`), R26 (license scan), R27.
- [ ] **B11-7 · MA0048 file-name analyzer (R24 naming half).**
- [ ] **B11-8 · QA: move guide-PDF generation into CI (deterministic); assert run-log append-only; assert QA plan + PDFs change together.**
- [ ] **B11-9 · Definition of Solid + Standing Instruction:** update `CONTRIBUTING.md` with the new invariants; add the `FOUNDATION_RULES.md`-binding standing instruction to `CLAUDE.md`/`AGENTS.md` (per the suite's STANDING INSTRUCTION block). Decide E2E-in-CI (B8-5).
  - **Exit check (V2-B11):** every machine rule in `FOUNDATION_RULES.md` v1.0 is a green gate; a generated clone inherits them; the doc-only floor (TR-9) is now CI-enforced.

---

## Coverage map (every finding → a task)

- **Critical:** GAP-1 → B1-1/B1-2.
- **High:** SOLID-1→B6-1 · GAP-2→B5-1 · TR-1→B7-1 · DOC-10→B10-1 · SOLID-2→B9-1 · LOGIC-S1→B3 · LOGIC-B1→B4-1 · ADV-1→B2-1.
- **Medium:** LOGIC-B7→B5-3 · LOGIC-B3/GAP-4→B5-4 · LOGIC-B2→B4-2 · SOLID-3→B5-2 · CON-1/GAP-3→B5-1 · ADV-2→B2-2 · DEBT-1..7→B9 · DOC-13/16/17/18/22 + stale markers→B10 · T1(supply)→B11-6 · CON-2→B11-5 · CON-3/DEBT-5→B9-5.
- **Low:** GAP-5→B1-2 · LOGIC-B5/B6/B8, S2/S3→B3-2/B4-2/B9-7 (+D-decisions) · ARCH-1..3, TR-2..9, SOLID-4..8, DEBT-8..12, DOC (low)→B9/B10 · T2/T3/T4→B11-6/B8.
- **Enforcement (R1–R35 machine):** B11.

**Nothing ships until this plan is approved.** On approval: implement V2-B1 first, test-first, verifying each change against `FOUNDATION_RULES.md` v1.0; re-run the Phase-4 adversarial slice pass after B2/B6/B7 (a horizontal-concern change), and before generating the first real app.
