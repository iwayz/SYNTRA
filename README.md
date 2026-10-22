# SYNTRA

Multi-tenant inventory, sales, and purchasing SaaS platform.

## Architecture

- **Backend:** ASP.NET Core Web API (.NET 10), C#, modular monolith (Inventory / Sales / Purchasing / Tenancy modules).
- **Multi-tenancy:** database-per-tenant. A Platform DB (control plane only — tenant directory, subscriptions, module entitlements) resolves each request's tenant from its subdomain; each tenant's own database holds that tenant's business data and end-user accounts.
- **ORM/DB:** EF Core against SQL Server.
- **Auth:** ASP.NET Core Identity + JWT bearer tokens, scoped per tenant. A tenant's end-users live inside that tenant's own database, never centralized.
- **Frontend:** React + Vite + TypeScript SPA, TanStack Query for server state, API client generated from the backend's OpenAPI spec (not yet started).
- **API style:** REST + OpenAPI.

## Structure

```
backend/
  src/
    Syntra.Api/                ASP.NET Core Web API
    Syntra.Domain/             Entities (Platform, TenantData, Inventory, Sales, Purchasing, Tenancy)
    Syntra.Infrastructure/     EF Core DbContexts (PlatformDbContext, TenantDbContext) and migrations
    Syntra.MigrationRunner/    Fans out tenant-schema migrations across every tenant database
    Syntra.TenantProvisioner/  Provisions a new tenant end-to-end (database, schema, admin user)
  tests/
    Syntra.Tests/              Tenant isolation integration tests
frontend/   React + Vite + TypeScript SPA (not yet started)
```

## Status

**Phase 0 (foundations) — complete.** Platform DB schema, subdomain-based tenant resolution, migration orchestrator, tenant provisioning pipeline, tenant end-user auth (Identity + JWT), isolation tests, module folder structure.

**Phase 1 (single-tenant vertical slice) — in progress.**
- [x] 1a — master data: units of measure, products (with unit conversions), warehouses, customers, suppliers
- [x] 1b — stock movement + balance (manual adjustments, movement history, on-hand balance)
- [ ] 1c — purchase order flow
- [ ] 1d — sales order flow
