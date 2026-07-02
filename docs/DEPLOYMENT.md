# Deployment runbook

> How to run this template on a real environment. The target stack is **free-tier-first** (ADR-017):
> **Render** (one container serving the API **and** the Blazor WASM client, single-origin) + **Neon**
> Postgres + **Brevo** SMTP. The same Docker image runs anywhere — Render is the reference, not a lock-in.
>
> Epic: `DEPLOY` (`docs/stories/deploy.md`). This runbook is DEPLOY-2's deliverable; the CI-driven
> deploy pipeline is DEPLOY-3.

## TL;DR

1. Build once, locally, to prove the container: `docker compose --profile app up --build` → open the app.
2. Create a **Neon** DB, a **Brevo** SMTP key, and (for staging) a **Stripe test** key.
3. Apply `render.yaml` as a Render **Blueprint**, paste the secrets, deploy.
4. Register the OAuth redirect URIs + Stripe webhook against the live URL.

---

## 0. The image (what ships)

`Dockerfile` (repo root) is a multi-stage build that publishes the **Web** (Blazor WASM) and **Api**
projects, folds the WASM bundle into the API's `wwwroot`, and runs the API as a **non-root** user. The
API then serves everything from **one origin** (`Hosting__ServeWebClient=true`, baked in) — so the
refresh cookie is first-party and **no CORS configuration is needed** (`Auth__AllowedOrigins` can stay
empty). It listens on `$PORT` (Render provides it; defaults to 8080), migrates the database on boot, and
exposes `/health` (liveness) + `/health/ready` (DB reachable).

> **SDK pin:** the build image is pinned to `sdk:10.0.301` to match the committed `packages.lock.json`
> files (the Blazor SDK injects patch-specific implicit packages). If you bump the SDK that regenerates
> the lockfiles, bump the Dockerfile tag to match.

### Verify the image locally (no cloud accounts needed)

The `app` service in `docker-compose.yml` is behind the `app` **profile**, so the normal dev flow
(`docker compose up -d` → just Postgres + Mailpit) is unchanged. To run the production container against
the compose Postgres + Mailpit:

```bash
cp .env.example .env         # if you haven't — the app service loads it for secrets
# .env must set a Jwt__Secret (≥32 chars) and, because the container runs in Production, a
# Billing__Stripe__SecretKey (a Stripe TEST key is fine — see §3).
docker compose --profile app up --build      # add: APP_PORT=8099 to change the host port
```

Then check (host port 8080 unless you set `APP_PORT`):

| Request | Expect |
|---|---|
| `GET /health` | 200 |
| `GET /health/ready` | 200 (migrations applied, DB reachable) |
| `GET /` and `GET /settings` | 200, the SPA shell (deep links fall back to `index.html`) |
| `GET /_framework/dotnet.<hash>.js` | 200 `text/javascript` (WASM runtime served) |
| `GET /api/does-not-exist` | **404** (an unmatched API route is never the SPA shell) |
| `GET /api/notifications` (no token) | **401** (the API still guards its routes) |

Tear down with `docker compose --profile app down`.

---

## 1. Neon (Postgres, free)

1. Create a project at <https://neon.tech> (Postgres 17). **Do not enable Neon Auth** — this template
   ships its own auth (ADR-002); a second identity source would only conflict. Use Neon as plain Postgres.
2. Copy the **Direct connection** string (the host **without** `-pooler`). That's the right default here:
   a single Render instance keeps its own Npgsql connection pool, and the app **polls** (no
   `LISTEN/NOTIFY`) and uses no server-side prepared statements, so it doesn't need PgBouncer. Keep
   `SSL Mode=Require`. Shape:
   `Host=<ep>.<region>.aws.neon.tech;Port=5432;Database=<db>;Username=<user>;Password=<pw>;SSL Mode=Require;Trust Server Certificate=true`.
   *(Only switch to the pooled `-pooler` host if you later run many instances — Neon's pooler is
   transaction-mode PgBouncer, which this app is compatible with but doesn't require.)*
3. This becomes `ConnectionStrings__DefaultConnection`. Migrations apply automatically on first boot.

> Free Neon autosuspends when idle and **auto-wakes in ~1 s** on the next query — no manual unpause.

## 2. Brevo (SMTP, free — 300/day)

1. Sign up at <https://brevo.com>, create an **SMTP key** (Senders & API → SMTP).
2. **Verify a sender** (Senders, Domains & Dedicated IPs → **Senders** → add + verify your email).
   Brevo refuses to relay from an unverified sender, so this is required before any mail flows.
3. Set: `Email__Smtp__Host=smtp-relay.brevo.com`, `Email__Smtp__Port=587`,
   `Email__Smtp__Username=<your Brevo login>`, `Email__Smtp__Password=<the SMTP key>`, and
   **`Email__Smtp__FromAddress=<the verified sender>`** (optionally `Email__Smtp__FromName`). Without a
   valid, verified `FromAddress` the send is **rejected by Brevo** — and because mail is async via the
   outbox, the request still returns success while the email never arrives (it retries/dead-letters in
   `OutboxMessages`). If a code doesn't turn up, check that first.
4. For real deliverability later, verify a sender **domain** (SPF/DKIM) — optional for staging QA.

## 3. Stripe (test mode) — REQUIRED

The billing provider is **fail-closed**: in any non-Development environment the app **refuses to boot**
without `Billing__Stripe__SecretKey` (the in-memory fake provider trusts an unsigned webhook and must
never run in Production — GAP-1). For staging, use a **test-mode** secret key (`sk_test_…`) from the
Stripe dashboard. You don't need working billing to sign in — this just satisfies the guard. (When you
later wire real billing, add `Billing__Stripe__WebhookSecret` and point a Stripe webhook at
`/api/billing/webhook`.)

