using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Syntra.Api.Auth;
using Syntra.Domain.Inventory;
using Syntra.Domain.Sales;
using Syntra.Infrastructure.Inventory;
using Syntra.Infrastructure.TenantData;

namespace Syntra.Api.Controllers.Sales;

public record CreateSalesOrderLineRequest(int ProductId, int UnitOfMeasureId, decimal Quantity, decimal UnitPrice);

public class CreateSalesOrderLineRequestValidator : AbstractValidator<CreateSalesOrderLineRequest>
{
    public CreateSalesOrderLineRequestValidator()
    {
        RuleFor(x => x.ProductId).GreaterThan(0);
        RuleFor(x => x.UnitOfMeasureId).GreaterThan(0);
        RuleFor(x => x.Quantity).GreaterThan(0);
        RuleFor(x => x.UnitPrice).GreaterThanOrEqualTo(0);
    }
}

public record CreateSalesOrderRequest(
    int CustomerId,
    int WarehouseId,
    DateTime OrderDate,
    List<CreateSalesOrderLineRequest> Lines);

public class CreateSalesOrderRequestValidator : AbstractValidator<CreateSalesOrderRequest>
{
    public CreateSalesOrderRequestValidator()
    {
        RuleFor(x => x.CustomerId).GreaterThan(0);
        RuleFor(x => x.WarehouseId).GreaterThan(0);
        RuleFor(x => x.Lines).NotEmpty();
        RuleForEach(x => x.Lines).SetValidator(new CreateSalesOrderLineRequestValidator());
    }
}

[ApiController]
[Authorize]
[Route("sales/sales-orders")]
public class SalesOrdersController : ControllerBase
{
    private readonly TenantDbContext _tenantDb;
    private readonly ICurrentUserAccess _userAccess;
    private readonly IBranchScope _branchScope;
    private readonly IStockLedger _stockLedger;

