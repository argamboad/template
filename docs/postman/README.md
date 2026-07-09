# Postman collection — Perezosoft Platform API

Complete, chained collection covering **every HTTP surface** of the platform: health/meta,
passwordless sign-in (+ MFA step-up), account/sessions, MFA management, household (members,
invitations, roles, transfer, leave, export + signed download), notifications (incl.
delete/clear), billing (+ the fake-provider webhook that simulates payment completion), the
Notes sample slice, the staff admin surface (announce/broadcast/comp — ADR-021), and the
config-gated Public API + outbound webhooks.

## Files

| File | Import as |
|------|-----------|
| `Perezosoft.postman_collection.json` | Collection (v2.1) |
| `Perezosoft.local.postman_environment.json` | Environment ("Perezosoft — local dev") |

## Quick start

1. `docker compose up -d` (Postgres + Mailpit) and run the API with the **https** profile
   (`dotnet run --project src/Api --launch-profile https` → `https://localhost:7160`).
2. Import both files, select the **Perezosoft — local dev** environment.
3. **SMTP must point at Mailpit** (`localhost:1025`). If your local `.env` overrides SMTP to a
   real provider (e.g. Brevo), switch it back — the OTP auto-fetch reads Mailpit's API.
4. Folder **1 · Sign in**: run `OTP — send` → wait ~2–5 s (outbox is async) → `OTP — fetch code
   from Mailpit` → `OTP — verify`. Tokens land in the environment; everything else just works.

## How auth is wired

- The collection authenticates every request with **Bearer `{{accessToken}}`** (collection-level).
- Sign-in requests send **`X-Native-Client: true`**, selecting the API's body token transport —
  the refresh token arrives in JSON instead of an HttpOnly cookie, so Postman can chain
  `Refresh` (which **rotates**: the stored `refreshToken` is updated on every call; replaying an
  old one revokes all sessions by design).
- MFA-enabled account? `OTP — verify` stores `mfaChallenge`; complete with `MFA — verify
  (step-up)` using a TOTP or recovery code.
- `Impersonate user` stores a separate `{{impersonationToken}}` — it never clobbers your session.

## Feature gates & prerequisites

| Folder | Needs |
|--------|-------|
| 8 · Admin | your email in `Admin__StaffEmails__0` (repo `.env`) + API restart |
| 9 · Public API | `PublicApi__Enabled=true` + restart (`404` when off) |
| 10 · Webhooks | `Webhooks__Enabled=true` + restart; target URL must be public-routable (SSRF guard) |
| 6 · Billing webhook | fake provider (dev default); header `Stripe-Signature: valid` |

## Notes

- Passwordless send endpoints are rate-limited **5/min per IP** (verify: 10/min) → `429` with a
  "too many requests" message; that's the abuse guard (QA-AUTH-11), not a bug.
- Requests marked ⚠ are destructive (account erasure). The tests on each request assert the
  *expected* status set, including documented guard responses (402 quota, 409 provider-managed…).
- Rebranding: rename the collection/env (`Perezosoft` → your app) — see `docs/REBRANDING.md`.
