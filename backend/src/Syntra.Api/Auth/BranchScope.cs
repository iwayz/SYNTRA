using Microsoft.EntityFrameworkCore;
using Syntra.Domain.Inventory;
using Syntra.Domain.Purchasing;
using Syntra.Domain.Sales;
using Syntra.Domain.Tenancy;
using Syntra.Infrastructure.TenantData;

namespace Syntra.Api.Auth;

// Branch scoping for reads. Admins are unscoped; everyone else is limited to
// their assigned branches (none assigned -> sees nothing). Stock movements and
// orders have no branch of their own - they reach it through their warehouse.
// The optional branchId is an extra narrowing filter on top of the scope, so a
// non-admin asking for a branch they don't have gets an empty result.
public static class BranchScopeExtensions
{
    // Null means "no restriction" (admin).
    private static int[]? ScopedBranchIds(UserAccess access) => access.IsAdmin ? null : access.BranchIds.ToArray();

    public static IQueryable<Branch> VisibleTo(this IQueryable<Branch> query, UserAccess access)
    {
        var ids = ScopedBranchIds(access);
        return ids is null ? query : query.Where(b => ids.Contains(b.BranchId));
    }

    public static IQueryable<Warehouse> VisibleTo(this IQueryable<Warehouse> query, UserAccess access, int? branchId = null)
    {
        var ids = ScopedBranchIds(access);

        if (ids is not null)
        {
            query = query.Where(w => ids.Contains(w.BranchId));
        }

        return branchId is null ? query : query.Where(w => w.BranchId == branchId);
    }

    public static IQueryable<StockMovement> VisibleTo(this IQueryable<StockMovement> query, UserAccess access, int? branchId = null)
    {
        var ids = ScopedBranchIds(access);

        if (ids is not null)
        {
            query = query.Where(m => ids.Contains(m.Warehouse!.BranchId));
        }

        return branchId is null ? query : query.Where(m => m.Warehouse!.BranchId == branchId);
    }

    public static IQueryable<PurchaseOrder> VisibleTo(this IQueryable<PurchaseOrder> query, UserAccess access, int? branchId = null)
    {
        var ids = ScopedBranchIds(access);

        if (ids is not null)
        {
            query = query.Where(o => ids.Contains(o.Warehouse!.BranchId));
        }

        return branchId is null ? query : query.Where(o => o.Warehouse!.BranchId == branchId);
    }

    public static IQueryable<SalesOrder> VisibleTo(this IQueryable<SalesOrder> query, UserAccess access, int? branchId = null)
    {
        var ids = ScopedBranchIds(access);

        if (ids is not null)
        {
            query = query.Where(o => ids.Contains(o.Warehouse!.BranchId));
        }

        return branchId is null ? query : query.Where(o => o.Warehouse!.BranchId == branchId);
    }

    public static IQueryable<StockCount> VisibleTo(this IQueryable<StockCount> query, UserAccess access, int? branchId = null)
    {
        var ids = ScopedBranchIds(access);

        if (ids is not null)
        {
            query = query.Where(c => ids.Contains(c.Warehouse.BranchId));
        }

        return branchId is null ? query : query.Where(c => c.Warehouse.BranchId == branchId);
    }

    // A transfer belongs to two branches. It is visible to users of either end
    // (the sender needs to follow it, the receiver needs to act on it); the
    // optional branchId matches either end too.
    public static IQueryable<StockTransfer> VisibleTo(this IQueryable<StockTransfer> query, UserAccess access, int? branchId = null)
    {
        var ids = ScopedBranchIds(access);

        if (ids is not null)
        {
            query = query.Where(t => ids.Contains(t.FromWarehouse.BranchId) || ids.Contains(t.ToWarehouse.BranchId));
        }

        return branchId is null
            ? query
            : query.Where(t => t.FromWarehouse.BranchId == branchId || t.ToWarehouse.BranchId == branchId);
    }

    // Whether the user can act in this branch (admins in every branch).
    public static bool CanAccessBranch(this UserAccess access, int branchId) =>
        access.IsAdmin || access.BranchIds.Contains(branchId);
}

public interface IBranchScope
{
    // For creating NEW activity (an order, a stock adjustment): the warehouse
    // must exist in a branch the caller can access, and that branch must be
    // active. A warehouse outside the caller's branches gets the same "does not
    // exist" error as a missing one, so its existence isn't revealed. Acting on
    // existing orders (confirm/receive/cancel) deliberately skips the
    // active-branch rule so in-flight work can still be closed out.
    Task<(Warehouse? Warehouse, string? Error)> ResolveWarehouseForNewActivityAsync(int warehouseId);
}

public class BranchScope : IBranchScope
{
    private readonly ICurrentUserAccess _userAccess;
    private readonly TenantDbContext _tenantDb;

    public BranchScope(ICurrentUserAccess userAccess, TenantDbContext tenantDb)
    {
        _userAccess = userAccess;
        _tenantDb = tenantDb;
    }

    public async Task<(Warehouse? Warehouse, string? Error)> ResolveWarehouseForNewActivityAsync(int warehouseId)
    {
        var access = await _userAccess.GetAsync();

        var warehouse = await _tenantDb.Warehouses.AsNoTracking()
            .Include(w => w.Branch)
            .VisibleTo(access)
            .FirstOrDefaultAsync(w => w.WarehouseId == warehouseId);

        if (warehouse is null)
        {
            return (null, $"Warehouse {warehouseId} does not exist.");
        }

        if (warehouse.Branch is { IsActive: false })
        {
            return (null, $"Branch '{warehouse.Branch.Name}' is inactive; no new activity can be recorded in it.");
        }

        return (warehouse, null);
    }
}
