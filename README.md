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
    auth/          Token storage, route guards (sign-in, admin-only), current-user hook
    branch/       Header branch switcher and the branch context pages read from
    components/   Shell layout
    pages/         Login, Dashboard, Products (+ detail/unit conversions), Units of Measure, Warehouses, Customers, Suppliers, Stock Balance, Stock Movements, Purchase Orders and Sales Orders (list/create/detail), Branches and Users (admin), Account (change password)
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

Then open `http://<tenant-slug>.localhost:5173` — the subdomain matters, it's how the backend resolves which tenant's database to use. Two persistent demo tenants exist for exploring the UI: `demo.localhost:5173` (login `admin@demo.com` / `Demo123!`, has sample data) and `demo2.localhost:5173` (login `admin@demo2.com` / `Demo123!`, starts empty - handy for seeing that tenants never share data).

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

**Phase 2 (multi-branch + tenant data isolation) — complete.** A branch owns one or more warehouses; admins see every branch, other users only their assigned ones.
- [x] 2a — `Branch` entity, `Warehouse.BranchId`, default "Main" branch seeded by the `AddBranches` migration (existing warehouses are backfilled into it), branch CRUD at `/tenancy/branches`; the Warehouses screen got a minimal branch picker
- [x] 2b — user-to-branch assignment (`UserBranches`), admin-only user API at `/tenancy/users` (create, activate/deactivate, promote/demote, set branches, reset password), `POST /auth/change-password`; admin status, active flag and branches are read from the database on every request, not from the JWT. Assignments are stored but not yet enforced on stock/orders (that is 2c)
- [x] 2c — branch scoping: non-admins only see and act on their assigned branches (branches, warehouses, stock movements/balance, purchase and sales orders; out-of-scope items return 404, creating in another branch is refused); admins see everything; optional `branchId` filter on the lists; stock balance rows carry the branch; warehouse creation is admin-only; an inactive branch blocks new warehouses/orders/adjustments but existing orders can still be closed out
- [x] 2d — cross-tenant business-data isolation tests (two seeded tenants with colliding ids and shared SKU/codes/email: list and by-id sweeps, per-tenant uniqueness, separate credentials, write isolation, concurrent reads/writes, direct database check, host-header spoofing) plus a second persistent demo tenant, `demo2`, to compare against `demo`. Branch-scoping tests shipped with 2c
- [x] 2e — frontend:
  - [x] 2e-1 — current-user hook, admin-only nav and route guard, no-branch banner, Branches page (create/edit/deactivate), Account page (change own password), Dashboard showing role and visible branches
  - [x] 2e-2 — Users page (admin): list with role/status/branches, create form, per-row edit branches, activate/deactivate, make/remove admin, reset password; actions on your own row are disabled
  - [x] 2e-3 — global branch switcher in the header (remembered per user, hidden with one branch) that narrows every list and the warehouse dropdowns; Branch columns on stock and order screens; create forms show the server's real error and grey out warehouses in inactive branches; the add-warehouse form is hidden from non-admins

**Phase 3 (full inventory ops) — in progress.**
- [x] 3a — safe stock writes and goods receipts: every stock check-then-write now goes through one `StockLedger` that holds a per-product, per-warehouse lock (fixes a real race where concurrent sales confirms could oversell, or confirm the same order twice); order status is a concurrency token so conflicting changes return 409; manual adjustments can no longer take stock below zero; goods receipts allow partial deliveries with per-line notes, POs gain a `PartiallyReceived` status and per-line received/outstanding quantities, and a partially received order can be closed when the rest will not arrive
- [x] 3b — batch tracking: per-product mode (not tracked / by batch / by batch + expiry); `ProductBatch` and a batch on every movement of a tracked product; goods receipts and manual adjustments capture batch (and expiry for new batches), one PO line can be split across batches; sales issue first-expiry-first-out and never sell expired batches; switching tracking on moves existing stock into an `OPENING` batch (one-way); balances, movements and the product screen show batches with expired / expiring-soon highlighting; admins can correct a batch expiry
- [x] 3c — transfers between warehouses: draft, dispatch (stock leaves the source, first-expiry-first-out or a named batch), receive (in several steps, same batches arrive), close short (the remainder is written off as lost in transit) or recall while nothing has arrived; works between branches (sender acts on the source, receiver on the destination, both see it; staff pick the destination from a names-only lookup); in-transit stock is derived and shown on Stock Balance
- [x] 3d — stock counts (opname): a count sheet per warehouse snapshotting its stock (per batch for tracked products); branch staff enter counted quantities row by row while trading continues, and a row whose stock moves after it was counted is flagged stale and must be counted again; an admin reviews and posts, which applies each counted row's difference as a stock-count movement (expired batches are written off by counting them at 0); uncounted rows are left unchanged; one open count per warehouse
- [ ] 3e — returns
