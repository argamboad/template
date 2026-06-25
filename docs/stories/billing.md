# Stories — Billing & Subscriptions

> One file per epic. Adds monetization to the template: a provider-abstracted billing seam
> (`IBillingProvider`), a Stripe reference implementation, plan-tier **entitlements** (feature
> flags keyed to plan) and **quotas** (countable limits). **Status: DEFERRED** — design decision
> and constraints in **ADR-006**; pick this up web-first when there's a paid product to sell.
> Stories use Gherkin acceptance criteria. Billing depends on the outbox/inbox from
> `docs/stories/async-jobs.md` (ADR-007) for reliable webhook processing.

**Epic key:** `BILLING`

**Prerequisites (external, before any code):**
- A **Stripe account** (test mode is enough to build and ship the whole epic — no real charges).
- The **Stripe CLI** installed for local webhook forwarding (`stripe listen --forward-to
  https://localhost:7160/api/billing/webhook`) and event simulation (`stripe trigger`).
- A defined **plan catalog** — at least Free + one paid tier — with Stripe Product/Price ids. The
  plan catalog is code/config, **not** tenant data (see ADR-006).
- The **outbox/inbox** slice (`JOBS-1`/`JOBS-2`) merged first — webhook idempotency rides on it.
- Package: `Stripe.net` (latest stable, .NET 10 — no previews, ADR-C10).

