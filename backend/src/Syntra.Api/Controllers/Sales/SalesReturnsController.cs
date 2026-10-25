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

public record SalesReturnLineRequest(
    int SalesOrderLineId,
    decimal Quantity,
    string? BatchNumber,
    ReturnDisposition Disposition);

public record CreateSalesReturnRequest(string Reason, DateTime? ReturnDate, List<SalesReturnLineRequest> Lines);

public class SalesReturnLineRequestValidator : AbstractValidator<SalesReturnLineRequest>
{
    public SalesReturnLineRequestValidator()
    {
        RuleFor(x => x.SalesOrderLineId).GreaterThan(0);
        RuleFor(x => x.Quantity).GreaterThan(0);
        RuleFor(x => x.BatchNumber).MaximumLength(BatchResolver.MaxBatchNumberLength);
        RuleFor(x => x.Disposition).IsInEnum();
    }
}

public class CreateSalesReturnRequestValidator : AbstractValidator<CreateSalesReturnRequest>
{
    public CreateSalesReturnRequestValidator()
    {
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Lines).NotEmpty();
        RuleForEach(x => x.Lines).SetValidator(new SalesReturnLineRequestValidator());
    }
}

// Goods coming back from a customer. Anyone who can see the order's branch can record
// one. Restocked goods go back into the order's own warehouse; written-off goods are
// recorded but never enter stock.
[ApiController]
[Authorize]
[Route("sales/sales-orders/{orderId:int}")]
public class SalesReturnsController : ControllerBase
{
    private readonly TenantDbContext _tenantDb;
    private readonly ICurrentUserAccess _userAccess;
    private readonly IStockLedger _stockLedger;
    private readonly IBatchResolver _batchResolver;

    public SalesReturnsController(
        TenantDbContext tenantDb,
        ICurrentUserAccess userAccess,
        IStockLedger stockLedger,
        IBatchResolver batchResolver)
    {
        _tenantDb = tenantDb;
        _userAccess = userAccess;
        _stockLedger = stockLedger;
        _batchResolver = batchResolver;
    }

    // What can still be returned, per line and (for tracked products) per batch.
    [HttpGet("returnable")]
    public async Task<ActionResult<IEnumerable<ReturnableLine>>> Returnable(int orderId)
    {
        var access = await _userAccess.GetAsync();

        var order = await _tenantDb.SalesOrders.AsNoTracking()
            .VisibleTo(access)
            .Include(o => o.Lines).ThenInclude(l => l.Product)
            .Include(o => o.Lines).ThenInclude(l => l.UnitOfMeasure)
            .FirstOrDefaultAsync(o => o.SalesOrderId == orderId);

        if (order is null)
        {
            return NotFound();
        }

        if (order.Status != SalesOrderStatus.Confirmed)
        {
            return ValidationProblem($"Only a Confirmed sales order has goods to return; this one is {order.Status}.");
        }

        var sold = await SoldByBatchAsync(orderId);
        var returned = await ReturnedByBatchAsync(orderId);

        var batchIds = sold.Keys.Select(k => k.BatchId).Distinct().ToList();
        var batches = await _tenantDb.ProductBatches.AsNoTracking()
            .Where(b => batchIds.Contains(b.ProductBatchId))
            .ToDictionaryAsync(b => b.ProductBatchId);

        var lines = new List<ReturnableLine>();

        foreach (var line in order.Lines)
        {
            var tracked = line.Product.BatchTracking != BatchTracking.None;
            var factor = await UnitFactors.ToBaseAsync(_tenantDb, line.Product, line.UnitOfMeasureId);

            var batchRows = tracked
                ? sold.Where(s => s.Key.ProductId == line.ProductId)
                    .Select(s => new
                    {
                        s.Key.BatchId,
                        Base = s.Value - returned.GetValueOrDefault(s.Key)
                    })
                    .Where(x => x.Base > 0)
                    .Select(x => new ReturnableBatch(
                        x.BatchId,
                        batches[x.BatchId].BatchNumber,
                        batches[x.BatchId].ExpiryDate,
                        x.Base / factor))
                    .OrderBy(b => b.ExpiryDate == null)
                    .ThenBy(b => b.ExpiryDate)
                    .ThenBy(b => b.BatchNumber)
                    .ToList()
                : [];

            lines.Add(new ReturnableLine(
                line.SalesOrderLineId,
                line.ProductId,
                line.Product.Name,
                line.UnitOfMeasure.Code,
                line.Quantity,
                line.QuantityReturned,
                line.Quantity - line.QuantityReturned,
                tracked,
                batchRows));
        }

        return Ok(lines);
    }

