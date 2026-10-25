using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Syntra.Domain.Inventory;
using Syntra.Infrastructure.TenantData;

namespace Syntra.Infrastructure.Inventory;

public class StockLockTimeoutException : Exception
{
    public StockLockTimeoutException()
        : base("Stock for this item is being updated by another request. Please retry.")
    {
    }
}

// The single door for anything that checks stock and then changes it. On-hand
// is a SUM over movements, so "read the balance, then insert movements" is a
// race: two requests can read the same balance and both write. BeginAsync
// opens a transaction and takes an exclusive application lock per
// (product, warehouse) that is held until the transaction ends, so competing
// requests for the same stock run one after another and each sees the other's
// committed result.
public interface IStockLedger
{
    // Locks are taken in a fixed order (warehouse, then product) so two
    // requests needing overlapping sets can't deadlock each other.
    Task<StockTransaction> BeginAsync(IEnumerable<(int ProductId, int WarehouseId)> keys);
}

public class StockLedger : IStockLedger
{
    private const int LockTimeoutMilliseconds = 15000;

    private readonly TenantDbContext _tenantDb;

    public StockLedger(TenantDbContext tenantDb)
    {
        _tenantDb = tenantDb;
    }

    public async Task<StockTransaction> BeginAsync(IEnumerable<(int ProductId, int WarehouseId)> keys)
    {
        var ordered = keys.Distinct().OrderBy(k => k.WarehouseId).ThenBy(k => k.ProductId).ToList();

        var transaction = await _tenantDb.Database.BeginTransactionAsync();

        try
        {
            foreach (var (productId, warehouseId) in ordered)
            {
                await AcquireAsync(productId, warehouseId);
            }
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }

        return new StockTransaction(_tenantDb, transaction);
    }

    private async Task AcquireAsync(int productId, int warehouseId)
    {
        var resource = $"syntra-stock:{warehouseId}:{productId}";

        // sp_getapplock returns 0/1 when granted and a negative code on timeout,
        // deadlock or error. LockOwner = Transaction releases it at commit/rollback.
        var result = (await _tenantDb.Database
                .SqlQuery<int>($@"DECLARE @result int;
EXEC @result = sp_getapplock @Resource = {resource}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = {LockTimeoutMilliseconds};
SELECT @result AS [Value]")
                .ToListAsync())
            .Single();

        if (result < 0)
        {
            throw new StockLockTimeoutException();
        }
    }
}

public sealed class StockTransaction : IAsyncDisposable
{
    private readonly TenantDbContext _tenantDb;
    private readonly IDbContextTransaction _transaction;

    internal StockTransaction(TenantDbContext tenantDb, IDbContextTransaction transaction)
    {
        _tenantDb = tenantDb;
        _transaction = transaction;
    }

    // Reads committed movements only; anything added with Post in this
    // transaction is not visible until CommitAsync, so callers that post several
    // movements for one product must track their own running total.
    public async Task<decimal> OnHandAsync(int productId, int warehouseId) =>
        await _tenantDb.StockMovements.AsNoTracking()
            .Where(m => m.ProductId == productId && m.WarehouseId == warehouseId)
            .SumAsync(m => m.Quantity);

    // Quantity of one batch in one warehouse (committed movements only).
    public async Task<decimal> OnHandInBatchAsync(int productId, int warehouseId, int batchId) =>
        await _tenantDb.StockMovements.AsNoTracking()
            .Where(m => m.ProductId == productId && m.WarehouseId == warehouseId && m.BatchId == batchId)
            .SumAsync(m => m.Quantity);

    // The batches of a product that currently hold stock in a warehouse, as a
    // working copy for FEFO allocation.
    public async Task<List<BatchStock>> BatchStockAsync(int productId, int warehouseId)
    {
        var rows = await _tenantDb.StockMovements.AsNoTracking()
            .Where(m => m.ProductId == productId && m.WarehouseId == warehouseId && m.BatchId != null)
            .GroupBy(m => m.BatchId!.Value)
            .Select(g => new { BatchId = g.Key, Quantity = g.Sum(m => m.Quantity) })
            .Where(x => x.Quantity > 0)
            .ToListAsync();

        var ids = rows.Select(r => r.BatchId).ToList();

        var batches = await _tenantDb.ProductBatches.AsNoTracking()
            .Where(b => ids.Contains(b.ProductBatchId))
            .ToDictionaryAsync(b => b.ProductBatchId);

        return rows.Select(r => new BatchStock
        {
            BatchId = r.BatchId,
            ExpiryDate = batches[r.BatchId].ExpiryDate,
            CreatedAt = batches[r.BatchId].CreatedAt,
            Remaining = r.Quantity
        }).ToList();
    }

    public StockMovement Post(
        int productId,
        int warehouseId,
        decimal quantity,
        MovementType movementType,
        int? referenceId,
        string? notes = null,
        int? batchId = null,
        ProductBatch? batch = null)
    {
        var movement = new StockMovement
        {
            ProductId = productId,
            WarehouseId = warehouseId,
            Quantity = quantity,
            MovementType = movementType,
            ReferenceId = referenceId,
            BatchId = batchId,
            Batch = batch,
            OccurredAt = DateTime.UtcNow,
            Notes = notes
        };

        _tenantDb.StockMovements.Add(movement);
        return movement;
    }

    // Saves everything pending on the context (movements and the document
    // changes made alongside them) and releases the locks. Disposing without
    // committing rolls everything back.
    public async Task CommitAsync()
    {
        await _tenantDb.SaveChangesAsync();
        await _transaction.CommitAsync();
    }

    public ValueTask DisposeAsync() => _transaction.DisposeAsync();
}