**Billing without real money — the sandbox/fake stack (ADR-006):**
| Layer | Tool | Used by |
|-------|------|---------|
| Unit | `FakeBillingProvider` (in-memory, implements `IBillingProvider`) | `Core.Tests`, `Api.Tests` handler logic |
| Integration | **stripe-mock** (Stripe's official mock server, offline) | `Api.Tests` — request/response shape, no network |
| E2E / manual | **Stripe test mode + Stripe CLI** (`listen` / `trigger`) | `E2E.Tests`, local QA — real webhook round-trip |
| Lifecycle | **Stripe Test Clocks** | trial-end → renewal → dunning, simulated deterministically |

This is the Mailpit-for-billing analogue (ADR-C13): traps everything locally, zero real cost.

---

### BILLING-1 — Plan catalog + entitlement gate

**As a** product owner
**I want** features gated by the tenant's plan tier
**So that** paid capabilities are reserved for paying tenants and I can sell tiers

**Context / notes:** foundation slice — no payment yet, just the access seam. A `Subscription`
projection (ADR-006) defaults every tenant to **Free** (fail-closed) until BILLING-2 attaches a
paid plan. `IEntitlementService.Has(tenant, "feature-key")` reads the plan from the subscription
projection; a `RequireEntitlement("feature-key")` endpoint filter mirrors
[`MapTenantFeatureGroup`](../../src/Api/Features/FeatureEndpointExtensions.cs) so a slice gates
itself declaratively. Entitlement checks are **server-side only** — never trust the client.

**Acceptance criteria**

```gherkin
Scenario: Free tenant is denied a pro-gated endpoint
  Given I am the owner of a tenant on the Free plan
  When I call an endpoint gated by RequireEntitlement("pro-feature")
  Then I receive 402 Payment Required (not 403/500)
  And the response names the entitlement and a link to upgrade

Scenario: Pro tenant is allowed the same endpoint
  Given my tenant has an active Pro subscription
  When I call the pro-gated endpoint
  Then the request is allowed and handled normally

Scenario: Missing or expired subscription falls back to Free (fail-closed)
  Given my tenant has no subscription row, or a past_due/canceled one
  When an entitlement is checked
  Then the tenant is treated as Free, never as the higher tier
```

**Out of scope:** taking payment (BILLING-2); quotas/counters (BILLING-5).
**Definition of done:** tests first (TDD); `IEntitlementService` + the fail-closed default unit-tested;
the `RequireEntitlement` filter integration-tested (Free→402, Pro→200); tenant-scoping verified;
merged, app working; ADR-006 referenced.

---

### BILLING-2 — Subscribe via Stripe Checkout

**As a** tenant owner
**I want** to upgrade my tenant to a paid plan
**So that** my tenant unlocks paid features

**Context / notes:** money mutations happen **on Stripe**, never in our forms — we redirect to
**Stripe Checkout** and store no card data (PCI scope stays SAQ-A, ADR-006). The handler creates a
Checkout Session via `IBillingProvider.CreateCheckoutSession(tenant, priceId)` and returns its URL;
our `Subscription` row only becomes Active when the resulting **webhook** lands (BILLING-3), not on
the redirect back. Only the **owner** may purchase (`TenantRoles.Owner`).

**Acceptance criteria**

```gherkin
Scenario: Owner starts a checkout for the Pro plan
  Given I am the owner of a Free-plan tenant
  When I choose "Upgrade to Pro"
  Then I am redirected to a Stripe Checkout session for the Pro price
  And the session carries my tenant id so the webhook can reconcile it

Scenario: Non-owner cannot purchase
  Given I am a member (not owner) of the tenant
  When I attempt to start a checkout
  Then I receive 403 and no Checkout session is created

Scenario: Checkout success does not grant access until the webhook confirms
  Given I completed Stripe Checkout
  When I return to the app before the webhook is processed
  Then my tenant is still Free until the subscription.created/updated event is applied
```

**Out of scope:** webhook processing (BILLING-3); portal/cancel (BILLING-4).
**Definition of done:** tests first; owner-only guard + tenant-id propagation unit-tested; the
`FakeBillingProvider` drives handler tests, `stripe-mock` the integration test; tenant-scoping
verified; merged, app working.

---

### BILLING-3 — Webhook keeps the subscription projection current

**As a** the platform
**I want** Stripe webhooks to drive our subscription state
**So that** entitlements reflect reality (paid, past_due, canceled) without polling

**Context / notes:** Stripe is the **system of record for money**; our DB holds a **projection**
(ADR-006). The webhook endpoint verifies the Stripe signature, then hands the event to the **inbox**
(`JOBS-2`) for at-least-once **idempotent** processing — dedupe by Stripe event id, tolerate
out-of-order delivery. This endpoint is **unauthenticated** (Stripe calls it) but signature-gated,
and is rate-limit-exempt from the per-IP passwordless policy. Applying an event upserts the
`Subscription` (status + current_period_end + plan).

**Acceptance criteria**

```gherkin
Scenario: subscription.created activates the tenant's plan
  Given a valid signed customer.subscription.created event for my tenant's Pro price
  When the webhook is processed
  Then my tenant's Subscription projection is Active on Pro
  And pro-gated endpoints become allowed

Scenario: Duplicate delivery is ignored (idempotent)
  Given an event id that has already been processed
  When the same event is delivered again
  Then it is acknowledged with no duplicate state change

Scenario: Invalid signature is rejected
  Given a webhook payload with a bad or missing Stripe signature
  When it hits the endpoint
  Then it is rejected 400 and nothing is applied

Scenario: subscription.deleted / past_due downgrades to Free (fail-closed)
  Given my tenant was Active on Pro
  When a canceled or past_due event is applied
  Then entitlements fall back to Free
```

**Out of scope:** the inbox mechanism itself (JOBS-2 owns it); dunning emails (BILLING-6).
**Definition of done:** tests first; signature verification + idempotency + each state transition
unit/integration-tested with `stripe-mock` and `stripe trigger`; tenant reconciliation (event →
correct tenant) verified; merged, app working.

---

### BILLING-4 — Manage subscription via Customer Portal

**As a** tenant owner
**I want** to change or cancel my plan
**So that** I control my own billing without contacting support

**Context / notes:** reuse **Stripe Customer Portal** (a redirect, like Checkout) rather than
building plan-change/cancel UI. `IBillingProvider.CreatePortalSession(tenant)` returns the URL; all
resulting changes flow back through BILLING-3's webhook. Owner-only.

**Acceptance criteria**

```gherkin
Scenario: Owner opens the billing portal
  Given I am the owner of a tenant with a Stripe customer
  When I click "Manage billing"
  Then I am redirected to the Stripe Customer Portal for my customer

Scenario: Cancellation propagates via webhook
  Given I cancel my subscription in the portal
  When Stripe sends the resulting subscription.updated/deleted event
  Then my tenant downgrades to Free per BILLING-3
```

**Out of scope:** proration math (Stripe owns it); invoices UI.
**Definition of done:** tests first; owner-only guard tested; portal session creation tested via
`FakeBillingProvider`/`stripe-mock`; merged, app working.

---

### BILLING-5 — Seat & usage quotas

**As a** the platform
**I want** plan-tier quotas enforced (e.g. seats, metered usage)
**So that** tiers have teeth beyond feature on/off

**Context / notes:** quotas are **per-tenant, persisted counters** — a different mechanism from the
per-IP request throttle in [`RateLimiting.cs`](../../src/Api/Configuration/RateLimiting.cs) (don't
conflate them, ADR-006). **Seats** = count of `TenantMembership` for the tenant vs the plan's seat
limit; checked when inviting (extends
[`HouseholdInvitationsController`](../../src/Api/Controllers/HouseholdInvitationsController.cs)).
**Metered usage** = an incrementing counter checked by `IQuotaService.TryConsume(tenant, key, n)`.

**Acceptance criteria**

```gherkin
Scenario: Inviting beyond the seat limit is blocked
  Given my Free plan allows 3 seats and my tenant already has 3 members
  When I invite a 4th member
  Then the invite is rejected 402 with an upgrade prompt
  And no TenantInvitation is created

Scenario: Upgrading raises the seat limit
  Given my tenant upgrades to Pro (10 seats)
  When I invite a 4th member
  Then the invite succeeds

Scenario: Metered action is denied once the quota is exhausted
  Given my plan's monthly quota for "export" is 5 and I have used 5
  When I attempt a 6th export
  Then it is denied 402 and the counter is not incremented
```

**Out of scope:** billing for overages (metered/usage-based pricing is a later slice);
quota-reset scheduling lives in `JOBS-3`.
**Definition of done:** tests first; seat-count and `IQuotaService` logic unit-tested at boundaries
(at-limit, over-limit, after-upgrade); tenant-scoping verified; merged, app working.

---

### BILLING-6 — Trial & dunning lifecycle — DEFERRED within this epic

**As a** product owner
**I want** trials and failed-payment (dunning) handling
**So that** I can offer trials and recover failed renewals

**Context / notes:** lifecycle transitions (trial → active → past_due → canceled) are driven by
Stripe and validated with **Stripe Test Clocks** (fast-forward simulated time — ADR-006). Dunning
**emails** ride the outbox (`JOBS-1`); trial-expiry **sweeps** ride scheduled jobs (`JOBS-3`).

**Acceptance criteria:** _to be written when this slice is undeferred (use Test Clocks to script the
timeline)._
**Out of scope:** everything until BILLING-1..3 ship.
**Definition of done:** n/a while deferred.

---

## Slice plan (implementation map — when undeferred)

Ordered, each a mergeable vertical slice. TDD throughout (write the failing test first). **JOBS-1/2
must land first** (ADR-007) — billing webhooks depend on the inbox.

1. **Seam + plan catalog + entitlement gate (BILLING-1).**
   - `Core/Abstractions/IBillingProvider.cs` (CreateCheckoutSession, CreatePortalSession,
     ParseWebhookEvent) — mirrors `IEmailSender`'s Core-abstraction shape.
   - `Core/Entities/Subscription.cs : ITenantScoped` (plan key, status, stripe customer/subscription
     ids, current_period_end) + EF config + migration. Default-absent ⇒ Free.
   - Plan catalog as code/config (`PlanCatalog` static or `Billing:Plans` config): plan key →
     entitlements + seat/usage limits + Stripe price id.
   - `IEntitlementService` + `RequireEntitlement(key)` endpoint filter (sibling of
     `FeatureEndpointExtensions`). 402 on deny.
   - `FakeBillingProvider` (Infrastructure or test double) for unit tests.
   - **Tests first:** entitlement fail-closed default; filter Free→402 / Pro→200.
2. **Stripe reference impl + Checkout (BILLING-2).**
   - `Infrastructure/Billing/StripeBillingProvider.cs` (`Stripe.net`); register in
     `ServiceCollectionExtensions` guarded by `Billing:Stripe:SecretKey` presence (same
     config-presence pattern as the OAuth providers).
   - `Features/Billing/` slice: `MapTenantFeatureGroup("/api/billing")`, owner-only checkout handler.
   - `.env.example`: `Billing__Stripe__SecretKey`, `Billing__Stripe__WebhookSecret`, price ids.
   - **Tests first:** owner-only; tenant-id in session metadata; `stripe-mock` integration.
3. **Webhook + projection (BILLING-3).** Unauthenticated signed endpoint → inbox (JOBS-2) →
   idempotent apply. **Tests first:** signature reject, dedupe, each transition (drive with
   `stripe trigger`).
4. **Customer Portal (BILLING-4).** Portal redirect; changes reconcile via the webhook from step 3.
5. **Quotas (BILLING-5).** `IQuotaService` + seat check wired into the invitation path; a
   `BillingDataContributor : ITenantDataContributor` that cancels the Stripe subscription and wipes
   the projection on tenant dissolve.
6. **Trial/dunning (BILLING-6).** Undeferred later; Test Clocks + JOBS-1 (emails) + JOBS-3 (sweeps).

**Known sharp edges (from ADR-006):** webhooks are at-least-once and out-of-order (idempotency is
mandatory — needs JOBS-2); never grant access on the Checkout redirect, only on the webhook; the DB
is a **projection**, Stripe is the source of truth for money; entitlement checks are server-side and
fail-closed; quotas ≠ rate limits. Budget for Stripe dashboard setup (products/prices/webhook
endpoint), not just code.
