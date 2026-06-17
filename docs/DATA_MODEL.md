# Data Model

> Structural source of truth: entities, fields, relationships, and **derived rules** (computed,
> not stored). Stack-agnostic; concrete EF Core migrations follow in the repo. The multi-tenant
> base entities are constant; everything else is app-specific.

## Conventions
- `id` primary key on every entity unless noted. (Consider time-ordered UUIDv7 keys — EF Core 10
  + PostgreSQL 18 supports `Guid.CreateVersion7()`.)
- Timestamps (`created_at`, `updated_at`) assumed on all entities; omitted below for brevity.
- **Tenant scoping:** every app entity that holds tenant data carries a `tenant_id` and must be
  filtered by it on every query. Never leak across tenants.
- "Tenant" is the code term for the org/household/team. _App label: TODO._

## Base entities (constant — multi-tenant foundation)

### Tenant
The org/household/team. Owns all tenant-scoped data.
- `id`
- `name`
- _TODO: tenant-level fields specific to this app_

### User
A person belonging to a tenant. Many users → one tenant.
- `id`
- `tenant_id` (required)
- `email` / auth fields (via ASP.NET Core Identity)
- _per-user preferences only — TODO: which preferences does this app need?_

## App entities
<!-- Design fresh per app. For each entity: fields, relationships, and tenant_id where it holds
     tenant data. Do NOT copy entity designs from other projects. -->
_TODO_

## Relationship summary
<!-- Bullet the key relationships once entities are defined. -->
- Tenant 1 — N User *(constant)*
- _TODO_

## Derived rules (computed, never stored)
<!-- The domain logic that is calculated at query time rather than persisted. This is usually the
     heart of the app. Define each rule precisely. -->
_TODO_

## Pinned model extensions (future, not built)
<!-- Schema-level changes parked for later. -->
- _TODO_
