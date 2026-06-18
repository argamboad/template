# Data Model

> Structural source of truth: entities, fields, relationships, and **derived rules** (computed,
> not stored). Stack-agnostic; concrete EF Core migrations follow in the repo. The multi-tenant
> base entities are constant; everything else is app-specific.

## Conventions
- `id` primary key on every entity unless noted. UUIDv7 (`Guid.CreateVersion7()`) used — time-ordered,
  supported in .NET 9+ and already active in base entities.
- Timestamps (`created_at`, `updated_at`) assumed on all entities; omitted below for brevity.
- **Tenant scoping:** every app entity that holds tenant data carries a `tenant_id` and must be
  filtered by it on every query. Never leak across tenants.
- "Tenant" is the code term for the household/org/team. _App label: TODO._

## Base entities (constant — multi-tenant foundation)

### Tenant
The household/org/team. Owns all tenant-scoped data.
- `id` (UUIDv7)
- `name`
- _TODO: tenant-level fields specific to this app_

### User (`AppUser`)
A person belonging to a tenant. Many users → one tenant.
- `id` (UUIDv7, from `IdentityUser<Guid>`)
- `tenant_id` (FK → Tenant, required)
- `email`, `username`, `password_hash`, `security_stamp` — managed by ASP.NET Core Identity
- External logins stored in Identity's `AspNetUserLogins` table (one row per OAuth provider)
- _per-user preferences only — TODO: which preferences does this app need?_

### TenantInvitation *(constant — auth foundation)*
An email invitation for a person to join a tenant. One invitation per email per tenant.
- `id` (UUIDv7)
- `tenant_id` (FK → Tenant)
- `email` — the invited address
- `token` — unique, URL-safe token (generated via Identity token provider)
- `invited_by_user_id` (FK → User, loose — stored as Guid)
- `expires_at`
- `accepted_at` (nullable)

**Derived rules (computed, never stored):**
- `is_expired` → `now > expires_at`
- `is_accepted` → `accepted_at IS NOT NULL`
- `is_valid` → `!is_expired AND !is_accepted`

## App entities
<!-- Design fresh per app. For each entity: fields, relationships, and tenant_id where it holds
     tenant data. Do NOT copy entity designs from other projects. -->
_TODO_

## Relationship summary
- Tenant 1 — N User *(constant)*
- Tenant 1 — N TenantInvitation *(constant)*
- _TODO: app-specific relationships_

## Derived rules (computed, never stored)
<!-- The domain logic specific to this app. TenantInvitation rules are defined above. -->
_TODO_

## Pinned model extensions (future, not built)
- SMS/phone field on User — needed when mobile OTP via phone is implemented.
- _TODO_
