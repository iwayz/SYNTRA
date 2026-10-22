using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Syntra.Domain.Inventory;
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

public record StockBalance(int ProductId, int WarehouseId, decimal QuantityOnHand);

[ApiController]
[Authorize]
[Route("inventory")]
public class StockController : ControllerBase
{
    private readonly TenantDbContext _tenantDb;

    public StockController(TenantDbContext tenantDb)
    {
        _tenantDb = tenantDb;
    }

    [HttpGet("stock-movements")]
    public async Task<ActionResult<IEnumerable<StockMovement>>> ListMovements(
        [FromQuery] int? productId,
        [FromQuery] int? warehouseId,
        [FromQuery] int? referenceId)
    {
        var query = _tenantDb.StockMovements.AsNoTracking();

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
    public async Task<ActionResult<IEnumerable<StockBalance>>> GetBalance([FromQuery] int? productId, [FromQuery] int? warehouseId)
    {
        var query = _tenantDb.StockMovements.AsNoTracking();

        if (productId is not null)
        {
            query = query.Where(m => m.ProductId == productId);
        }

        if (warehouseId is not null)
        {
            query = query.Where(m => m.WarehouseId == warehouseId);
        }

        var balances = await query
            .GroupBy(m => new { m.ProductId, m.WarehouseId })
            .Select(g => new StockBalance(g.Key.ProductId, g.Key.WarehouseId, g.Sum(m => m.Quantity)))
            .ToListAsync();

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

        var warehouseExists = await _tenantDb.Warehouses.AnyAsync(w => w.WarehouseId == request.WarehouseId);
        if (!warehouseExists)
        {
            return ValidationProblem($"Warehouse {request.WarehouseId} does not exist.");
        }

        var movement = new StockMovement
        {
            ProductId = request.ProductId,
            WarehouseId = request.WarehouseId,
            Quantity = request.Quantity,
            MovementType = MovementType.ManualAdjustment,
            ReferenceId = null,
            OccurredAt = DateTime.UtcNow,
            Notes = request.Notes
        };

        _tenantDb.StockMovements.Add(movement);
        await _tenantDb.SaveChangesAsync();

        return CreatedAtAction(nameof(ListMovements), new { productId = movement.ProductId }, movement);
    }
}
