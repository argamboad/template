# Stories — In-app notifications (`NOTIFY`)

> One file per epic. A **per-user** notification center (read/unread) + **per-user delivery preferences**
> (in-app / email), fanned out through the existing **outbox** (ADR-007) so one call reaches a user on
> the channels they chose. Design decision + constraints in **ADR-013**. Stories use Gherkin acceptance
> criteria. **Status: 🔲 in progress.**

**Epic key:** `NOTIFY`

**Prerequisites (external, before any code):**
- None. Reuses the outbox-backed `IEmailSender` (ADR-007 ✅), the per-user pattern (ADR-C2), and account
  erasure (GDPR-2). No new packages.

**Per-user, not tenant-scoped (ADR-C2/ADR-013):** notifications + preferences are keyed by `user_id`
and scoped to the authenticated caller (`NameIdentifier`) — never the tenant filter, never another
user's data. Wiped by account erasure (GDPR-2).

---

### NOTIFY-1 — In-app notification center

**Status: 🔲 Planned.**

**As a** user
**I want** an in-app list of notifications with read/unread state
**So that** I can see what happened without relying on email

**Context / notes:** `Notification` (per-user: `user_id`, `kind`, `title`, `body`, `metadata` jsonb,
`read_at`, `created_at`). `INotificationService.NotifyAsync(userId, kind, title, body, metadata)` creates
the in-app row **transactionally** (in-app only for this slice; the email channel is NOTIFY-2). A
user-scoped center API: list (paginated, newest first), unread count, mark-one-read, mark-all-read — all
scoped to the caller. **Wiped by account erasure** (GDPR-2). No secrets in `metadata`.

**Acceptance criteria**

```gherkin
Scenario: A notification appears in my center
  Given a feature calls NotifyAsync for me
  When I list my notifications
  Then the new one is there, unread, newest first

Scenario: Read state
  Given I have unread notifications
  When I mark one (or all) read
  Then its read_at is set and my unread count drops

Scenario: Notifications are per-user
  Given two users each have notifications
  When I list or mark read
  Then I only ever see/affect my own — never another user's

Scenario: Erasing my account removes my notifications
  Given I have notifications
  When I delete my account (GDPR-2)
  Then they are wiped
```

**Out of scope:** preferences + email fan-out (NOTIFY-2); realtime push/SignalR (a later concern —
polling is fine at template scale); notification templates/i18n beyond a title+body.
**Definition of done:** tests first; create + list (newest-first, paginated), unread count, mark
one/all read, per-user isolation, erasure wipes notifications; merged, app working; ADR-013 referenced.

---

### NOTIFY-2 — Delivery preferences + email fan-out

**Status: 🔲 Planned.**

**As a** user
**I want** to choose whether I'm notified in-app and/or by email
**So that** I control how I'm reached

**Context / notes:** `NotificationPreference` (per-user channel toggles: in-app / email; default on).
`NotifyAsync` becomes the **fan-out**: always considers prefs — in-app row when in-app is on (transactional),
and an **email** via the outbox-backed `IEmailSender` (ADR-007) when email is on. Get/update-preferences
endpoints (user-scoped). A feature calls `NotifyAsync` once; channels are never hard-coded at the call site.

**Acceptance criteria**

```gherkin
Scenario: Fan-out honors preferences
  Given my preferences: in-app on, email off
  When NotifyAsync runs for me
  Then an in-app notification is created and no email is sent
  And with email on, an email is enqueued via the outbox too

Scenario: Defaults
  Given I never set preferences
  Then both channels default to on

Scenario: Update my preferences
  When I update my notification preferences
  Then subsequent NotifyAsync calls respect them

Scenario: Email goes through the reliable path
  Given email is on
  When NotifyAsync sends
  Then it uses the outbox-backed IEmailSender (retried), not an inline send
```

**Out of scope:** per-notification-kind granularity (a single global channel toggle is enough for the
template — extendable later); SMS/push channels; digest/batching.
**Definition of done:** tests first; prefs default-on, get/update, fan-out respects prefs (in-app +
email-via-outbox), email uses the outbox sender; merged, app working; ADR-013 referenced.

---

## Slice plan (implementation map)

Ordered, each a mergeable vertical slice. TDD throughout.

1. 🔲 **In-app center (NOTIFY-1).** `Notification` (per-user) + migration; `INotificationService.NotifyAsync`
   (in-app insert) + a user-scoped center API (list/unread-count/mark-read/mark-all); account erasure wipes
   notifications.
2. 🔲 **Preferences + fan-out (NOTIFY-2).** `NotificationPreference` (per-user, default-on) + get/update API;
   `NotifyAsync` fans out to in-app + email (outbox-backed `IEmailSender`) per prefs.

**Known sharp edges (from ADR-013):** everything is **per-user** (scoped to the caller, never
cross-user); **in-app = transactional DB row, email = outbox** (don't mix them up); **preferences gate
delivery** (no hard-coded channels); notifications are **user PII** (erasure wipes them); **no
secrets/PII** in `metadata`.
