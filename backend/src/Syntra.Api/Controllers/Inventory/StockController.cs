using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Syntra.Api.Auth;
using Syntra.Domain.Inventory;
using Syntra.Infrastructure.Inventory;
using Syntra.Infrastructure.TenantData;

namespace Syntra.Api.Controllers.Inventory;

public record CreateStockAdjustmentRequest(
    int ProductId,
    int WarehouseId,
    decimal Quantity,
    string Notes,
    string? BatchNumber = null,
    DateOnly? ExpiryDate = null);

public class CreateStockAdjustmentRequestValidator : AbstractValidator<CreateStockAdjustmentRequest>
{
    public CreateStockAdjustmentRequestValidator()
    {
        RuleFor(x => x.ProductId).GreaterThan(0);
        RuleFor(x => x.WarehouseId).GreaterThan(0);
        RuleFor(x => x.Quantity).NotEqual(0);
        RuleFor(x => x.Notes).NotEmpty().MaximumLength(500);
        RuleFor(x => x.BatchNumber).MaximumLength(BatchResolver.MaxBatchNumberLength);
    }
}

// For batch-tracked products there is one row per batch (and BatchId/BatchNumber
// are set); other products have a single row per warehouse with the batch fields null.
public record StockBalance(
    int ProductId,
    string ProductSku,
    string ProductName,
    int WarehouseId,
    string WarehouseName,
    int BranchId,
    string BranchName,
    decimal QuantityOnHand,
    int? BatchId,
    string? BatchNumber,
    DateOnly? ExpiryDate);

[ApiController]
[Authorize]
[Route("inventory")]
public class StockController : ControllerBase
{
    private readonly TenantDbContext _tenantDb;
    private readonly ICurrentUserAccess _userAccess;
    private readonly IBranchScope _branchScope;
    private readonly IStockLedger _stockLedger;
    private readonly IBatchResolver _batchResolver;

    public StockController(
        TenantDbContext tenantDb,
        ICurrentUserAccess userAccess,
        IBranchScope branchScope,
        IStockLedger stockLedger,
        IBatchResolver batchResolver)
    {
        _tenantDb = tenantDb;
        _userAccess = userAccess;
        _branchScope = branchScope;
        _stockLedger = stockLedger;
        _batchResolver = batchResolver;
    }

    [HttpGet("stock-movements")]
    public async Task<ActionResult<IEnumerable<StockMovement>>> ListMovements(
        [FromQuery] int? productId,
        [FromQuery] int? warehouseId,
        [FromQuery] int? referenceId,
        [FromQuery] int? branchId)
    {
        var access = await _userAccess.GetAsync();

        var query = _tenantDb.StockMovements.AsNoTracking()
            .VisibleTo(access, branchId)
            .Include(m => m.Product)
            .Include(m => m.Warehouse)
            .Include(m => m.Batch)
            .AsQueryable();

        if (productId is not null)
        {
            query = query.Where(m => m.ProductId == productId);
        }

        if (warehouseId is not null)
        {
            query = query.Where(m => m.WarehouseId == warehouseId);
        }

        if (referenceId is not null)
        {
            query = query.Where(m => m.ReferenceId == referenceId);
        }

        var movements = await query.OrderByDescending(m => m.OccurredAt).ToListAsync();
        return Ok(movements);
    }

