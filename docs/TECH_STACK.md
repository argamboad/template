# Tech Stack

> Constant across these SaaS projects. The stack/architecture below is pre-decided; only the
> version numbers need re-verification at project start (this file ages). App-specific notes go
> in the marked section at the end.

## Summary

| Layer | Choice | Status |
|-------|--------|--------|
| Backend API | ASP.NET Core Web API | Committed |
| Web frontend | Blazor WebAssembly (WASM) | Committed |
| UI components | Shared **Razor Class Library (RCL)** | Committed (discipline rule) |
| Database | PostgreSQL | Committed |
| ORM | Entity Framework Core (Npgsql) | Committed |
| Auth | Custom JWT + rotating refresh tokens (no ASP.NET Core Identity) | Committed (ADR-002) |
| Non-web clients (mobile + desktop) | .NET MAUI **Blazor Hybrid**, reusing the RCL | Implemented (auth wired); feature parity web-first |
| Hosting | TBD (cheap .NET API + static WASM + Postgres) | Deferred |

## Target versions

> ⚠️ **RE-VERIFY at project start.** Versions move; search for current stable before committing.
> Policy: **target the latest _stable_ release, never previews.**
>
> **Verified 2026-06-17:** .NET SDK 10.0.301 · ASP.NET Core / EF Core **10.0.9** ·
> Npgsql.EntityFrameworkCore.PostgreSQL **10.0.2** · PostgreSQL server **17**.
> Note: `Guid.CreateVersion7()` (time-ordered UUIDv7) is supported in .NET 9+ — already used in
> `Tenant.cs`. PostgreSQL 18 adds a native `uuidv7()` SQL function but is not required for this.

## Architecture shape

**Clean API boundary.** The Blazor WASM web app is a client of the ASP.NET Core API. The frontend
never talks to the database directly. This boundary is the durable architectural asset: any future
client (MAUI mobile/desktop, etc.) consumes the same API.

```
            ┌─────────────────────────┐
            │  ASP.NET Core Web API    │
            │  + EF Core (Npgsql)      │──── PostgreSQL
            │  + custom JWT auth       │
            └────────────┬────────────┘
                         │ HTTP (API boundary)
        ┌────────────────┴───────────────────┐
        │                                     │
┌───────────────┐                  ┌─────────────────────────┐
│ Blazor WASM   │  (NOW)           │ MAUI Blazor Hybrid      │  (LATER)
│ web app       │                  │ mobile/desktop shell    │
└───────┬───────┘                  └───────────┬─────────────┘
        │                                       │
        └──────────────┬────────────────────────┘
                        │  both consume
              ┌─────────────────────┐
              │ Shared Razor Class  │
              │ Library (UI)        │
              └─────────────────────┘
```

## The RCL discipline (present-day rule)

**Blazor UI components live in a shared Razor Class Library, not inline in the web app project.**
A future MAUI Blazor Hybrid app (mobile **and** Windows/macOS desktop) reuses the same components
from the RCL rather than rewriting the frontend. Reuse isn't 100% (navigation/platform bits
differ) but captures the majority of the UI. Cheap now, expensive to retrofit — so pay it up front.

## Multi-tenancy (constant)

- **Tenant ≠ User.** A Tenant (org/household/team — label is app-specific) owns the data; Users
  belong to a Tenant; multiple Users per Tenant.
- **Tenant-scoped data, per-user preferences only.** Enforce tenant scoping on every query; never
  leak across tenants. Users are custom entities authenticated by app-issued JWTs; tenant
  association sits on top and is enforced by a global EF query filter.

## Why these choices (rationale, constant)

- **ASP.NET Core Web API** — strong, well-supported, the durable client-agnostic asset.
- **Blazor WASM (over Server)** — preserves the "frontend is just another API client" boundary;
  modern Blazor reduced WASM bundle size and improved AOT. Server couples UI to server + holds a
  per-user live connection — rejected for that reason.
- **PostgreSQL** — free, portable, cheap to host; capable. Chosen over SQL Server for
  economy/portability.
- **EF Core (Npgsql)** — default .NET ORM; first-class Postgres; maps the data model to migrations.
- **Custom JWT + rotating refresh tokens** (not ASP.NET Core Identity) — a hardened auth stack the
  template ships: passwordless (magic link + email OTP) and OAuth account-linking on custom
  `User`/`UserLogin`/`LoginToken`/`RefreshToken` entities. Tenant scoping layers on top. See ADR-002.
- **MAUI Blazor Hybrid (deferred)** — reuses the C# Blazor UI (via RCL) across mobile + Win/macOS
  desktop, not just the API. Deferred until non-web work begins; re-check MAUI maturity then.
  Alternatives if MAUI disappoints: Uno Platform, Avalonia, or a JS frontend against the same API.

## Deferred sub-decisions (revisit when relevant)

- Hosting specifics (pick near deploy; undemanding profile).
- SMS OTP provider (Twilio etc.) — deferred until phone-based OTP is needed.

## Local dev environment (constant)

Spun up via `docker compose up -d`. Copy `.env.example` → `.env` and adjust before first run.

| Service | Image | Default port(s) | Purpose |
|---------|-------|-----------------|---------|
| `db` | `postgres:17` | `${DB_PORT:-5432}` (committed `.env.example` sets **5433**) | PostgreSQL — matches production DB engine |
| `mail` | `axllent/mailpit:latest` | SMTP `${MAIL_SMTP_PORT:-1025}`, UI `${MAIL_UI_PORT:-8025}` | Local SMTP trap for Identity email flows |

Both services have healthchecks. When the API container is added to compose (per-project), it should declare `depends_on: db: condition: service_healthy`.

Port variables allow multiple projects to run simultaneously without conflicts.

## Auth packages (constant)

| Package | Version | Purpose |
|---------|---------|---------|
| `Microsoft.AspNetCore.Authentication.Google` | 10.0.9 | Google OAuth provider |
| `Microsoft.AspNetCore.Authentication.MicrosoftAccount` | 10.0.9 | Microsoft OAuth provider |
| `MailKit` | 4.17.0 | SMTP email sending (magic links, invitations) |

**Adding a new OAuth provider:** install the provider package, add `.AddXxx(options => ...)` in
`ServiceCollectionExtensions.AddInfrastructure()`. No structural changes needed.

**Secrets in dev:** all local secrets/config live in the gitignored repo-root `.env` (loaded by
the API via DotNetEnv — see ADR-001); never put them in `appsettings*.json`. Copy `.env.example`
to `.env` and fill in. Keys use the .NET env-var form (`__` = section nesting):
```sh
# .env  (repo root)
Jwt__Secret=...
Authentication__Google__ClientId=...
Authentication__Google__ClientSecret=...
Authentication__Microsoft__ClientId=...
Authentication__Microsoft__ClientSecret=...
# Email__Smtp__* — optional; unset = Mailpit trap in dev
```
Production reads the same keys from real environment variables, never a committed file.

## App-specific notes
<!-- Fill per project: anything this app needs beyond the constant stack — extra libraries,
     storage (blob/file), background jobs, real-time (SignalR), search, etc. -->
- _TODO_
