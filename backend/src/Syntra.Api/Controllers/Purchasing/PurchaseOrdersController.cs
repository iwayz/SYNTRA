using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Syntra.Domain.Inventory;
using Syntra.Domain.Purchasing;
using Syntra.Infrastructure.TenantData;

namespace Syntra.Api.Controllers.Purchasing;

public record CreatePurchaseOrderLineRequest(int ProductId, int UnitOfMeasureId, decimal Quantity, decimal UnitPrice);

public class CreatePurchaseOrderLineRequestValidator : AbstractValidator<CreatePurchaseOrderLineRequest>
{
    public CreatePurchaseOrderLineRequestValidator()
    {
        RuleFor(x => x.ProductId).GreaterThan(0);
        RuleFor(x => x.UnitOfMeasureId).GreaterThan(0);
        RuleFor(x => x.Quantity).GreaterThan(0);
        RuleFor(x => x.UnitPrice).GreaterThanOrEqualTo(0);
    }
}

public record CreatePurchaseOrderRequest(
    int SupplierId,
    int WarehouseId,
    DateTime OrderDate,
    List<CreatePurchaseOrderLineRequest> Lines);

public class CreatePurchaseOrderRequestValidator : AbstractValidator<CreatePurchaseOrderRequest>
{
    public CreatePurchaseOrderRequestValidator()
    {
        RuleFor(x => x.SupplierId).GreaterThan(0);
        RuleFor(x => x.WarehouseId).GreaterThan(0);
        RuleFor(x => x.Lines).NotEmpty();
        RuleForEach(x => x.Lines).SetValidator(new CreatePurchaseOrderLineRequestValidator());
    }
}

[ApiController]
[Authorize]
[Route("purchasing/purchase-orders")]
public class PurchaseOrdersController : ControllerBase
{
    private readonly TenantDbContext _tenantDb;

    public PurchaseOrdersController(TenantDbContext tenantDb)
    {
        _tenantDb = tenantDb;
    }

    [HttpGet]
    public async Task<IActionResult> List()
    {
        var orders = await _tenantDb.PurchaseOrders.AsNoTracking()
            .Include(o => o.Supplier)
            .Include(o => o.Warehouse)
            .ToListAsync();

        return Ok(orders);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        var order = await _tenantDb.PurchaseOrders.AsNoTracking()
            .Include(o => o.Supplier)
            .Include(o => o.Warehouse)
            .Include(o => o.Lines).ThenInclude(l => l.Product)
            .Include(o => o.Lines).ThenInclude(l => l.UnitOfMeasure)
            .FirstOrDefaultAsync(o => o.PurchaseOrderId == id);

        return order is null ? NotFound() : Ok(order);
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreatePurchaseOrderRequest request)
    {
        var supplierExists = await _tenantDb.Suppliers.AnyAsync(s => s.SupplierId == request.SupplierId);
        if (!supplierExists)
        {
            return ValidationProblem($"Supplier {request.SupplierId} does not exist.");
        }

        var warehouseExists = await _tenantDb.Warehouses.AnyAsync(w => w.WarehouseId == request.WarehouseId);
        if (!warehouseExists)
        {
            return ValidationProblem($"Warehouse {request.WarehouseId} does not exist.");
        }

        var lines = new List<PurchaseOrderLine>();

        foreach (var lineRequest in request.Lines)
        {
            var product = await _tenantDb.Products.AsNoTracking()
                .FirstOrDefaultAsync(p => p.ProductId == lineRequest.ProductId);

            if (product is null)
            {
                return ValidationProblem($"Product {lineRequest.ProductId} does not exist.");
            }

            var unitExists = await _tenantDb.UnitOfMeasures.AnyAsync(u => u.UnitOfMeasureId == lineRequest.UnitOfMeasureId);
            if (!unitExists)
            {
                return ValidationProblem($"Unit of measure {lineRequest.UnitOfMeasureId} does not exist.");
            }

            if (lineRequest.UnitOfMeasureId != product.BaseUnitOfMeasureId)
            {
                var hasConversion = await _tenantDb.ProductUnitConversions.AnyAsync(c =>
                    c.ProductId == lineRequest.ProductId && c.UnitOfMeasureId == lineRequest.UnitOfMeasureId);

                if (!hasConversion)
                {
                    return ValidationProblem(
                        $"Product {lineRequest.ProductId} has no unit conversion defined for unit {lineRequest.UnitOfMeasureId}.");
                }
            }

            lines.Add(new PurchaseOrderLine
            {
                ProductId = lineRequest.ProductId,
                UnitOfMeasureId = lineRequest.UnitOfMeasureId,
                Quantity = lineRequest.Quantity,
                UnitPrice = lineRequest.UnitPrice
            });
        }

        var order = new PurchaseOrder
        {
            SupplierId = request.SupplierId,
            WarehouseId = request.WarehouseId,
            OrderDate = request.OrderDate,
            Status = PurchaseOrderStatus.Draft,
            Lines = lines
        };

        _tenantDb.PurchaseOrders.Add(order);
        await _tenantDb.SaveChangesAsync();

        return CreatedAtAction(nameof(Get), new { id = order.PurchaseOrderId }, order);
    }

