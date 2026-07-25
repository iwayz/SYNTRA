# SYNTRA

Multi-tenant inventory, sales, and purchasing SaaS platform.

## Architecture

- **Backend:** ASP.NET Core Web API (.NET 10), C#, modular monolith (Inventory / Sales / Purchasing / Tenancy-Identity modules).
- **ORM/DB:** EF Core against SQL Server. Multi-tenancy via a shared database with a `TenantId` column enforced through EF Core global query filters.
- **Auth:** ASP.NET Core Identity + JWT bearer tokens, carrying tenant/branch/role claims.
- **Frontend:** React + Vite + TypeScript SPA, TanStack Query for server state, API client generated from the backend's OpenAPI spec.
- **API style:** REST + OpenAPI.

## Structure

```
backend/    ASP.NET Core solution (API, Domain, Infrastructure, Tests)
frontend/   React + Vite + TypeScript SPA
```

## Status

Phase 0 — foundations (tooling, tenant-resolution middleware, auth skeleton). No functional features yet.