    [HttpPost("returns")]
    public async Task<ActionResult<SalesReturn>> Create(int orderId, CreateSalesReturnRequest request)
    {
        var access = await _userAccess.GetAsync();

        var preview = await _tenantDb.SalesOrders.AsNoTracking()
            .VisibleTo(access)
            .Include(o => o.Lines)
            .Include(o => o.Warehouse).ThenInclude(w => w.Branch)
            .FirstOrDefaultAsync(o => o.SalesOrderId == orderId);

        if (preview is null)
        {
            return NotFound();
        }

        // A return puts stock into the warehouse, which is new stock activity.
        if (preview.Warehouse.Branch is { IsActive: false } inactive)
        {
            return ValidationProblem($"Branch '{inactive.Name}' is inactive; no new returns can be recorded in it.");
        }

        var wantedLineIds = request.Lines.Select(l => l.SalesOrderLineId).ToHashSet();
        var keys = preview.Lines
            .Where(l => wantedLineIds.Contains(l.SalesOrderLineId))
            .Select(l => (l.ProductId, preview.WarehouseId))
            .ToList();

        await using var stock = await _stockLedger.BeginAsync(keys);

        // Everything that decides the outcome is read again now the locks are held.
        var order = await _tenantDb.SalesOrders
            .Include(o => o.Lines)
            .FirstAsync(o => o.SalesOrderId == orderId);

        if (order.Status != SalesOrderStatus.Confirmed)
        {
            return ValidationProblem($"Only a Confirmed sales order has goods to return; this one is {order.Status}.");
        }

        var soldByBatch = await SoldByBatchAsync(orderId);
        var returnedByBatch = await ReturnedByBatchAsync(orderId);

        var lines = new List<(SalesReturnLine Line, SalesReturnLineRequest Request)>();

        foreach (var want in request.Lines)
        {
            var orderLine = order.Lines.FirstOrDefault(l => l.SalesOrderLineId == want.SalesOrderLineId);
            if (orderLine is null)
            {
                return ValidationProblem($"Line {want.SalesOrderLineId} does not belong to this sales order.");
            }

            var returnable = orderLine.Quantity - orderLine.QuantityReturned;
            if (want.Quantity > returnable)
            {
                return ValidationProblem(
                    $"Line {orderLine.SalesOrderLineId}: cannot return {Quantities.Format(want.Quantity)}, " +
                    $"only {Quantities.Format(returnable)} can still be returned.");
            }

            var product = await _tenantDb.Products.AsNoTracking().FirstAsync(p => p.ProductId == orderLine.ProductId);
            var factor = await UnitFactors.ToBaseAsync(_tenantDb, product, orderLine.UnitOfMeasureId);
            var baseQuantity = want.Quantity * factor;

            // A tracked product goes back into a batch this order actually sold from,
            // and no more than it took from that batch.
            var (batch, batchError) = await _batchResolver.FindForStockOutAsync(product, want.BatchNumber);
            if (batchError is not null)
            {
                return ValidationProblem(batchError);
            }

            if (batch is not null)
            {
                var key = (product.ProductId, batch.ProductBatchId);
                var room = soldByBatch.GetValueOrDefault(key) - returnedByBatch.GetValueOrDefault(key);

                if (baseQuantity > room)
                {
                    return ValidationProblem(
                        $"Batch '{batch.BatchNumber}' of '{product.Name}': this order can take back at most " +
                        $"{Quantities.Format(Math.Max(room, 0))} (in the base unit) from that batch.");
                }

                returnedByBatch[key] = returnedByBatch.GetValueOrDefault(key) + baseQuantity;
            }

            orderLine.QuantityReturned += want.Quantity;

            lines.Add((
                new SalesReturnLine
                {
                    SalesOrderLineId = orderLine.SalesOrderLineId,
                    ProductId = orderLine.ProductId,
                    Quantity = want.Quantity,
                    BaseQuantity = baseQuantity,
                    ProductBatchId = batch?.ProductBatchId,
                    Disposition = want.Disposition
                },
                want));
        }

        var salesReturn = new SalesReturn
        {
            SalesOrderId = orderId,
            ReturnDate = request.ReturnDate ?? DateTime.UtcNow,
            Reason = request.Reason.Trim(),
            Lines = lines.Select(l => l.Line).ToList()
        };

        // Saved first (inside the stock transaction) so the movements can point at its id.
        _tenantDb.SalesReturns.Add(salesReturn);
        await _tenantDb.SaveChangesAsync();

        foreach (var (line, _) in lines.Where(l => l.Line.Disposition == ReturnDisposition.Restock))
        {
            line.StockMovement = stock.Post(
                line.ProductId,
                order.WarehouseId,
                line.BaseQuantity,
                MovementType.SalesReturn,
                salesReturn.SalesReturnId,
                $"Sales return #{salesReturn.SalesReturnId}",
                batchId: line.ProductBatchId);
        }

        await stock.CommitAsync();

        return CreatedAtAction(
            nameof(SalesOrdersController.Get),
            "SalesOrders",
            new { id = orderId },
            salesReturn);
    }

    // What the order sold, per (product, batch), in base units.
    private async Task<Dictionary<(int ProductId, int BatchId), decimal>> SoldByBatchAsync(int orderId)
    {
        var rows = await _tenantDb.StockMovements.AsNoTracking()
            .Where(m => m.MovementType == MovementType.Sale && m.ReferenceId == orderId && m.BatchId != null)
            .GroupBy(m => new { m.ProductId, BatchId = m.BatchId!.Value })
            .Select(g => new { g.Key.ProductId, g.Key.BatchId, Quantity = -g.Sum(m => m.Quantity) })
            .ToListAsync();

        return rows.ToDictionary(r => (r.ProductId, r.BatchId), r => r.Quantity);
    }

    // What earlier returns of this order already put back (restocked or written off), per (product, batch).
    private async Task<Dictionary<(int ProductId, int BatchId), decimal>> ReturnedByBatchAsync(int orderId)
    {
        var rows = await _tenantDb.SalesReturns.AsNoTracking()
            .Where(r => r.SalesOrderId == orderId)
            .SelectMany(r => r.Lines)
            .Where(l => l.ProductBatchId != null)
            .GroupBy(l => new { l.ProductId, BatchId = l.ProductBatchId!.Value })
            .Select(g => new { g.Key.ProductId, g.Key.BatchId, Quantity = g.Sum(l => l.BaseQuantity) })
            .ToListAsync();

        return rows.ToDictionary(r => (r.ProductId, r.BatchId), r => r.Quantity);
    }
}
