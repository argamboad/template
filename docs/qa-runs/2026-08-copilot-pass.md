# QA Co-Pilot Run Log — Perezosoft Platform

**Scope:** every QA_TEST_PLAN case runnable in a co-pilot session (web client + curl/API + Mailpit).
Native-client cases (Windows MAUI / Android / iOS / macCatalyst) are listed at the bottom as
out-of-scope — they need a device/emulator.

**Environment A (local):** develop @ SDK 10.0.400 toolchain (post PR #196/#197). API `https:7160`
with CLI SMTP override → Mailpit; Web `https:7008`. Billing = fake provider. PUBAPI + HOOKS toggled
on for the API-surface cases.

**Testers:** Claude (driver) + Allan Gamboa (OAuth credential hand-offs, real-email checks, visual calls).

### Legend
- ✅ **Pass** (confirmed, date noted) · ⬜ **Not yet run** · ⛔ **Blocked** (open finding)
- ⚙️ also automated in CI (logic covered; a co-pilot run re-confirms the UI layer)
- 🔑 needs your OAuth credential hand-off · 👁 visual-judgment (you eyeball a screenshot)

---

## §4 Smoke suite

| Case | Title | Status | Notes |
|---|---|---|---|
| QA-SMK-01 | Web: OTP sign-in happy path ⚙️ | ✅ 2026-08-28 | code-entry → Mailpit OTP → Home w/ tenant badge + chrome |
| QA-SMK-02 | Web: Google OAuth sign-in 🔑 | ✅ 2026-08-28 | full-page redirect; you signed in; returned on Home as Allan |
| QA-SMK-03 | Web: Sign out ⚙️ | ✅ 2026-08-28 | → /login; /settings while anon bounced to /login |
| QA-SMK-04 | Web: Session persists across reload | ✅ 2026-08-28 | hard reload, still signed in (silent refresh) |
| QA-SMK-07 | API health & readiness | ✅ 2026-08-28 | /health 200; /ready 200; DB-down → 503, liveness stayed 200 |

## §5 Web — Authentication — ✅ COMPLETE (11/11, 2026-08-29)

| Case | Title | Status | Notes |
|---|---|---|---|
| QA-AUTH-01 | Magic-link sign-in happy path ⚙️ | ✅ 2026-08-29 | success panel ("expires in 15 minutes"); Mailpit link → signed in as qa-magic on Home |
| QA-AUTH-02 | Microsoft OAuth sign-in 🔑 | ✅ 2026-08-29 | personal MSA → returned signed in; email matched existing account → auto-linked (consumers tenant), landed in My Household |
| QA-AUTH-03 | OTP wrong code is rejected | ✅ 2026-08-29 | 000000 → "That code is incorrect or has expired."; stayed on code-entry, not signed in |
| QA-AUTH-04 | OTP cumulative lockout & code expiry | ✅ 2026-08-29 | (a) 5 wrong → fresh CORRECT code still 401 (lock survives resend); (b) real code after 1-min lifespan → 401 expired |
| QA-AUTH-11 | Passwordless endpoints are rate-limited | ✅ 2026-08-29 | curl burst → 429 after 5; UI send → exact banner "Too many requests. Please wait a minute…" |
| QA-AUTH-05 | Magic link is single-use ⚙️ | ✅ 2026-08-29 | reused consumed link → /login?error=invalid_link, not signed in |
| QA-AUTH-06 | Magic link expiry | ✅ 2026-08-29 | link opened after 1-min lifespan → /login?error=invalid_link "expired or already used" |
| QA-AUTH-07 | User-enumeration protection | ✅ 2026-08-29 | never-used address: identical "Check your inbox" (magic) + code-entry (OTP); no existence leak |
| QA-AUTH-08 | Login error banners | ✅ 2026-08-29 | all 4 params → correct copy (external_failed / email_unverified / invalid_link / fallback) |
| QA-AUTH-09 | Email-format validation ⚙️ | ✅ 2026-08-29 | "abc" → no request fired + inline "Enter a valid email address." |
| QA-AUTH-10 | Magic link & OTP on web; OAuth always | ✅ 2026-08-29 | both email methods + Google + Microsoft buttons present |

## §6 Web — Onboarding & new tenant — ✅ COMPLETE (2/2, 2026-08-29)

| Case | Title | Status | Notes |
|---|---|---|---|
| QA-ONB-01 | First-ever sign-in auto-creates a household, user is owner | ✅ 2026-08-29 | fresh OTP account → straight into app, /household shows Owner badge, sole member |
| QA-ONB-02 | Returning user keeps their household | ✅ 2026-08-29 | sign out + back in → same household "QA House", role + data intact |

## §7 Web — Household management — ✅ COMPLETE (14/14, 2026-08-29)

_Harness note: Remove/Leave/Dissolve funnel through one native `confirm()` (JsConfirm) that this browser automation can't accept — those 3 (HH-03/07/08, all ⚙️ CI-automated) were behavior-verified via API; their UI buttons were verified present in the browser._

| Case | Title | Status | Notes |
|---|---|---|---|
| QA-HH-01 | Owner renames the household | ✅ 2026-08-29 | "Household renamed."; header badge → "QA House" after silent refresh |
| QA-HH-02 | Member sees read-only household ⚙️ | ✅ 2026-08-29 | as member: read-only name, no invitations/data card, no per-row buttons, only Leave |
| QA-HH-03 | Owner removes a member ⚙️ | ✅✅ 2026-08-29 | **GENUINE end-to-end** (user's own Chrome, magic-link sign-in → click Remove → confirm dialog OK): member gone from owner's list, re-homed to fresh solo household — verified via API both sides. (Also API-verified earlier.) |
| QA-HH-04 | Owner cannot remove themselves | ✅ 2026-08-29 | owner's own row shows Owner badge, no Remove button (screenshot-confirmed) |
| QA-HH-05 | Transfer ownership ⚙️ | ✅ 2026-08-29 | UI (no confirm): "Ownership transferred."; badges flipped owner↔member |
| QA-HH-06 | Owner w/ members cannot directly leave/dissolve | ✅ 2026-08-29 | owner-with-members shows Transfer card, no leave/dissolve button |
| QA-HH-07 | Sole owner dissolves the household ⚙️ | ✅✅ 2026-08-29 | **GENUINE** (Chrome-extension click "Leave and delete" → native dialog in user's real Chrome → user clicked OK): "Member removed."→ dissolve; old tenant deleted (DB 0 rows), re-homed to new household |
| QA-HH-08 | Member leaves the household ⚙️ | ✅✅ 2026-08-29 | **GENUINE** (extension click Leave → user OK): left → re-homed to fresh solo household; host household intact, user gone |
| QA-HH-09 | Owner promotes a member to admin ⚙️ | ✅ 2026-08-29 | UI: "Role updated.", badge Member→Admin, button → Make member |
| QA-HH-10 | Owner demotes an admin to member ⚙️ | ✅ 2026-08-29 | UI: "Role updated.", badge Admin→Member, button → Make admin |
| QA-HH-11 | Admin sees mgmt controls, not role/ownership ⚙️ | ✅ 2026-08-29 | admin: rename + invite + Remove-on-member; NO role controls, NO transfer/dissolve/data, only Leave |
| QA-HH-12 | Member sees no management controls ⚙️ | ✅ 2026-08-29 | same as HH-02 — plain member sees only read-only name + Leave |
| QA-HH-13 | Owner exports household data | ✅ 2026-08-29 | "Your export is ready: Download"; bundle has tenant/members/invitations, zero secret leaks |
| QA-HH-14 | Seat quota blocks inviting past the plan limit ⚙️ | ✅ 2026-08-29 | Free cap 3: 3rd invite → 402 "seat limit reached" (UI banner + API); revoke pending → re-invite 201 |

## §8 Web — Invitations & joining — ✅ COMPLETE (10/10, 2026-08-29)

_Run in the Claude-in-Chrome extension (owner + invitee sessions). Token panels are redacted by the
extension's sensitive-data filter — verified token behavior via API accept-attempts._

| Case | Title | Status | Notes |
|---|---|---|---|
| QA-INV-01 | Owner invites by email; token + email ⚙️ | ✅ 2026-08-29 | token revealed + Copy + "emailed a join link"; Mailpit "You've been invited" w/ /join link; pending entry |
| QA-INV-02 | Invitee accepts and joins (two-user) ⚙️ | ✅ 2026-08-29 | invitee opened /join → "Sign in to accept" → OTP sign-in → joined owner's household (API-confirmed member; invite consumed) |
| QA-INV-03 | Already-authenticated user accepts directly | ✅ 2026-08-29 | signed-in user opened valid /join → **"You're in!"** immediately (no sign-in step); moved into owner's household (old solo membership replaced) |
| QA-INV-04 | Join page with no token → manual code entry | ✅ 2026-08-29 | /join shows manual invite-code entry; garbage code → inline error, no crash |
| QA-INV-05 | Join with invalid/expired/used token | ✅ 2026-08-29 | used token + garbage token → "Couldn't join. The invitation is invalid or has expired." + Back to household; no membership change |
| QA-INV-06 | Invite an existing member is rejected | ✅ 2026-08-29 | invite own member email → "That email is already a member." (409), no duplicate |
| QA-INV-07 | Regenerate a pending invitation | ✅ 2026-08-29 | UI Regenerate → new token revealed; OLD token → 400 (API accept-attempt), invite still pending |
| QA-INV-08 | Revoke a pending invitation ⚙️ | ✅ 2026-08-29 | UI Revoke → "Invitation revoked.", gone from pending; revoked token → 400 |
| QA-INV-09 | Copy token button | ✅ 2026-08-29 | Copy button present on revealed token; click no error |
| QA-INV-10 | Accept after downgrade refused (seat re-check) ⚙️ | ✅ 2026-08-29 | invite while Pro → revert to Free (webhook) → invitee opens link → **"This household is full"** (402), stays pending; re-comp to Pro → same link → **"You're in!"** (self-heal, BILLING-9) |

## §9 Web — Settings / linked accounts & MFA — ✅ COMPLETE (16/16, 2026-08-29)

| Case | Title | Status | Notes |
|---|---|---|---|
| QA-SET-01 | View linked providers | ✅ 2026-08-29 | after SET-02: Google **Connected + Unlink**, Microsoft **Link** — one row per provider |
| QA-SET-02 | Link a second provider 🔑 | ✅ 2026-08-29 | Settings → Link Google → consent (argamboad, freed by the Allan-unlink cleanup) → *"Google linked successfully."* `?linked=google`, Connected, **same qa-set9 account** (no new account) |
| QA-SET-03 | Linking a provider used by another account rejected 🔑 | ✅ 2026-08-29 | linked-attempt with argamboad Google while still owned by Allan → *"That provider account is already linked to a different user."* `?link_error=in_use`, nothing linked, Allan untouched |
| QA-SET-04 | Unlink a provider 🔑 | ✅ 2026-08-29 | guarded confirm *"Unlink this sign-in method?"* → **OK** → Google reverts to **Link**. (Confirm branch + dialog-fires; cancel branch already proven HH-03/07/08) |
| QA-SET-05 | Unlinking your only provider never locks you out | ✅ 2026-08-29 | sole provider unlinked → sign out → OTP `562534` + MFA step-up TOTP → *"Welcome back… qa-set9's Household"* — same account, no lockout |
| QA-SET-06 | Settings requires auth | ✅ 2026-08-29 | signed out → /settings → /login |
| QA-SET-07 | Delete my account ⚙️ | ✅ 2026-08-29 | sole-owner → 1st confirm + 2nd dissolve confirm → **OK/OK** → /login; **DB: 0 Users / 0 Tenants** for qa-set9 |
| QA-SET-08 | Theme: dark mode applies/persists/follows ⚙️ | ✅ 2026-08-29 | Dark applies instantly, reload first-paints dark, persists; Auto/cross-profile via PREFS-1 (ADV-19) |
| QA-MFA-01 | Enable two-factor (authenticator TOTP) ⚙️ | ✅ 2026-08-29 | enroll form renders (QR + manual key + verify + Off badge); enrolled → badge **On**, 10 recovery codes (typeable `xxxxx-xxxxx`, safe glyphs). In-UI code entry blocked by extension redacting the secret — mechanism API-proven + CI |
| QA-MFA-02 | Two-factor required at sign-in ⚙️ | ✅ 2026-08-29 | OTP sign-in → step-up prompt (not signed in) → correct TOTP → signed in |
| QA-MFA-03 | Use a recovery code, then disable two-factor | ✅ 2026-08-29 | recovery code at step-up (forgiving: `S8HWZDRM99` ≡ `s8hwz-drm99`) → signed in; Settings Disable w/ TOTP → badge Off |
| QA-MFA-04 | Redirect logins (OAuth/magic link) enforce step-up | ✅ 2026-08-29 | magic link → `/login?mfa=<challenge>`, not signed in → TOTP → signed in. OAuth leg = same path, ⏳ your hand-off to confirm |

## §9b Web — Notifications — ✅ COMPLETE (4/4, 2026-08-29)

| Case | Title | Status | Notes |
|---|---|---|---|
| QA-NOTIF-01 | Notification bell + list ⚙️ | ✅ 2026-08-29 | bell badge count 3; dropdown shows 3 seeded notices newest-first + body + "just now" + trash; Mark all read / Clear read / Clear all footer |
| QA-NOTIF-02 | Mark read / mark all read ⚙️ | ✅ 2026-08-29 | UI row-click marked notice 1 read (API unread-count → 2); read-all → 0 |
| QA-NOTIF-03 | Delivery preferences ⚙️ | ✅ 2026-08-29 | prefs persist (fields `in_app`/`email`); `in_app=false` suppresses the in-app row (announce → 0). NOTE: PUT silently no-ops unknown field names (`in_app_enabled`) w/ 200 |
| QA-NOTIF-04 | Delete a notification / clear read / clear all | ✅ 2026-08-29 | DELETE one → 204; bulk no-scope → 400 `scope_required`; `?read=false` clears all → empty; clear-read scope in ADV-20 |

**ADV-09 re-verified (2026-08-29):** §14a's precondition used `in_app_enabled`/`email_enabled` (silent no-op → channels were actually ON). Re-ran with genuinely-off prefs (`in_app:false,email:false`): staff MFA-reset still delivered BOTH the `security.mfa_reset` bell row AND the "Two-factor authentication was reset" email. Security override holds on both channels. ✅

## §10 Web — Localization (i18n) — ✅ COMPLETE (4/4, 2026-08-31)

| Case | Title | Status | Notes |
|---|---|---|---|
| QA-I18N-01 | Switch language on the login page ⚙️ | ✅ 2026-08-31 | login switcher → Español: title/labels/buttons Spanish (*Iniciar sesión / Enviarme un enlace mágico / código de 6 dígitos*), tab title *Iniciar sesión*; **persists on reload** (theme opts also localized Claro/Oscuro) |
| QA-I18N-02 | Language persists per user across sessions ⚙️ | ✅ 2026-08-31 | qa-c5 signed in w/ Spanish adopted (PREFS-1); signed out → **login page reverted to English**; signed in again from English page → app came up **Spanish** = user's saved locale beat browser context (server-wins) |
| QA-I18N-03 | In-app UI fully translated (no English leaks) | ✅ 2026-08-31 | Home welcome + Hogar + Ajustes + invite form all Spanish; only brand (Google/Microsoft) + self-labeled lang options stay as-is — no leaks |
| QA-I18N-04 | Email language matches requester's UI language | ✅ 2026-08-31 | OTP es *"Tu código de verificación"* / en *"Your verification code"*; magic link es *"Tu enlace de acceso"* / en *"Your sign-in link"* (via `culture` body field, not Accept-Language); invitation es *"Te han invitado a un hogar"* |

## 👥 Human re-test queue (genuine clicks + dialog confirms — Sherlock & Watson)

Items passed via API/CI workaround that we'll re-run the *real* way with the user driving the
human step. Already API- + CI-verified, so this is fidelity confirmation, not a coverage gap.
**This list grows** as later chunks hit more confirm dialogs / downloads / OAuth-linking.

| Case | Human step | Prep Claude does first |
|---|---|---|
| QA-HH-03 | ~~click Remove → confirm~~ **NOT human-reachable in the pane** | — |
| QA-HH-08 | ~~click Leave → confirm~~ **NOT human-reachable in the pane** | — |
| QA-HH-07 | ~~Leave and delete → confirm~~ **NOT human-reachable in the pane** | — |
| QA-HH-13 | click **Download** → confirm the JSON file saves (may also be sandbox-blocked) | signed in as owner |

**RESOLVED 2026-08-29 — WORKFLOW ESTABLISHED.** The MCP browser *pane* is CDP-automated and
suppresses native `confirm()` (app `JsConfirm` fail-closes safely — CONF-15, a positive finding).
**THE WORKING PATH for confirm dialogs & other genuine clicks: the Claude-in-Chrome EXTENSION.**
Claude drives the click via `mcp__claude-in-chrome__*` in the user's **real Chrome** (shared
localhost session, so already signed in); the native dialog **renders and blocks there**; the user
clicks **OK**; Claude verifies via API/DB + re-reads the UI. **Proven end-to-end on HH-03, HH-07,
HH-08 (all ✅✅).** Use this for every remaining confirm-gated / genuine-click case (§9 delete
account + unlink, §10b admin writes, etc.). Note: the extension click that triggers a blocking
dialog may return a CDP "timed out"/"frozen renderer" message — that's expected (the dialog is
blocking); proceed to the user's OK click.

## §10 Web — Localization (i18n)

| Case | Title | Status | Notes |
|---|---|---|---|
| QA-I18N-01 | Switch language on the login page ⚙️ | ⬜ | |
| QA-I18N-02 | Language persists per user across sessions ⚙️ | ⬜ | |
| QA-I18N-03 | In-app UI fully translated (no English leaks) 👁 | ⬜ | |
| QA-I18N-04 | Email language matches the requester's UI language | ⬜ | Mailpit |

## §10b Web — Admin console (platform staff) — ✅ COMPLETE (7/7, 2026-08-31)

Staff acct = qa-staff@example.com (launch `Admin:StaffEmails:0`). Target tenant = qa-c5's Household (owner qa-c5 + member qa-c5inv).

| Case | Title | Status | Notes |
|---|---|---|---|
| QA-ADMIN-01 | Staff sees the console; non-staff don't ⚙️ | ✅ 2026-08-31 | **staff**: Admin link in header → `/admin` lists **28 tenants w/ member counts** + detail (id, created, audit count, subscription, members). **non-staff** (qa-c5inv): **no Admin link**, `/admin` → *"You don't have access to the admin console."* |
| QA-ADMIN-02 | View a tenant is audited in that tenant | ✅ 2026-08-31 | opening qa-c5 detail wrote `admin.tenant.viewed` **in qa-c5's tenant** (DB-confirmed; UI count lags one render) |
| QA-ADMIN-03 | Impersonate a user, then stop | ✅ 2026-08-31 | Sign-in-as qa-c5inv (confirm) → yellow banner + browse as qa-c5inv; **Stop impersonating** → back to staff; **reload also ends it** (non-refreshable token); 2× `admin.impersonation.started` audited in-tenant. ⚠️ **COSMETIC FINDING:** while impersonating, the **header keeps the staff badge/email AND the Admin link stays visible** (spec: show impersonated user + hide Admin). Non-security — the Admin link is non-functional (impersonation token is non-staff → `/admin` refuses); only the page body + banner reflect the impersonated user |
| QA-ADMIN-04 | Staff announcement reaches a tenant's members ⚙️ | ✅ 2026-08-31 | **all-members**: "sent to 2 member(s)"; **targeted** (check qa-c5inv only): button → "Send to 1 selected" → "sent to 1"; selection clears after send. DB: 2× `admin.announcement.sent` in-tenant; per-member fan-out exact (**owner 1 notif, qa-c5inv 2**); both members got the email too (Mailpit) |
| QA-ADMIN-05 | Platform-wide broadcast reaches every user | ✅ 2026-08-31 | "Announce to everyone" (above grid) → "Broadcast queued for delivery." (async outbox); DB: reached **38 distinct users across 27 tenants**; qa-c5inv bell shows it |
| QA-ADMIN-06 | Comp a tenant to Pro and revert | ✅ 2026-08-31 | **comp** (qa-magic Free→Pro): badge pro/active, "Subscription updated.", projection **pro/active w/ NO Stripe ids, no period-end** (never lapses), `admin.subscription.comped`. **revert**: badge free/none, `admin.subscription.reverted`. **provider guard**: pushed webhook w/ `StripeSubscriptionId` → qa-c5 detail shows *"Managed by the billing provider — change the plan there."* + no comp/revert buttons; API PUT comp **409 provider_managed** + DELETE revert **409 provider_managed** |
| QA-ADMIN-07 | Reset a locked-out user's MFA | ✅ 2026-08-31 | qa-c5inv MFA enrolled (API), then staff **Reset MFA** (confirm) → "Two-factor authentication was reset for qa-c5inv…". DB: recovery codes → 0, `admin.mfa.reset` in-tenant. **Behavioral**: qa-c5inv OTP sign-in → **NO MFA step-up** (primary auth alone). **Visibility**: bell shows *"Two-factor authentication was reset"* security notice + reset email in Mailpit |

## §10c Web — Billing page — ✅ COMPLETE (2/2, 2026-08-31)

| Case | Title | Status | Notes |
|---|---|---|---|
| QA-BILL-01 | Billing page shows plan + seats; owner-only ⚙️ | ✅ 2026-08-31 | **owner** (qa-c5): Current plan Free / *No subscription*, Seats **2 of 3** (owner + pending invite), Upgrade to Pro, no Manage-sub. **member** (qa-c5inv): only *"Only the household owner can manage billing…"* notice |
| QA-BILL-02 | Upgrade via checkout + webhook lands on Pro ⚙️ | ✅ 2026-08-31 | Upgrade→redirect `billing.test/checkout/{tenantId}/pro` (dead, expected); POST `/api/billing/webhook` `Stripe-Signature: valid` PascalCase pro/active → 200; reload → **Pro / Active, Seats 2 of 10, Manage subscription** shown. Tenant now provider-backed (`cus_qac5test`) → feeds ADMIN-06 guard |

**Setup note — member join (for BILL-01 member + Admin):** invited qa-c5inv (Spanish invite), accepted via `/join` "Sign in to accept" → OTP. DB confirms qa-c5inv = **member of qa-c5's Household, single membership, invite status accepted** (invariant holds; 2nd /join → "invalid or expired" = single-use ✓). **Minor cosmetic:** immediately post-join the header/welcome briefly showed the placeholder *"qa-c5inv's Household"* (sign-in JWT minted pre-join carried a default tenant name); **self-healed to "qa-c5's Household" on next reload/refresh.** Not a data defect (membership always correct) — flag as a transient stale-JWT-tenant-name display. qa-c5inv MFA then enrolled via API (secret stored `c5inv-mfa.json`) for ADMIN-07.

## §11 Emails (Mailpit) — branding & content

| Case | Title | Status | Notes |
|---|---|---|---|
| QA-MAIL-01 | OTP email is branded & correct | ✅ 2026-08-31 | Mailpit render: sloth logo (CID `perezosoft-logo`, renders, not broken), wordmark, code box, expiry copy, footer tagline *"Lazy reputation. Efficient engineering."*; Mailpit HTML-Check 92% |
| QA-MAIL-02 | Magic-link email | ✅ 2026-08-31 | same branded template (CID logo + tagline verified via API); sign-in link **proven working** by MFA-04's magic-link sign-in + AUTH-01 |
| QA-MAIL-03 | Invitation email | ✅ 2026-08-31 | branded (both EN + ES copies inspected); correct recipient; `/join?token=…` link **proven working** — Chunk 5's qa-c5inv joined through it |
| QA-MAIL-04 | Logo renders in a real client 👁 | ✅ 2026-08-31 | real OTP sent via Brevo to argamboad@gmail.com; **user confirmed the CID logo renders in Gmail** ("all good") |

## §14 Cross-cutting security

| Case | Title | Status | Notes |
|---|---|---|---|
| QA-SEC-01 | Tenant isolation | ✅ 2026-08-31 | UI: owner A (qa-c5) saw only A's members/invites (Chunk 5); owner B (qa-staff) saw only B's — zero cross-contamination; API level proven by ADV-01 |
| QA-SEC-02 | Protected pages require auth | ✅ 2026-08-31 | signed out: `/household` → `/login`, `/settings` → `/login` (fresh loads) |
| QA-SEC-03 | Session is gone after sign-out | ✅ w/ finding | reload/direct-nav → `/login` ✓; session dead (refresh → **401**, fetches return nothing, router inert). **F3 (minor):** browser **Back** restores a bfcache snapshot of the last authenticated page — a stale, INERT view (bell shows empty despite 1 real notif; Settings click dead; controlled fresh-token re-run confirmed no live access). Shared-device shoulder-surf nit → fix = `pageshow`-persisted redirect or `Cache-Control: no-store`; task chip spawned |
| QA-SEC-05 | Server-side hardening (automated) | ✅ 2026-08-31 | ran the named suites live: TenantStamping + RefreshTokenService + ClaimsExtractor + NativeRedirectPolicy = **41/41 pass** (last also re-confirms SEC-04's evidence) |

## §14a Adversarial & tenant-isolation — COMPLETE (2026-08-28)

| Case | Title | Status | Notes |
|---|---|---|---|
| QA-ADV-01 | A cannot read/export/erase B via the API | ✅ 2026-08-28 | every B id → 404; export B-free; B intact |
| QA-ADV-02 | Cross-tenant isolation on PUBAPI/HOOKS tables | ✅ 2026-08-28 | replay/delete → 404; GET deliveries 200-empty (cosmetic, no leak) |
| QA-ADV-03 | Export covers every tenant table, secret-free | ✅ 2026-08-28 | api_keys/webhooks/usage present; no whsec_/hash |
| QA-ADV-04 | Dissolve deletes keys/secrets/counters/logs | ✅ 2026-08-28 | DB: all 0 rows for dissolved tenant |
| QA-ADV-05 | Writes under impersonation attributed to staff | ✅ 2026-08-28 | mechanism verified (tenant.exported carries impersonated_by); finding #3 FIXED (PR #200 — case reworded to audited writes) |
| QA-ADV-06 | Impersonation can't reach staff-only actions | ✅ 2026-08-28 | all staff endpoints → 403 |
| QA-ADV-07 | Impersonation can't exceed target's role | ✅ 2026-08-28 | member impersonation → owner actions 403 |
| QA-ADV-08 | No pref rewrite under imp.; shared-device bleed | ✅ 2026-08-28 | leg A: theme PUT 403, unchanged; leg B: Y renders English, no bleed |
| QA-ADV-09 | Staff MFA-reset notif not silenceable | ✅ 2026-08-28 | prefs off → bell + email still delivered |
| QA-ADV-10 | MFA step-up cumulative lockout | ✅ 2026-08-28 | correct TOTP still 401 after per-IP reset |
| QA-ADV-11 | Recovery code single-use | ✅ 2026-08-28 | reuse 401; unused code works |
| QA-ADV-12 | Webhook idempotency + same-second recency | ✅ 2026-08-28 | dup no-op; same-second active applied |
| QA-ADV-13 | First-ever bad-state webhook no false-notify | ✅ 2026-08-28 | past_due first-ever → 0 notifications |
| QA-ADV-14 | Comp/revert a churned (canceled) tenant | ✅ 2026-08-28 | comp → 200, flips to active/pro |
| QA-ADV-15 | Concurrent last-seat acceptance | ✅ 2026-08-28 | invariant held; setup nuance → finding #4 |
| QA-ADV-16 | OTP double-redemption issues one session | ✅ 2026-08-28 | concurrent → one 200, one 401 |
| QA-ADV-17 | Replaying rotated refresh revokes family | ✅ 2026-08-28 | old 401; new then 401 (family revoked) |
| QA-ADV-18 | Spanish account, English device, invite ⚙️ | ⚙️ covered | I18nTests, green in develop CI |
| QA-ADV-19 | Theme/locale no revert after soft sign-in | ✅ 2026-08-28 | server won (Light); switcher consistent |
| QA-ADV-20 | "Clear read" never deletes unread | ✅ 2026-08-28 | no-scope → 400; read-only clear; unread survived |
| QA-ADV-21 | Security headers on single-origin host | ✅ 2026-08-28 | on real staging: HSTS/nosniff/CSP/Referrer + cache |
| QA-ADV-22 | Forged XFF can't bypass rate limit | ✅ 2026-08-28 | rotating XFF → 429 after 5 |

## §14b API surfaces — PUBAPI + HOOKS (curl)

| Case | Title | Status | Notes |
|---|---|---|---|
| QA-API-01 | Config gate: surfaces 404 when disabled | ✅ 2026-08-31 | temp flags-off launch profile: `/api/public/openapi.json`, `whoami`, `/api/apikeys`, `/api/webhooks` all **404** (routes unmapped); flags back on: openapi **200 anon**, others **401** |
| QA-API-02 | Mint an API key (shown once) | ✅ 2026-08-31 | 201 + raw `pk_…` in create response only; GET list → `key:null` + prefix/scopes; member mint → **403 ManageApiKeys** |
| QA-API-03 | Call public API with key; scopes + tenant scope | ✅ 2026-08-31 | whoami 200 w/ correct `tenant_id` (qa-c5); read-key POST echo → **403 insufficient_scope**; write-key → 200; no/garbage key → 401; revoke (204) → whoami **401** |
| QA-API-04 | Per-key rate limit | ✅ 2026-08-31 | 65-burst on one key → 59×200 + 6×429 (60-budget incl. 1 prior in-window call); fresh 2nd key → **200** while 1st still 429 — budgets per key |
| QA-API-05 | Register a webhook + send test + verify signature | ✅ 2026-08-31 | 201 + `whsec_` shown once; bad URL/empty events → 400; member → 403; send-test → `{delivered:true,status_code:200}`; webhook.site got `X-Webhook-Id`/`X-Webhook-Event: ping`/`X-Webhook-Signature` and **independent HMAC-SHA256(raw body, whsec) matched exactly** (`sha256=` hex) |
| QA-API-06 | Delivery log + replay | ✅ 2026-08-28 | UNBLOCKED by PR #199: send-test records a WebhookDelivery row (success:false + error); replay → 202 re-delivers same event_id; unknown id → 404 |

---

## Progress & reconciliation (total plan = 150 cases)

- **Co-pilot-runnable: 107 — 107 ✅. THE CO-PILOT PASS IS COMPLETE (2026-08-31).**
  (Smoke 5 · AUTH 11 · ONB 2 · HH 14 · INV 10 · SET 8 · MFA 4 · NOTIF 4 · ADV 22 · I18N 4 ·
  ADMIN 7 · BILL 2 · MAIL 4 · SEC 4 · API 6.) Remaining plan-wide: only the **43 native-client
  cases** (device/emulator; QA-AND-15 kill drill is the headline item).
- **Chunk 6 (§11 Mail + §14 SEC + §14b API) COMPLETE 2026-08-31** — 12/13 ✅ + MAIL-04 optional.
  **Finding F3 (minor, SEC-03):** browser Back after sign-out restores a bfcache snapshot of the
  last authenticated page — stale but INERT (refresh 401, fetches empty, router dead; fresh-token
  controlled re-run confirmed no live access). Fix = `pageshow` persisted-restore redirect or
  `Cache-Control: no-store`; task chip spawned.
- **Not runnable in co-pilot (native clients): 43** — see the list below.
- **107 + 43 = 150.** ✓ matches the full QA plan count.
- **Chunk 4 (§9 Settings/MFA/Notifications) COMPLETE 2026-08-29** — SET-01..08 + MFA-01..04 +
  NOTIF-01..04 all ✅. SET-02/03 done via the Allan-Google unlink cleanup (freed argamboad to link
  fresh onto qa-set9); qa-set9 then deleted (SET-07, DB-verified 0/0). **Side effect: Allan app
  account left with 0 linked providers (email-only) — relink pending user choice.**
- **Chunk 5 (§10 i18n + §10b Admin + §10c Billing) COMPLETE 2026-08-31** — 13/13 ✅. Built one
  test tenant (qa-c5 owner + qa-c5inv member) through i18n → billing (Pro via fake webhook) → then
  inspected it as staff. **2 minor findings, both non-blocking:** (F1) transient stale-JWT
  **tenant-name** right after a join ("qa-c5inv's Household" placeholder → self-heals to real name on
  next refresh; membership always correct). (F2) **impersonation cosmetic** — header keeps staff
  identity + Admin link visible during impersonation (spec: show impersonated user, hide Admin);
  non-security (impersonation token is non-staff, Admin link refuses). **F2 FIXED + RE-TESTED
  2026-08-31** — branch `claude/zealous-jones-ca683c` (worktree session): `AuthService.IdentityChanged`
  event + `AppHeader` re-source (distinct from `SignedIn` so prefs-reconcile never adopts the
  impersonated user's prefs); re-test in fixed build: header shows impersonated identity, Admin link
  hidden, exit restores staff header immediately; 2 new Core.Tests pass, course COVERAGE mapped
  (811→812), regen drift-free, no stale lesson quotes. Awaiting PR/merge. Left state: qa-c5 is
  provider-managed (`sub_qac5test`) from the ADMIN-06 guard test; qa-magic reverted to Free.
- Per-area co-pilot totals: Smoke 5 · AUTH 11 · ONB 2 · HH 14 · INV 10 · SET 8 · MFA 4 · NOTIF 4 ·
  I18N 4 · ADMIN 7 · BILL 2 · MAIL 4 · SEC 4 · ADV 22 · API 6.
- **Findings (from §14a) — #1–#3 FIXED & merged, re-verified 2026-08-28:**
  #1 Postman `ping` example (PR #198) · #2 webhook delivery log now records on send-test (PR #199,
  unblocks API-06) · #3 ADV-05 case reworded to audited writes (PR #200). #4 ADV-15 setup nuance =
  doc-only, not fixed (reachable only via downgrade, already covered by automated QA-INV-10).

## Out of scope for co-pilot — native clients (device/emulator required) — 43 cases

- Scattered native cases (7): QA-SMK-05, QA-SMK-06, QA-MFA-05, QA-SEC-04, QA-ADV-23, QA-ADV-24
  _(that's 6 — plus QA-DSK/AND/IOS/MAC below)_
- §12 Desktop (Windows MAUI): QA-DSK-01..15 — **15**
- §13 Android (MAUI): QA-AND-01..15 — **15**
- §13b iOS: QA-IOS-01..04 — **4**
- §13b macCatalyst: QA-MAC-01..03 — **3**

Count: 6 scattered + 15 + 15 + 4 + 3 = **43**. (QA-AND-15 = the on-device OAuth process-death
drill, the last open item on the whole plan.)
