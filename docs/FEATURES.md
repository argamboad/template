# Features & User Flows

> How the product behaves, flow by flow. The "why/what" lives in `PROJECT_BRIEF.md`; structures
> live in `DATA_MODEL.md`. Fill one section per major flow. Pattern shown below.

## Flow template (copy per flow)

### N. <Flow name>
**Goal:** <what the user is trying to accomplish>

Flow:
1. <step>
2. <step>

Notes: <edge cases, derived-rule references, tenant-scoping considerations>

---

## Constant flows (most multi-tenant SaaS apps need these — adapt as needed)

### Auth & tenant onboarding
**Goal:** a new user signs up and lands in a tenant (creates one or joins an existing one).
- Sign-up / sign-in via ASP.NET Core Identity.
- New tenant creation vs. invitation into an existing tenant — _TODO: which does this app need?_
- First-run experience so the app isn't empty on arrival — _TODO: app-specific onboarding._

### User & tenant settings
**Goal:** manage per-user preferences (only preferences are per-user) and tenant-level settings.
- _TODO: what preferences? what tenant settings?_

---

## App-specific flows
<!-- The heart of the product. One subsection per flow, using the template above. -->
_TODO_

## Flow-to-rule cross-reference
<!-- Map each flow to the derived rule(s) in DATA_MODEL.md it depends on. -->
| Flow | Key derived rule |
|------|------------------|
| _TODO_ | _TODO_ |

## Out of scope
See the OUT list in `PROJECT_BRIEF.md`.
