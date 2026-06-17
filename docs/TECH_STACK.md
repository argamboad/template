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
| Auth | ASP.NET Core Identity | Committed |
| Non-web clients (mobile + desktop) | .NET MAUI **Blazor Hybrid**, reusing the RCL | **Deferred** (intended direction) |
| Hosting | TBD (cheap .NET API + static WASM + Postgres) | Deferred |

## Target versions

> ⚠️ **RE-VERIFY at project start.** Versions move; search for current stable before committing.
> Policy: **target the latest _stable_ release, never previews.**
>
> Baseline at template authoring (2026-06): the **.NET 10 (LTS)** line — .NET 10, ASP.NET Core
> 10, Blazor 10, EF Core 10.0.x, Npgsql.EntityFrameworkCore.PostgreSQL 10.0.x, Identity 10.
> PostgreSQL server 17 stable (18 enables native `uuidv7()` via `Guid.CreateVersion7()` with
> EFCore.PG 10 — worth considering for time-ordered primary keys).

## Architecture shape

**Clean API boundary.** The Blazor WASM web app is a client of the ASP.NET Core API. The frontend
never talks to the database directly. This boundary is the durable architectural asset: any future
client (MAUI mobile/desktop, etc.) consumes the same API.

```
            ┌─────────────────────────┐
            │  ASP.NET Core Web API    │
            │  + EF Core (Npgsql)      │──── PostgreSQL
            │  + ASP.NET Core Identity │
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
  leak across tenants. ASP.NET Core Identity handles users; tenant association sits on top.

## Why these choices (rationale, constant)

- **ASP.NET Core Web API** — strong, well-supported, the durable client-agnostic asset.
- **Blazor WASM (over Server)** — preserves the "frontend is just another API client" boundary;
  modern Blazor reduced WASM bundle size and improved AOT. Server couples UI to server + holds a
  per-user live connection — rejected for that reason.
- **PostgreSQL** — free, portable, cheap to host; capable. Chosen over SQL Server for
  economy/portability.
- **EF Core (Npgsql)** — default .NET ORM; first-class Postgres; maps the data model to migrations.
- **ASP.NET Core Identity** — built-in user/auth; tenant scoping layers on top.
- **MAUI Blazor Hybrid (deferred)** — reuses the C# Blazor UI (via RCL) across mobile + Win/macOS
  desktop, not just the API. Deferred until non-web work begins; re-check MAUI maturity then.
  Alternatives if MAUI disappoints: Uno Platform, Avalonia, or a JS frontend against the same API.

## Deferred sub-decisions (revisit when relevant)

- Final non-web-client framework commitment (MAUI intended).
- Hosting specifics (pick near deploy; undemanding profile).
- Identity details (social login, email confirmation, etc., as auth is built).

## App-specific notes
<!-- Fill per project: anything this app needs beyond the constant stack — extra libraries,
     storage (blob/file), background jobs, real-time (SignalR), search, etc. -->
- _TODO_