    public SalesOrdersController(
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

    [HttpGet]
    public async Task<ActionResult<IEnumerable<SalesOrder>>> List([FromQuery] int? branchId)
    {
        var access = await _userAccess.GetAsync();

        var orders = await _tenantDb.SalesOrders.AsNoTracking()
            .VisibleTo(access, branchId)
            .Include(o => o.Customer)
            .Include(o => o.Warehouse)
            .ToListAsync();

        return Ok(orders);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<SalesOrder>> Get(int id)
    {
        var access = await _userAccess.GetAsync();

        var order = await _tenantDb.SalesOrders.AsNoTracking()
            .VisibleTo(access)
            .Include(o => o.Customer)
            .Include(o => o.Warehouse)
            .Include(o => o.Lines).ThenInclude(l => l.Product)
            .Include(o => o.Lines).ThenInclude(l => l.UnitOfMeasure)
            .FirstOrDefaultAsync(o => o.SalesOrderId == id);

        return order is null ? NotFound() : Ok(order);
    }

    [HttpPost]
    public async Task<ActionResult<SalesOrder>> Create(CreateSalesOrderRequest request)
    {
        var customerExists = await _tenantDb.Customers.AnyAsync(c => c.CustomerId == request.CustomerId);
        if (!customerExists)
        {
            return ValidationProblem($"Customer {request.CustomerId} does not exist.");
        }

        var (warehouse, warehouseError) = await _branchScope.ResolveWarehouseForNewActivityAsync(request.WarehouseId);
        if (warehouse is null)
        {
            return ValidationProblem(warehouseError!);
        }

        var lines = new List<SalesOrderLine>();

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

            lines.Add(new SalesOrderLine
            {
                ProductId = lineRequest.ProductId,
                UnitOfMeasureId = lineRequest.UnitOfMeasureId,
                Quantity = lineRequest.Quantity,
                UnitPrice = lineRequest.UnitPrice
            });
        }

        var order = new SalesOrder
        {
            CustomerId = request.CustomerId,
            WarehouseId = request.WarehouseId,
            OrderDate = request.OrderDate,
            Status = SalesOrderStatus.Draft,
            Lines = lines
        };

        _tenantDb.SalesOrders.Add(order);
        await _tenantDb.SaveChangesAsync();

        return CreatedAtAction(nameof(Get), new { id = order.SalesOrderId }, order);
    }

    [HttpPost("{id:int}/confirm")]
    public async Task<ActionResult<SalesOrder>> Confirm(int id)
    {
        var access = await _userAccess.GetAsync();

        // This first look only tells us which stock to lock (and enforces branch
        // scope). Everything that decides the outcome is re-read after the locks
        // are held, so a concurrent confirm can't act on stale status or balances.
        var preview = await _tenantDb.SalesOrders.AsNoTracking()
            .VisibleTo(access)
            .Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.SalesOrderId == id);

        if (preview is null)
        {
            return NotFound();
        }

        await using var stock = await _stockLedger.BeginAsync(
            preview.Lines.Select(l => (l.ProductId, preview.WarehouseId)));

        var order = await _tenantDb.SalesOrders
            .Include(o => o.Lines)
            .FirstAsync(o => o.SalesOrderId == id);

        if (order.Status != SalesOrderStatus.Draft)
        {
            return ValidationProblem($"Sales order must be Draft to confirm; current status is {order.Status}.");
        }

        // Pass 1: validate every line against on-hand balance, tracking how
        // much this order has already claimed per product so two lines for
        // the same product can't each pass against the same starting
        // balance and together oversell.
        var baseQuantities = new Dictionary<int, decimal>();
        var committedSoFar = new Dictionary<int, decimal>();
        var errors = new List<string>();

        // Batch-tracked products are issued first-expiry-first-out from a working
        // copy of the warehouse's batches that every line for the product shares,
        // so later lines only see what earlier lines left.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var batchPools = new Dictionary<int, List<BatchStock>>();
        var allocations = new Dictionary<int, IReadOnlyList<BatchAllocation>>();

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

            var baseQuantity = line.Quantity * factor;
            baseQuantities[line.SalesOrderLineId] = baseQuantity;

            if (product.BatchTracking != BatchTracking.None)
            {
                if (!batchPools.TryGetValue(line.ProductId, out var pool))
                {
                    pool = await stock.BatchStockAsync(line.ProductId, order.WarehouseId);
                    batchPools[line.ProductId] = pool;
                }

                var allocation = FefoAllocator.TryAllocate(baseQuantity, pool, today);
                if (allocation is null)
                {
                    var expired = FefoAllocator.ExpiredQuantity(pool, today);
                    errors.Add(
                        $"Insufficient stock for product {line.ProductId} at warehouse {order.WarehouseId}: " +
                        $"requested {Quantities.Format(baseQuantity)}, available {Quantities.Format(FefoAllocator.SellableQuantity(pool, today))}" +
                        (expired > 0 ? $" (a further {Quantities.Format(expired)} is expired and cannot be sold)." : "."));
                }
                else
                {
                    allocations[line.SalesOrderLineId] = allocation;
                }

                continue;
            }

            var currentBalance = await stock.OnHandAsync(line.ProductId, order.WarehouseId);

            var alreadyCommitted = committedSoFar.GetValueOrDefault(line.ProductId);
            var available = currentBalance - alreadyCommitted;

            if (baseQuantity > available)
            {
                errors.Add(
                    $"Insufficient stock for product {line.ProductId} at warehouse {order.WarehouseId}: " +
                    $"requested {Quantities.Format(baseQuantity)}, available {Quantities.Format(available)}.");
            }

            committedSoFar[line.ProductId] = alreadyCommitted + baseQuantity;
        }

        if (errors.Count > 0)
        {
            return ValidationProblem(string.Join(" ", errors));
        }

        // Pass 2: all lines passed - create every movement, no partial fulfillment.
        foreach (var line in order.Lines)
        {
            if (allocations.TryGetValue(line.SalesOrderLineId, out var lineAllocations))
            {
                // One movement per batch the line was drawn from.
                foreach (var allocation in lineAllocations)
                {
                    stock.Post(
                        line.ProductId,
                        order.WarehouseId,
                        -allocation.Quantity,
                        MovementType.Sale,
                        order.SalesOrderId,
                        batchId: allocation.BatchId);
                }

                continue;
            }

            stock.Post(
                line.ProductId,
                order.WarehouseId,
                -baseQuantities[line.SalesOrderLineId],
                MovementType.Sale,
                order.SalesOrderId);
        }

        order.Status = SalesOrderStatus.Confirmed;
        await stock.CommitAsync();

        return Ok(order);
    }

    [HttpPost("{id:int}/cancel")]
    public async Task<ActionResult<SalesOrder>> Cancel(int id)
    {
        var access = await _userAccess.GetAsync();
        var order = await _tenantDb.SalesOrders.VisibleTo(access).FirstOrDefaultAsync(o => o.SalesOrderId == id);
        if (order is null)
        {
            return NotFound();
        }

        if (order.Status != SalesOrderStatus.Draft)
        {
            return ValidationProblem($"Sales order can only be cancelled from Draft; current status is {order.Status}.");
        }

        order.Status = SalesOrderStatus.Cancelled;
        await _tenantDb.SaveChangesAsync();

        return Ok(order);
    }
}
