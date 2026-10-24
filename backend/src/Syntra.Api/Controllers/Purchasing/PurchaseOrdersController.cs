using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Syntra.Api.Auth;
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
    private readonly ICurrentUserAccess _userAccess;
    private readonly IBranchScope _branchScope;

    public PurchaseOrdersController(TenantDbContext tenantDb, ICurrentUserAccess userAccess, IBranchScope branchScope)
    {
        _tenantDb = tenantDb;
        _userAccess = userAccess;
        _branchScope = branchScope;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<PurchaseOrder>>> List([FromQuery] int? branchId)
    {
        var access = await _userAccess.GetAsync();

        var orders = await _tenantDb.PurchaseOrders.AsNoTracking()
            .VisibleTo(access, branchId)
            .Include(o => o.Supplier)
            .Include(o => o.Warehouse)
            .ToListAsync();

        return Ok(orders);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<PurchaseOrder>> Get(int id)
    {
        var access = await _userAccess.GetAsync();

        var order = await _tenantDb.PurchaseOrders.AsNoTracking()
            .VisibleTo(access)
            .Include(o => o.Supplier)
            .Include(o => o.Warehouse)
            .Include(o => o.Lines).ThenInclude(l => l.Product)
            .Include(o => o.Lines).ThenInclude(l => l.UnitOfMeasure)
            .FirstOrDefaultAsync(o => o.PurchaseOrderId == id);

        return order is null ? NotFound() : Ok(order);
    }

    [HttpPost]
    public async Task<ActionResult<PurchaseOrder>> Create(CreatePurchaseOrderRequest request)
    {
        var supplierExists = await _tenantDb.Suppliers.AnyAsync(s => s.SupplierId == request.SupplierId);
        if (!supplierExists)
        {
            return ValidationProblem($"Supplier {request.SupplierId} does not exist.");
        }

        var (warehouse, warehouseError) = await _branchScope.ResolveWarehouseForNewActivityAsync(request.WarehouseId);
        if (warehouse is null)
        {
            return ValidationProblem(warehouseError!);
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
    public async Task<ActionResult<PurchaseOrder>> Confirm(int id)
    {
        var access = await _userAccess.GetAsync();
        var order = await _tenantDb.PurchaseOrders.VisibleTo(access).FirstOrDefaultAsync(o => o.PurchaseOrderId == id);
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
    public async Task<ActionResult<PurchaseOrder>> Receive(int id)
    {
        var access = await _userAccess.GetAsync();

        var order = await _tenantDb.PurchaseOrders
            .VisibleTo(access)
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
    public async Task<ActionResult<PurchaseOrder>> Cancel(int id)
    {
        var access = await _userAccess.GetAsync();
        var order = await _tenantDb.PurchaseOrders.VisibleTo(access).FirstOrDefaultAsync(o => o.PurchaseOrderId == id);
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
