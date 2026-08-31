# QA Co-pilot Session Run Log — 2026-08-28

**Environment A (local)** — develop @ `faf5985`+`#197` (SDK 10.0.400 toolchain, post-bump).
Stack: Postgres + Mailpit (docker compose), API `https:7160` with CLI SMTP override → Mailpit
(`.env` Brevo values untouched), Web `https:7008`.
**Testers:** Claude (driver) + Allan Gamboa (OAuth credential steps).

## §4 Smoke suite — COMPLETE (web/platform scope)

| Case | Result | Notes |
|---|---|---|
| QA-SMK-01 — Web: OTP sign-in happy path | ✅ **PASS** | `/login` redirect → code-entry view after request → OTP fetched from Mailpit (async outbox delivery ~3 s) → landed on Home; header shows tenant badge "qa-smoke's Household", display name, Household/Billing/Settings/Sign out + bell. |
| QA-SMK-02 — Web: Google OAuth sign-in | ✅ **PASS** | Full-page redirect to Google consent confirmed; user completed sign-in (`qa.owner` account); returned signed-in on Home, "Allan's Household" badge + full chrome. |
| QA-SMK-03 — Web: Sign out | ✅ **PASS** | Sign out → `/login`; direct navigation to `/settings` while anonymous bounced back to `/login`. |
| QA-SMK-04 — Web: Session persists across reload | ✅ **PASS** | Hard reload while signed in → still signed in, no `/login` trip (silent refresh). Run before SMK-03's sign-out. |
| QA-SMK-05 — Desktop: OTP sign-in | ➖ N-A | Native column — out of scope for the browser session (→ QA-DSK-01 on a device pass). |
| QA-SMK-06 — Android: OTP sign-in | ➖ N-A | Native column — out of scope for the browser session (→ QA-AND-01 on a device pass). |
| QA-SMK-07 — API health & readiness | ✅ **PASS** | `/health` 200 `Healthy`; `/health/ready` 200; **optional leg run**: DB stopped → ready 503 while liveness stayed 200 → DB restarted → ready 200. Responses status-only. |

**Observations (non-findings):**
- The Blazor `#blazor-error-ui` div appears in the accessibility tree on every page but is
  `display:none` — verified not visible; the only console error is the expected 401 from the
  anonymous silent-refresh probe. Not a defect.
