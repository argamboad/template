# Stories — E2E journey coverage for platform UI (`E2E`)

> One file per epic. Closes the Playwright gap for platform epics that shipped real UI but were
> built unit/integration-first: **RBAC roster management**, the **billing seat-quota** surface, and
> the **notification center/preferences**. Deliberately selective — headless machinery (webhooks,
> outbox, health checks, Stripe money paths) stays at the integration layer where it's already
> covered; `/health` liveness belongs to the DEPLOY-3 smoke step, not a browser test. Stories use
> Gherkin acceptance criteria. **Status: 📝 planned.**

**Epic key:** `E2E`

**Prerequisites (before any code):**
- **v2 audit remediation approved/landed first** (`docs/audits/v2-2026-07/AUDIT_TASKS.md`) — these
  journeys drive auth/tenancy surfaces the remediation may touch; don't write them against code
  about to change.
- No new packages, no new services. Reuses the existing harness: `E2ETestBase`, the Page Object
  Model (`tests/E2E.Tests/Pages/`), Mailpit (`Mailpit.cs`) for OTP sign-in and invitation emails,
  and the running dev stack per `tests/E2E.Tests/README.md`.

**Scope guardrails (what this epic deliberately does NOT do):**
- **No Stripe.** Checkout/Portal round-trips stay covered by
  `tests/Api.Tests/Billing/*` (webhook handler, entitlements, dunning). There is **no billing UI
  page** in the Web app today; the only billing-visible UI is the seat-limit message on the invite
  flow — that is the billing E2E surface. Building a billing page is a separate (unscheduled) slice.
- **No health/observability browser tests.** `/health` verification is the DEPLOY-3 deploy-smoke
  concern (ADR-017).
- **No E2E for config-gated-off surfaces** (PUBAPI, HOOKS, ADMIN console) or destructive GDPR
  flows — integration tests + `docs/QA_TEST_PLAN.md` cover them.

**Conventions that apply to every story below:**
- TDD: write the failing Playwright test first; the failure drives adding the missing
  `data-testid` hooks to the Shared.Ui components (the existing pages — e.g. `Household.razor` —
  have none yet). Selectors use `data-testid` only, per `tests/E2E.Tests/README.md`.
- Multi-user scenarios use separate Playwright browser contexts (owner + member), each signing in
  via email OTP read from Mailpit — same pattern as `AuthFlowTests`.
- Each test run creates fresh users/households (unique emails) so tests don't depend on reset
  state, matching the existing suite.
- Update the coverage table in `tests/E2E.Tests/README.md` and map each journey to its
  `docs/QA_TEST_PLAN.md` IDs as part of the slice.

---

### E2E-1 — RBAC roster journey

**As a** household owner
**I want** the roster UI (invite → join → promote/demote → remove) verified in a real browser
**So that** permission-sensitive UI regressions are caught before they ship

**Context / notes:** Drives `Household.razor` end-to-end. The client-side permission mirror
(`CanManageRoles` = owner only, `CanManageMembers` = owner+admin — ADR-009) shows/hides controls;
the API is the real gate (already integration-tested in `Rbac/*` + `RbacForbiddenIntegrationTests`),
so the E2E asserts the **UI reflects the role**, not the 403s. Invitation email arrives in Mailpit;
the member joins via the `/join` page with the token. New test file
`tests/E2E.Tests/RosterJourneyTests.cs` + `Pages/HouseholdPage.cs`, `Pages/JoinPage.cs`.

**Acceptance criteria**

```gherkin
Scenario: Owner invites a member who joins via the emailed token
  Given a signed-in owner on the Household page
  When the owner invites member@example.test
  Then the pending invitation appears in the list
  And a second browser context signs in as member@example.test and joins via /join with the token
  And the owner's reloaded roster shows the new member with the Member badge

Scenario: Owner promotes and demotes a member
  Given a household with an owner and a member
  When the owner clicks Make admin on the member
  Then the member's badge changes to Admin
  And clicking Make member changes it back

Scenario: The roster is permission-aware for a non-owner
  Given the member (role: member) opens the Household page in their own context
  Then no rename field, invite form, promote/demote, or remove controls are visible
  And after promotion to admin (by the owner), a reload shows invite + remove but NOT promote/demote

Scenario: Owner removes a member
  Given a household with an owner and a member
  When the owner removes the member (confirming the dialog)
  Then the member disappears from the roster
```

**Out of scope:** ownership transfer + dissolve (destructive; QA plan covers them manually);
API-level 403 assertions (integration-tested); invitation regenerate/revoke edge cases.
**Definition of done:** tests written first; `data-testid` hooks added to `Household.razor` roster
controls; all scenarios green against the local stack; README coverage table + QA plan mapping
updated; merged, app working.

---

### E2E-2 — Billing seat-quota journey