## 4. Render (host, free)

1. Push your branch; in Render choose **New + → Blueprint** and point it at the repo. It reads
   `render.yaml` (service `template-staging`, Docker runtime, free plan, health check `/health/ready`).
2. Fill the dashboard secrets (everything marked `sync: false`): `ConnectionStrings__DefaultConnection`
   (§1), `Billing__Stripe__SecretKey` (§3), the four `Email__Smtp__*` (§2). `Jwt__Secret` is
   auto-generated by Render (persisted, so sessions survive redeploys). Leave `Auth__AppBaseUrl` blank
   for the first deploy.
3. Deploy. When it's live, copy the public URL (e.g. `https://template-staging.onrender.com`) and set
   **`Auth__AppBaseUrl`** to it (magic-link/invite emails link there), then redeploy.

> Free instances **sleep after ~15 min idle** — the first request cold-starts (~30–60 s) and the
> background outbox/scheduler pause while asleep (queued email/webhooks flush on wake). Fine for staging;
> a paid always-on plan (~$7/mo) is the floor for real users. The same image; no code change.

## 5. OAuth redirect URIs (if using Google/Microsoft)

In each provider console add the live callback: `https://<host>/signin-google` and
`…/signin-microsoft`, and set `Authentication__Google__ClientId/Secret` (+ Microsoft) in Render. Because
`Proxy__Enabled=true`, the app sees the real `https` scheme behind Render's proxy, so generated redirect
URIs are correct.

---

## Environment variables (reference)

| Key | Required | Notes |
|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | yes | `Production` (set by `render.yaml`) |
| `ConnectionStrings__DefaultConnection` | yes | Neon **session** pooler, `sslmode=require` |
| `Jwt__Secret` | yes | ≥32 chars; Render auto-generates |
| `Billing__Stripe__SecretKey` | **yes** | fail-closed guard; a `sk_test_…` key for staging |
| `Email__Smtp__Host/Port/Username/Password` | yes | Brevo |
| `Email__Smtp__FromAddress` | yes | a Brevo-**verified** sender; sends fail without it |
| `Email__Smtp__FromName` | no | display name on outgoing mail |
| `Auth__AppBaseUrl` | yes | the public URL — used to build email links |
| `Hosting__ServeWebClient` | yes | `true` (baked into the image; keep set) |
| `Proxy__Enabled` | behind a proxy | `true` on Render — honor `X-Forwarded-*` |
| `PORT` | platform-set | Render provides it; image defaults to 8080 |
| `Auth__AllowedOrigins` | no | leave empty — single-origin needs no CORS |
| `Authentication__Google/Microsoft__*` | optional | enable OAuth |
| `PublicApi__Enabled`, `Webhooks__Enabled` | optional | default off |
| `Admin__StaffEmails__0…` | optional | platform-staff allowlist |

Secrets live only in the Render dashboard / your local `.env` (gitignored) — **never** in the repo
(`render.yaml` declares keys, not values; gitleaks enforces this in CI).

---

## Prod, later

When a downstream app has real users, repeat §1–§5 as a second Render service fed from `main` (an
always-on plan), with **live** Stripe keys, a verified email sender domain (DKIM), and — optionally — a
custom domain (~$10/yr, the first worthwhile paid upgrade: nicer URLs + deliverability). Rehearse risky
migrations against a **Neon branch** (a free copy-of-prod DB) before promoting. The `main`→prod deploy is
gated behind a manual approval (DEPLOY-3).
