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

## Constant flows (auth + tenant onboarding — always present)

### 1. Sign in via OAuth (Google / Microsoft / future providers)
**Goal:** a user authenticates using their existing identity provider account.

Flow:
1. User clicks "Sign in with Google" (or Microsoft, etc.) on the login page.
2. Browser redirects to the provider's OAuth consent screen.
3. Provider redirects back to `/auth/external/callback` with an auth code.
4. API exchanges code for profile, creates or retrieves the `AppUser` record via Identity's
   external login store (`AspNetUserLogins`).
5. **New user branch:** user has no tenant → redirect to "Create household" or "Accept invitation"
   flow (see §3 and §4).
6. **Existing user branch:** user is signed in; API issues a session token; client stores it.

Notes:
- Adding a new provider = one `.AddXxx()` call in `ServiceCollectionExtensions` + OAuth app
  registration at the provider — no other changes needed.
- `AspNetUserLogins` stores the external provider key; one user can have logins from multiple
  providers if they share an email.

### 2. Sign in via magic link (web, passwordless)
**Goal:** a user signs in without a password by clicking an emailed link.

Flow:
1. User enters their email on the login page and requests a magic link.
2. API looks up the `AppUser` by email. If none exists, one is created (unconfirmed).
3. API generates a short-lived token (`MagicLinkTokenProvider`, 15 min default) and emails a
   signed URL: `/auth/magic?token=...&email=...` via SMTP (Mailpit in dev).
4. User clicks the link; API calls `UserManager.VerifyUserTokenAsync` with
   `MagicLinkTokenProvider.Purpose`. Token is single-use (stamp rotates on use).
5. **New user branch:** same as §1 step 5.
6. **Existing user branch:** user is signed in; token issued.

Notes:
- Token is protected by ASP.NET Core Data Protection — tamper-evident and server-bound.
- Tokens expire after 15 min by default (`Auth:MagicLink:TokenLifespanMinutes` in config).
- If the email doesn't exist: respond with the same success message to prevent user enumeration.

### 3. OTP sign-in (mobile — framework ready, implementation deferred)
**Goal:** a user on a mobile device authenticates using a one-time code.

Two modes supported by Identity's `AddDefaultTokenProviders()`:
- **TOTP (authenticator app):** user scans a QR code once; subsequent sign-ins use a 6-digit
  rotating code from their authenticator app. Handled by Identity's `AuthenticatorTokenProvider`.
- **Email OTP:** API sends a 6-digit code to the user's email; user enters it in the app.
  Handled by Identity's `EmailTokenProvider`.
- **SMS OTP (deferred):** requires a phone number on the `User` entity and an SMS provider
  (Twilio, etc.). Infrastructure is intentionally not pre-wired — add when mobile is built.

Notes: mobile client is deferred (MAUI Blazor Hybrid). OTP infrastructure is in place; the
specific endpoints and UI are an auth slice for when mobile work begins.

### 4. Create household (first user, new tenant)
**Goal:** a newly authenticated user creates their household (tenant).

Flow:
1. After first OAuth or magic link sign-in, user has no `tenant_id` yet.
2. User provides a household name.
3. API creates a `Tenant`, sets `user.TenantId`, saves both.
4. User is now the first member of the household.

Notes:
- First user is the de-facto admin; role assignment (if any) is app-specific.
- This is triggered by the "new user branch" in sign-in flows §1 and §2.

### 5. Invite a member to the household
**Goal:** an existing household member invites someone new.

Flow:
1. Member submits the invitee's email address.
2. API creates a `TenantInvitation` record (`is_valid` must be true — no duplicate pending
   invite for the same email).
3. API emails an invitation link: `/join?token=...` via `IEmailSender`.
4. Invitee clicks the link; API validates the invitation (`invitation.IsValid`).
5. Invitee authenticates (OAuth or magic link); `AppUser` is created with `TenantId` set from
   the invitation; `accepted_at` is stamped.

Notes:
- `TenantInvitation.IsValid` = not expired AND not already accepted (derived, not stored).
- Invitations expire after a configurable period (app-specific, suggest 7 days).
- Attempting to use an expired or already-accepted token returns a clear error.
- A user cannot be invited to a second tenant — they already have a `tenant_id` once accepted.

### 6. User & tenant settings
**Goal:** manage per-user preferences and tenant-level settings.
- _TODO: what preferences? what tenant settings?_

---

## App-specific flows
<!-- The heart of the product. One subsection per flow, using the template above. -->
_TODO_

## Flow-to-rule cross-reference
| Flow | Key derived rule |
|------|------------------|
| Invite member (§5) | `TenantInvitation.IsValid` |
| Magic link sign-in (§2) | Token verified via `MagicLinkTokenProvider.Purpose` |
| _TODO_ | _TODO_ |

## Out of scope
- SMS OTP — deferred until mobile (MAUI) work begins.
- Social login beyond Google + Microsoft — infrastructure is provider-agnostic; add per-app.
- See the OUT list in `PROJECT_BRIEF.md`.