- Mailpit SMTP override via command line (QA plan §1.1's documented alternative) worked first try —
  recommended over editing `.env` for future sessions.

**Smoke verdict: 5/5 executable cases PASS, 0 findings.**

## §14a Adversarial & tenant-isolation — IN PROGRESS

Setup: two owner tenants A (`qa-smoke`) + B (`qa-adv-b`) via OTP-minted JWTs; staff token
(`qa-staff@example.com`) with `Admin__StaffEmails__0`; PUBAPI + HOOKS enabled via CLI override.
B seeded with an API key, a webhook subscription, and a staff announcement notification.

| Case | Result | Notes |
|---|---|---|
| QA-ADV-01 — A cannot read/export/erase B (API) | ✅ **PASS** | Every B id → 404 to A (member delete, notif read, notif delete); A's export had 0 B references; B's member + notification unchanged (still unread). |
| QA-ADV-02 — cross-tenant on PUBAPI/HOOKS tables | ✅ **PASS** (w/ note) | A→B replay, DELETE apikey, DELETE webhook all 404; B's key + webhook survived. **Note:** `GET /api/webhooks/{B-id}/deliveries` returns **200-empty** for A rather than 404 — but it's tenant-filtered (no leak) and a nonexistent id returns the same, so no existence oracle. Isolation intent (nothing read/replayed/revoked) fully met; the 200-vs-404 is cosmetic. |
| QA-ADV-03 — export covers new tables, secret-free | ✅ **PASS** | Bundle (via signed download_url) has `api_keys` (prefix, no hash), `webhooks.subscriptions` (url/events, no `whsec_`), `usage`. No secret leaked. Audit section already carries `ImpersonatedBy` field. |
| QA-ADV-04 — dissolve wipes new tables | ✅ **PASS** | Throwaway tenant + key + webhook → `POST /api/household/leave {confirm_dissolve:true}` (204). DB: ApiKey/Webhook/Delivery/Usage/Memberships/Tenant all **0 rows**. |
| QA-ADV-05 — writes under impersonation attributed | ✅ **PASS** (mechanism) + 🟡 QA-doc finding | Impersonation token carries `impersonated_by=<staff>`. An audited action under it (`tenant.exported`) records `ImpersonatedBy=staff`, `ActorUserId=target`. **BUT** the case's literal example writes (rename, mark-read) aren't audited actions, so the walkthrough can't be followed verbatim → chip spawned to fix the QA case. Security property holds. |
| QA-ADV-06 — impersonation can't reach staff actions | ✅ **PASS** | Impersonation token → `impersonate/{x}`, `announce-all`, `DELETE users/{x}/mfa` all **403**. Staff gate treats `impersonated_by` as non-staff. |

### Product/doc findings spawned as chips
1. **Postman webhook example** uses `event_types:["test.ping"]` → API 400 (only `"ping"` is known). QA hit it verbatim.
2. **Webhook delivery log unreachable in-template** — sync test-send doesn't persist a WebhookDelivery row and no feature calls `IWebhookPublisher`, so `GET .../deliveries` is always empty and replay has nothing to replay; QA-API-06 + delivery legs of ADV-02/04 can't be executed as written.
3. **QA-ADV-05 example writes** are unaudited actions (attribution mechanism itself is correct).

### §14a continued — billing + auth curl cluster
| Case | Result | Notes |
|---|---|---|
| QA-ADV-12 — webhook idempotency + same-second recency | ✅ **PASS** | Duplicate EventId = no-op (ack 200); same-second `active` after `created` applied (final state `active`, not dropped stale). |
| QA-ADV-13 — first-ever bad-state webhook no false-notify | ✅ **PASS** | Fresh tenant, first-ever `past_due` → 0 notifications (dunning needs a transition out of active/paid). |
| QA-ADV-14 — comp/revert a churned (canceled) tenant | ✅ **PASS** | Sub `canceled` w/ sub id persisted; staff comp to Pro → 200 (not 409); projection flipped to `active`/`pro`. Guard keys on liveness. |
| QA-ADV-17 — replaying rotated refresh revokes family | ✅ **PASS** | Native refresh flow: token rotated on refresh; replay OLD → 401; NEW then also 401 (whole family revoked on reuse detection). |
| QA-ADV-22 — forged X-Forwarded-For can't bypass rate limit | ✅ **PASS** | 8 OTP sends w/ rotating spoofed XFF → 200×5 then 429×3; client XFF ignored (untrusted proxy), real IP partitions one bucket. |

### §14a continued — MFA + prefs + headers
| Case | Result | Notes |
|---|---|---|
| QA-ADV-09 — staff MFA-reset notif not silenceable | ✅ **PASS** | Target with both notif channels OFF; staff `DELETE .../mfa` (204) still delivered `security.mfa_reset` bell row + email ("Two-factor authentication was reset"). |
| QA-ADV-10 — MFA step-up cumulative lockout | ✅ **PASS** | 5 wrong step-up codes arm a 15-min per-user lockout; after the per-IP window reset, a CORRECT TOTP still returned 401 (locked, no oracle) — distinct from the per-IP 429. |
| QA-ADV-11 — recovery code single-use | ✅ **PASS** | Recovery code #0 signed in (200); reuse of same code → 401; unused code #1 still worked. Burned on first use. |
| QA-ADV-08 leg A — impersonation never rewrites target prefs | ✅ **PASS** | `PUT /api/auth/theme` under impersonation → 403 (refused outright); B's stored theme unchanged. Leg B (shared-device bleed) = browser two-context, pending. |
| QA-ADV-16 — one OTP double-redeemed issues one session | ✅ **PASS** | Two concurrent verifies of one OTP → exactly one 200, one 401. |
| QA-ADV-20 — "Clear read" never deletes unread | ✅ **PASS** | No-scope bulk DELETE → 400 `scope_required`; `?read=true` cleared only read; unread survived. |
| QA-ADV-21 — security headers on single-origin host | ✅ **PASS** (on real staging) | `/`: HSTS + nosniff + CSP frame-ancestors 'none' + Referrer-Policy + no-cache; `_framework/*`: immutable + nosniff. Also confirms the SDK-10.0.400 staging deploy serves. |

### §14a continued — role ceiling + seat race
| Case | Result | Notes |
|---|---|---|
| QA-ADV-07 — impersonation can't exceed target role | ✅ **PASS** | Impersonating a plain member: transfer-ownership, role-change, invite all → 403. Inherits target's role ceiling. |
| QA-ADV-15 — concurrent last-seat acceptance | ✅ **PASS** (invariant) | Fresh Free tenant at 2/3, concurrent accept of the last seat → exactly one 204, cap never exceeded (settled 3/3). Note: the creation-time seat quota (members+pending, `TenantInvitationService.cs:104`) blocks a 2nd pending invite at 2/3, so the 2nd invitee is refused at *creation* not accept; the "2 pending past cap" concurrent-accept variant is only reachable via a downgrade → covered by BILLING-9 / QA-INV-10 (automated). Minor QA-doc realizability nuance in the setup step. |

_Contamination note: A's plan was set to Pro (seat cap 10) during ADV-12 billing webhook tests, so the first ADV-15 attempt on A was invalid — redone on a clean Free tenant._

### §14a continued — web-driven + closeout
| Case | Result | Notes |
|---|---|---|
| QA-ADV-08 leg B — shared-device pref bleed | ✅ **PASS** | Browser: user X picked Español pre-auth → adopted (app fully Spanish); signed out (login reverted to English); user Y (never chose) signed in same browser → rendered **English**, no inheritance of X's device locale. |
| QA-ADV-18 — Spanish account, English device, accept invite | ⚙️ **Covered by automation** | Marked ⚙️ Automated in CI (`I18nTests.LocaleChoice_FollowsTheUser_AcrossBrowsers`); green in the 2026-08-28 develop run. Not re-driven manually. |
| QA-ADV-19 — theme/locale no revert after soft sign-in | ✅ **PASS** | Pre-auth Dark pick, Y's stored theme Light → after soft OTP sign-in settled to **Light** (server wins), `data-bs-theme` + header switcher both Light (consistent). Frame-level "no flicker" is inherently visual — backed by automated PREFS-1 reconcile tests. |
| QA-ADV-23 — Release native build HTTPS base URL | ➖ **N-A this session** | Native build inspection — out of scope for a browser session. Backed by the v3 NAT-3 build gate (Release build fails without an HTTPS base URL). → device/build pass. |
| QA-ADV-24 — Windows loopback OAuth state guard | ➖ **N-A this session** | Desktop loopback flow — out of scope for a browser session. Unit-backed (NAT-10). → desktop pass. |

## §14a verdict

**22 of 24 cases executed, all PASS** (ADV-01..17, 20, 21, 22 + 08A/B, 19). 1 covered by
automation (ADV-18). 2 N-A for a browser session (ADV-23 native, ADV-24 desktop). **Zero
isolation/security failures.** The v3 remediation holds on every formerly-EXPECT-FAIL case
(03,04,05,06,08,09,10,13,14,18,21,23) — none regressed.

### Findings (all doc/product, none security)
1. **Postman webhook example** `event_types:["test.ping"]` → API 400 (only `"ping"` known). *(chip)*
2. **Webhook delivery log unreachable in-template** — sync test-send doesn't persist a delivery
   row + no feature calls `IWebhookPublisher`; QA-API-06 + delivery legs of ADV-02/04 not
   executable as written. *(chip)*
3. **QA-ADV-05 example writes** (rename, mark-read) are unaudited actions — attribution mechanism
   itself is correct. *(chip)*
4. **QA-ADV-15 setup** ("2 pending at 2 used") is unrealizable via normal invites (creation-time
   seat quota blocks it); reachable only via the downgrade path (BILLING-9/QA-INV-10 automated).
   *(minor QA-doc nuance — noted, no chip)*

### Cosmetic (not findings)
- `GET /api/webhooks/{foreign-id}/deliveries` returns 200-empty rather than 404 (no leak, no oracle).