**As a** household owner on the free plan
**I want** the seat-limit experience verified in a real browser
**So that** the quota → 402 → upgrade-prompt UX (BILLING-5) can't silently regress

**Context / notes:** The free plan's `SeatLimit` is **3** (`src/Core/Billing/PlanCatalog.cs`), and
seats = members + pending invites (a pending invite reserves a seat — `QuotaService`). So a fresh
household (owner = 1 seat) hits the limit after 2 pending invites, entirely from the browser, with
**no Stripe involvement**: the 3rd invite returns 402 `seat_limit_reached` and `Household.razor`
shows the localized `Household_ErrSeatLimit` upgrade message. Revoking an invite frees the seat.
New test file `tests/E2E.Tests/SeatQuotaJourneyTests.cs` (reuses `HouseholdPage` from E2E-1).

**Acceptance criteria**

```gherkin
Scenario: Inviting past the free-plan seat limit shows the upgrade prompt
  Given a fresh household (owner only, free plan)
  When the owner sends invitations to two distinct emails
  Then both appear as pending invitations
  When the owner invites a third email
  Then the seat-limit message is shown ("Upgrade your plan…")
  And no third pending invitation appears

Scenario: Revoking a pending invitation frees the seat
  Given the household is at the seat limit via pending invitations
  When the owner revokes one pending invitation
  Then inviting a new email succeeds and appears as pending
```

**Out of scope:** Checkout/Portal/upgrade flows (no billing UI page exists; the money path is
integration-tested); usage quotas other than seats; trial/dunning banners (no UI surface).
**Definition of done:** tests written first; `data-testid` hooks on the invite form, pending list,
and status alert; scenarios green; README + QA plan mapping updated; merged, app working.

---

### E2E-3 — Notification center & preferences journey

**As a** user
**I want** the notification bell and delivery preferences verified in a real browser
**So that** the NOTIFY UI (bell, prefs card) can't silently break

**Context / notes:** Covers `NotificationBell.razor` (header) and `NotificationPrefsCard.razor`
(Settings). **Constraint:** the only production caller of `NotifyAsync` is `BillingNotifier`
(trial/dunning), which is not browser-triggerable — so "a notification appears and can be marked
read" stays at the integration layer (`Notify/*` tests), and the E2E asserts the UI shell +
preferences round-trip. If a future feature emits user-triggerable notifications, extend this
journey then. New test file `tests/E2E.Tests/NotificationJourneyTests.cs` + prefs section on
`Pages/SettingsPage.cs`.

**Acceptance criteria**

```gherkin
Scenario: The bell renders with an empty center for a fresh user
  Given a freshly signed-in user
  When they open the notification bell
  Then the dropdown shows the empty state and no unread badge

Scenario: Delivery preferences round-trip
  Given the Settings page notification preferences card (both channels default on)
  When the user turns the email channel off
  And reloads the page
  Then the email toggle is still off and in-app is still on

Scenario: Preferences are per-user
  Given user A turned email off
  When user B (fresh) opens Settings in their own context
  Then user B's channels are both on
```

**Out of scope:** asserting notification creation/mark-read through the browser (no
browser-triggerable producer — integration-tested); realtime/badge-count updates.
**Definition of done:** tests written first; `data-testid` hooks on the bell + prefs card;
scenarios green; README + QA plan mapping updated; merged, app working.

---

## Slice plan (implementation map)

Ordered, each a mergeable vertical slice. TDD throughout — the failing Playwright test drives the
`data-testid` additions (the only production-code changes this epic should need).

1. 📝 **RBAC roster journey (E2E-1).** Page objects `HouseholdPage`/`JoinPage`, testid hooks on
   `Household.razor`, multi-context invite→join→promote→remove journey. Biggest value: the most
   permission-sensitive UI in the template.
2. 📝 **Seat-quota journey (E2E-2).** Reuses `HouseholdPage`; free-plan limit (3) hit via pending
   invites; asserts the 402 upgrade prompt without touching Stripe.
3. 📝 **Notification journey (E2E-3).** Bell empty state + prefs persistence, per-user isolation.

**Known sharp edges:**
- **Selectors are `data-testid`-only** — the touched components don't have hooks yet; adding them
  is part of each slice, not a separate refactor.
- **Seats count pending invites** — E2E-2 depends on that `QuotaService` rule; if the seat rule
  changes, this journey is the canary.
- **Free-plan `SeatLimit: 3` is an EXAMPLE quota** (`PlanCatalog.cs` says "tune per app") — the
  test should read failure gracefully: if a downstream app retunes the catalog, the test's invite
  count must follow. Keep the limit referenced in one constant in the test file.
- **No notification producer is browser-reachable** — don't be tempted to add a test-only endpoint
  to fake one; the integration tests own that behavior.
- **English-locale assertions only** — the suite runs in EN (matching `I18nTests`' approach of
  testing the switcher separately); don't assert localized strings in journey tests, use testids.
