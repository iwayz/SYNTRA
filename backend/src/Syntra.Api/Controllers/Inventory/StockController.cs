using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Syntra.Api.Auth;
using Syntra.Domain.Inventory;
using Syntra.Infrastructure.Inventory;
using Syntra.Infrastructure.TenantData;

namespace Syntra.Api.Controllers.Inventory;

public record CreateStockAdjustmentRequest(int ProductId, int WarehouseId, decimal Quantity, string Notes);

public class CreateStockAdjustmentRequestValidator : AbstractValidator<CreateStockAdjustmentRequest>
{
    public CreateStockAdjustmentRequestValidator()
    {
        RuleFor(x => x.ProductId).GreaterThan(0);
        RuleFor(x => x.WarehouseId).GreaterThan(0);
        RuleFor(x => x.Quantity).NotEqual(0);
        RuleFor(x => x.Notes).NotEmpty().MaximumLength(500);
    }
}

public record StockBalance(
    int ProductId,
    string ProductSku,
    string ProductName,
    int WarehouseId,
    string WarehouseName,
    int BranchId,
    string BranchName,
    decimal QuantityOnHand);

[ApiController]
[Authorize]
[Route("inventory")]
public class StockController : ControllerBase
{
    private readonly TenantDbContext _tenantDb;
    private readonly ICurrentUserAccess _userAccess;
    private readonly IBranchScope _branchScope;
    private readonly IStockLedger _stockLedger;

    public StockController(
        TenantDbContext tenantDb,
        ICurrentUserAccess userAccess,
        IBranchScope branchScope,
        IStockLedger stockLedger)
    {
        _tenantDb = tenantDb;
        _userAccess = userAccess;
        _branchScope = branchScope;
        _stockLedger = stockLedger;
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

        var rawBalances = await query
            .GroupBy(m => new { m.ProductId, m.WarehouseId })
            .Select(g => new { g.Key.ProductId, g.Key.WarehouseId, Quantity = g.Sum(m => m.Quantity) })
            .ToListAsync();

        var productIds = rawBalances.Select(b => b.ProductId).Distinct().ToList();
        var warehouseIds = rawBalances.Select(b => b.WarehouseId).Distinct().ToList();

        var products = await _tenantDb.Products.AsNoTracking()
            .Where(p => productIds.Contains(p.ProductId))
            .ToDictionaryAsync(p => p.ProductId);

        var warehouses = await _tenantDb.Warehouses.AsNoTracking()
            .Include(w => w.Branch)
            .Where(w => warehouseIds.Contains(w.WarehouseId))
            .ToDictionaryAsync(w => w.WarehouseId);

        var balances = rawBalances.Select(b => new StockBalance(
            b.ProductId,
            products[b.ProductId].Sku,
            products[b.ProductId].Name,
            b.WarehouseId,
            warehouses[b.WarehouseId].Name,
            warehouses[b.WarehouseId].BranchId,
            warehouses[b.WarehouseId].Branch!.Name,
            b.Quantity)).ToList();

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

        // A reduction may not take the warehouse below zero (the same rule sales
        // orders follow); corrections to the other side are always fine.
        if (request.Quantity < 0)
        {
            var onHand = await stock.OnHandAsync(request.ProductId, request.WarehouseId);
            if (onHand + request.Quantity < 0)
            {
                return ValidationProblem(
                    $"Cannot reduce stock by {-request.Quantity}: only {onHand} is on hand for product " +
                    $"{request.ProductId} at warehouse {request.WarehouseId}.");
            }
        }

        var movement = stock.Post(
            request.ProductId,
            request.WarehouseId,
            request.Quantity,
            MovementType.ManualAdjustment,
            referenceId: null,
            request.Notes);

        await stock.CommitAsync();

        return CreatedAtAction(nameof(ListMovements), new { productId = movement.ProductId }, movement);
    }
}