    [HttpGet("stock-balance")]
    public async Task<ActionResult<IEnumerable<StockBalance>>> GetBalance(
        [FromQuery] int? productId,
        [FromQuery] int? warehouseId,
        [FromQuery] int? branchId)
    {
        var access = await _userAccess.GetAsync();
        var query = _tenantDb.StockMovements.AsNoTracking().VisibleTo(access, branchId);

        if (productId is not null)
        {
            query = query.Where(m => m.ProductId == productId);
        }

        if (warehouseId is not null)
        {
            query = query.Where(m => m.WarehouseId == warehouseId);
        }

        var rawBalances = (await query
                .GroupBy(m => new { m.ProductId, m.WarehouseId, m.BatchId })
                .Select(g => new { g.Key.ProductId, g.Key.WarehouseId, g.Key.BatchId, Quantity = g.Sum(m => m.Quantity) })
                .ToListAsync())
            // A batch that has been fully used up is history, not stock; plain
            // (unbatched) rows keep showing even at zero, as they always have.
            .Where(b => b.BatchId is null || b.Quantity != 0)
            .ToList();

        var productIds = rawBalances.Select(b => b.ProductId).Distinct().ToList();
        var warehouseIds = rawBalances.Select(b => b.WarehouseId).Distinct().ToList();
        var batchIds = rawBalances.Where(b => b.BatchId is not null).Select(b => b.BatchId!.Value).Distinct().ToList();

        var products = await _tenantDb.Products.AsNoTracking()
            .Where(p => productIds.Contains(p.ProductId))
            .ToDictionaryAsync(p => p.ProductId);

        var warehouses = await _tenantDb.Warehouses.AsNoTracking()
            .Include(w => w.Branch)
            .Where(w => warehouseIds.Contains(w.WarehouseId))
            .ToDictionaryAsync(w => w.WarehouseId);

        var batches = await _tenantDb.ProductBatches.AsNoTracking()
            .Where(b => batchIds.Contains(b.ProductBatchId))
            .ToDictionaryAsync(b => b.ProductBatchId);

        var balances = rawBalances
            .Select(b => new StockBalance(
                b.ProductId,
                products[b.ProductId].Sku,
                products[b.ProductId].Name,
                b.WarehouseId,
                warehouses[b.WarehouseId].Name,
                warehouses[b.WarehouseId].BranchId,
                warehouses[b.WarehouseId].Branch!.Name,
                b.Quantity,
                b.BatchId,
                b.BatchId is null ? null : batches[b.BatchId.Value].BatchNumber,
                b.BatchId is null ? null : batches[b.BatchId.Value].ExpiryDate))
            .OrderBy(b => b.ProductName)
            .ThenBy(b => b.WarehouseName)
            .ThenBy(b => b.ExpiryDate == null)
            .ThenBy(b => b.ExpiryDate)
            .ThenBy(b => b.BatchNumber)
            .ToList();

        return Ok(balances);
    }

    [HttpPost("stock-adjustments")]
    public async Task<ActionResult<StockMovement>> CreateAdjustment(CreateStockAdjustmentRequest request)
    {
        var productExists = await _tenantDb.Products.AnyAsync(p => p.ProductId == request.ProductId);
        if (!productExists)
        {
            return ValidationProblem($"Product {request.ProductId} does not exist.");
        }

        var (warehouse, warehouseError) = await _branchScope.ResolveWarehouseForNewActivityAsync(request.WarehouseId);
        if (warehouse is null)
        {
            return ValidationProblem(warehouseError!);
        }

        await using var stock = await _stockLedger.BeginAsync([(request.ProductId, request.WarehouseId)]);

        // Read only after the locks are held: switching a product to batch
        // tracking takes the same locks, so the mode cannot change under us.
        var product = await _tenantDb.Products.AsNoTracking().FirstAsync(p => p.ProductId == request.ProductId);

        ProductBatch? batch;
        string? batchError;

        if (request.Quantity > 0)
        {
            (batch, batchError) = await _batchResolver.ResolveForStockInAsync(product, request.BatchNumber, request.ExpiryDate);
        }
        else
        {
            (batch, batchError) = request.ExpiryDate is not null
                ? (null, "An expiry date only applies when adding stock.")
                : await _batchResolver.FindForStockOutAsync(product, request.BatchNumber);
        }

        if (batchError is not null)
        {
            return ValidationProblem(batchError);
        }

        // A reduction may not take stock below zero (the same rule sales orders
        // follow) - per batch for a tracked product; corrections to the other
        // side are always fine.
        if (request.Quantity < 0)
        {
            var onHand = batch is null
                ? await stock.OnHandAsync(request.ProductId, request.WarehouseId)
                : await stock.OnHandInBatchAsync(request.ProductId, request.WarehouseId, batch.ProductBatchId);

            if (onHand + request.Quantity < 0)
            {
                var where = batch is null ? "" : $" in batch '{batch.BatchNumber}'";
                return ValidationProblem(
                    $"Cannot reduce stock by {Quantities.Format(-request.Quantity)}: only {Quantities.Format(onHand)} is on hand{where} for product " +
                    $"{request.ProductId} at warehouse {request.WarehouseId}.");
            }
        }

        var movement = stock.Post(
            request.ProductId,
            request.WarehouseId,
            request.Quantity,
            MovementType.ManualAdjustment,
            referenceId: null,
            request.Notes,
            batch: batch);

        await stock.CommitAsync();

        return CreatedAtAction(nameof(ListMovements), new { productId = movement.ProductId }, movement);
    }
}