    [HttpPost("{id:int}/confirm")]
    public async Task<IActionResult> Confirm(int id)
    {
        var order = await _tenantDb.PurchaseOrders.FirstOrDefaultAsync(o => o.PurchaseOrderId == id);
        if (order is null)
        {
            return NotFound();
        }

        if (order.Status != PurchaseOrderStatus.Draft)
        {
            return ValidationProblem($"Purchase order must be Draft to confirm; current status is {order.Status}.");
        }

        order.Status = PurchaseOrderStatus.Confirmed;
        await _tenantDb.SaveChangesAsync();

        return Ok(order);
    }

    [HttpPost("{id:int}/receive")]
    public async Task<IActionResult> Receive(int id)
    {
        var order = await _tenantDb.PurchaseOrders
            .Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.PurchaseOrderId == id);

        if (order is null)
        {
            return NotFound();
        }

        if (order.Status != PurchaseOrderStatus.Confirmed)
        {
            return ValidationProblem($"Purchase order must be Confirmed to receive; current status is {order.Status}.");
        }

        foreach (var line in order.Lines)
        {
            var product = await _tenantDb.Products.AsNoTracking()
                .FirstOrDefaultAsync(p => p.ProductId == line.ProductId);

            var factor = 1m;

            if (line.UnitOfMeasureId != product!.BaseUnitOfMeasureId)
            {
                var conversion = await _tenantDb.ProductUnitConversions.AsNoTracking().FirstOrDefaultAsync(c =>
                    c.ProductId == line.ProductId && c.UnitOfMeasureId == line.UnitOfMeasureId);

                factor = conversion!.ConversionFactor;
            }

            _tenantDb.StockMovements.Add(new StockMovement
            {
                ProductId = line.ProductId,
                WarehouseId = order.WarehouseId,
                Quantity = line.Quantity * factor,
                MovementType = MovementType.Purchase,
                ReferenceId = order.PurchaseOrderId,
                OccurredAt = DateTime.UtcNow
            });
        }

        order.Status = PurchaseOrderStatus.Received;
        await _tenantDb.SaveChangesAsync();

        return Ok(order);
    }

    [HttpPost("{id:int}/cancel")]
    public async Task<IActionResult> Cancel(int id)
    {
        var order = await _tenantDb.PurchaseOrders.FirstOrDefaultAsync(o => o.PurchaseOrderId == id);
        if (order is null)
        {
            return NotFound();
        }

        if (order.Status is not (PurchaseOrderStatus.Draft or PurchaseOrderStatus.Confirmed))
        {
            return ValidationProblem($"Purchase order cannot be cancelled from status {order.Status}.");
        }

        order.Status = PurchaseOrderStatus.Cancelled;
        await _tenantDb.SaveChangesAsync();

        return Ok(order);
    }
}
