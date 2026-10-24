# SYNTRA

Multi-tenant inventory, sales, and purchasing SaaS platform.

## Architecture

- **Backend:** ASP.NET Core Web API (.NET 10), C#, modular monolith (Inventory / Sales / Purchasing / Tenancy modules).
- **Multi-tenancy:** database-per-tenant. A Platform DB (control plane only — tenant directory, subscriptions, module entitlements) resolves each request's tenant from its subdomain; each tenant's own database holds that tenant's business data and end-user accounts.
- **ORM/DB:** EF Core against SQL Server.
- **Auth:** ASP.NET Core Identity + JWT bearer tokens, scoped per tenant. A tenant's end-users live inside that tenant's own database, never centralized.
- **Frontend:** React + Vite + TypeScript SPA, Tailwind CSS v4 for styling, TanStack Query for server state, API client generated from the backend's OpenAPI spec via orval. JWT stored in `localStorage`, attached per-request via an axios interceptor.
- **API style:** REST + OpenAPI. CORS enabled for `*.localhost` origins in dev (needs a real allow-list before production). Controller actions always return `ActionResult<T>`, never bare `IActionResult` — the latter gives the OpenAPI generator no static type to work with, silently breaking the generated frontend client (hit this bug twice already).

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
    Syntra.Tests/              Tenant isolation and branch integration tests
frontend/
  src/
    api/          Orval-generated API client (regenerate via `npm run codegen`, requires the API running) + the axios mutator
    auth/          Token storage, route guard
    components/   Shell layout
    pages/         Login, Dashboard, Products (+ detail/unit conversions), Units of Measure, Warehouses, Customers, Suppliers, Stock Balance, Stock Movements, Purchase Orders and Sales Orders (list/create/detail)
```

## Running it locally

Two terminals, both at once:

```bash
# Terminal 1 - backend API
cd backend/src/Syntra.Api
dotnet run --urls "http://localhost:5299"

# Terminal 2 - frontend
cd frontend
npm run dev
```

Then open `http://<tenant-slug>.localhost:5173` — the subdomain matters, it's how the backend resolves which tenant's database to use. A persistent demo tenant exists for exploring the UI: `demo.localhost:5173`, login `admin@demo.com` / `Demo123!`.

To regenerate the frontend's API client after a backend endpoint changes: `npm run codegen` (requires the API running).

## Status

**Phase 0 (foundations) — complete.** Platform DB schema, subdomain-based tenant resolution, migration orchestrator, tenant provisioning pipeline, tenant end-user auth (Identity + JWT), isolation tests, module folder structure.

**Phase 1 (single-tenant vertical slice) — complete.**
- [x] 1a — master data: units of measure, products (with unit conversions), warehouses, customers, suppliers
- [x] 1b — stock movement + balance (manual adjustments, movement history, on-hand balance)
- [x] 1c — purchase order flow (draft → confirm → receive, auto-generates stock movement)
- [x] 1d — sales order flow (draft → confirm, with an oversell check, auto-generates stock movement)

**Frontend track (parallel to the backend phases, not part of the 0-6 numbering) — complete.**
- [x] FE-1a — shell & auth: login, authenticated shell, JWT handling, API client codegen
- [x] FE-1b — master data screens: units of measure, products (+ unit conversions), warehouses, customers, suppliers
- [x] FE-1c — stock screens: balance view, filterable movement history, manual adjustment
- [x] FE-1d — purchase order screens: list, create (dynamic lines), detail with status-based actions (confirm/receive/cancel)
- [x] FE-1e — sales order screens: list, create (dynamic lines), detail; confirm surfaces the backend oversell message

**Phase 2 (multi-branch + tenant data isolation) — in progress.** A branch owns one or more warehouses; admins see every branch, other users only their assigned ones.
- [x] 2a — `Branch` entity, `Warehouse.BranchId`, default "Main" branch seeded by the `AddBranches` migration (existing warehouses are backfilled into it), branch CRUD at `/tenancy/branches`; the Warehouses screen got a minimal branch picker
- [ ] 2b — user-to-branch assignment, minimal user-management API
- [ ] 2c — branch scoping on warehouses, stock and orders; all-branches view for admins
- [ ] 2d — business-data isolation tests across two tenants, plus branch-scoping tests
- [ ] 2e — frontend: branch management, branch filter, user assignment
