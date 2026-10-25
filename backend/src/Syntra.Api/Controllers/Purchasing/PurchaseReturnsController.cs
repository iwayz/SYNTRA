using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Syntra.Api.Auth;
using Syntra.Domain.Inventory;
using Syntra.Domain.Purchasing;
using Syntra.Infrastructure.Inventory;
using Syntra.Infrastructure.TenantData;

namespace Syntra.Api.Controllers.Purchasing;

public record PurchaseReturnLineRequest(int PurchaseOrderLineId, decimal Quantity, string? BatchNumber);

public record CreatePurchaseReturnRequest(string Reason, DateTime? ReturnDate, List<PurchaseReturnLineRequest> Lines);

public class PurchaseReturnLineRequestValidator : AbstractValidator<PurchaseReturnLineRequest>
{
    public PurchaseReturnLineRequestValidator()
    {
        RuleFor(x => x.PurchaseOrderLineId).GreaterThan(0);
        RuleFor(x => x.Quantity).GreaterThan(0);
        RuleFor(x => x.BatchNumber).MaximumLength(BatchResolver.MaxBatchNumberLength);
    }
}

public class CreatePurchaseReturnRequestValidator : AbstractValidator<CreatePurchaseReturnRequest>
{
    public CreatePurchaseReturnRequestValidator()
    {
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Lines).NotEmpty();
        RuleForEach(x => x.Lines).SetValidator(new PurchaseReturnLineRequestValidator());
    }
}

// Goods going back to the supplier. They leave the order's own warehouse, and only
// what is still there can go: stock that has since been sold cannot be returned.
[ApiController]
[Authorize]
[Route("purchasing/purchase-orders/{orderId:int}")]
public class PurchaseReturnsController : ControllerBase
{
    private readonly TenantDbContext _tenantDb;
    private readonly ICurrentUserAccess _userAccess;
    private readonly IStockLedger _stockLedger;
    private readonly IBatchResolver _batchResolver;

    public PurchaseReturnsController(
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

    private static bool CanReturn(PurchaseOrderStatus status) =>
        status is PurchaseOrderStatus.Received or PurchaseOrderStatus.PartiallyReceived;

    [HttpGet("returnable")]
    public async Task<ActionResult<IEnumerable<ReturnableLine>>> Returnable(int orderId)
    {
        var access = await _userAccess.GetAsync();

        var order = await _tenantDb.PurchaseOrders.AsNoTracking()
            .VisibleTo(access)
            .Include(o => o.Lines).ThenInclude(l => l.Product)
            .Include(o => o.Lines).ThenInclude(l => l.UnitOfMeasure)
            .FirstOrDefaultAsync(o => o.PurchaseOrderId == orderId);

        if (order is null)
        {
            return NotFound();
        }

        if (!CanReturn(order.Status))
        {
            return ValidationProblem($"Only a purchase order that has received goods can have returns; this one is {order.Status}.");
        }

        var received = await ReceivedByBatchAsync(orderId);
        var returned = await ReturnedByBatchAsync(orderId);

        var batchIds = received.Keys.Select(k => k.BatchId).Distinct().ToList();
        var batches = await _tenantDb.ProductBatches.AsNoTracking()
            .Where(b => batchIds.Contains(b.ProductBatchId))
            .ToDictionaryAsync(b => b.ProductBatchId);

        var lines = order.Lines.Select(line =>
        {
            var tracked = line.Product.BatchTracking != BatchTracking.None;

            var batchRows = tracked
                ? received.Where(r => r.Key.LineId == line.PurchaseOrderLineId)
                    .Select(r => new
                    {
                        r.Key.BatchId,
                        Room = r.Value - returned.GetValueOrDefault(r.Key)
                    })
                    .Where(x => x.Room > 0)
                    .Select(x => new ReturnableBatch(
                        x.BatchId,
                        batches[x.BatchId].BatchNumber,
                        batches[x.BatchId].ExpiryDate,
                        x.Room))
                    .OrderBy(b => b.ExpiryDate == null)
                    .ThenBy(b => b.ExpiryDate)
                    .ThenBy(b => b.BatchNumber)
                    .ToList()
                : [];

            return new ReturnableLine(
                line.PurchaseOrderLineId,
                line.ProductId,
                line.Product.Name,
                line.UnitOfMeasure.Code,
                line.QuantityReceived,
                line.QuantityReturned,
                line.QuantityReceived - line.QuantityReturned,
                tracked,
                batchRows);
        }).ToList();

        return Ok(lines);
    }

    [HttpPost("returns")]
    public async Task<ActionResult<PurchaseReturn>> Create(int orderId, CreatePurchaseReturnRequest request)
    {
        var access = await _userAccess.GetAsync();

        var preview = await _tenantDb.PurchaseOrders.AsNoTracking()
            .VisibleTo(access)
            .Include(o => o.Lines)
            .Include(o => o.Warehouse).ThenInclude(w => w.Branch)
            .FirstOrDefaultAsync(o => o.PurchaseOrderId == orderId);

        if (preview is null)
        {
            return NotFound();
        }

        // A return moves stock out of the warehouse, which is new stock activity.
        if (preview.Warehouse.Branch is { IsActive: false } inactive)
        {
            return ValidationProblem($"Branch '{inactive.Name}' is inactive; no new returns can be recorded in it.");
        }

        var wantedLineIds = request.Lines.Select(l => l.PurchaseOrderLineId).ToHashSet();
        var keys = preview.Lines
            .Where(l => wantedLineIds.Contains(l.PurchaseOrderLineId))
            .Select(l => (l.ProductId, preview.WarehouseId))
            .ToList();

        await using var stock = await _stockLedger.BeginAsync(keys);

        // Everything that decides the outcome is read again now the locks are held.
        var order = await _tenantDb.PurchaseOrders
            .Include(o => o.Lines)
            .FirstAsync(o => o.PurchaseOrderId == orderId);

        if (!CanReturn(order.Status))
        {
            return ValidationProblem($"Only a purchase order that has received goods can have returns; this one is {order.Status}.");
        }

        var receivedByBatch = await ReceivedByBatchAsync(orderId);
        var returnedByBatch = await ReturnedByBatchAsync(orderId);
        var hasReceiptRecords = await _tenantDb.GoodsReceipts.AnyAsync(r => r.PurchaseOrderId == orderId);

        // Stock still to be taken out in this request, so several lines of one product
        // (or batch) cannot each claim the same units.
        var claimed = new Dictionary<(int ProductId, int? BatchId), decimal>();
        var lines = new List<PurchaseReturnLine>();

        foreach (var want in request.Lines)
        {
            var orderLine = order.Lines.FirstOrDefault(l => l.PurchaseOrderLineId == want.PurchaseOrderLineId);
            if (orderLine is null)
            {
                return ValidationProblem($"Line {want.PurchaseOrderLineId} does not belong to this purchase order.");
            }

            var returnable = orderLine.QuantityReceived - orderLine.QuantityReturned;
            if (want.Quantity > returnable)
            {
                return ValidationProblem(
                    $"Line {orderLine.PurchaseOrderLineId}: cannot return {Quantities.Format(want.Quantity)}, " +
                    $"only {Quantities.Format(returnable)} can still be returned.");
            }

            var product = await _tenantDb.Products.AsNoTracking().FirstAsync(p => p.ProductId == orderLine.ProductId);
            var factor = await UnitFactors.ToBaseAsync(_tenantDb, product, orderLine.UnitOfMeasureId);
            var baseQuantity = want.Quantity * factor;

            var (batch, batchError) = await _batchResolver.FindForStockOutAsync(product, want.BatchNumber);
            if (batchError is not null)
            {
                return ValidationProblem(batchError);
            }

            // A tracked product can only go back from a batch this order's receipts
            // brought in. (Orders received before goods receipts existed have no such
            // record, so only the stock check below applies to them.)
            if (batch is not null && hasReceiptRecords)
            {
                var key = (orderLine.PurchaseOrderLineId, batch.ProductBatchId);
                var room = receivedByBatch.GetValueOrDefault(key) - returnedByBatch.GetValueOrDefault(key);

                if (want.Quantity > room)
                {
                    return ValidationProblem(
                        $"Batch '{batch.BatchNumber}' of '{product.Name}': this order received only " +
                        $"{Quantities.Format(Math.Max(room, 0))} more of it that can still be returned.");
                }

                returnedByBatch[key] = returnedByBatch.GetValueOrDefault(key) + want.Quantity;
            }

            // The goods must still be on the shelf: whatever was sold cannot come back.
            var stockKey = (product.ProductId, batch?.ProductBatchId);
            var onHand = batch is null
                ? await stock.OnHandAsync(product.ProductId, order.WarehouseId)
                : await stock.OnHandInBatchAsync(product.ProductId, order.WarehouseId, batch.ProductBatchId);
            var available = onHand - claimed.GetValueOrDefault(stockKey);

            if (baseQuantity > available)
            {
                var where = batch is null ? "" : $" in batch '{batch.BatchNumber}'";
                return ValidationProblem(
                    $"Cannot return {Quantities.Format(baseQuantity)} of '{product.Name}': only " +
                    $"{Quantities.Format(Math.Max(available, 0))} is still in stock{where}; the rest has been used or sold.");
            }

            claimed[stockKey] = claimed.GetValueOrDefault(stockKey) + baseQuantity;
            orderLine.QuantityReturned += want.Quantity;

            lines.Add(new PurchaseReturnLine
            {
                PurchaseOrderLineId = orderLine.PurchaseOrderLineId,
                Quantity = want.Quantity,
                BaseQuantity = baseQuantity,
                ProductBatchId = batch?.ProductBatchId
            });
        }

        var purchaseReturn = new PurchaseReturn
        {
            PurchaseOrderId = orderId,
            ReturnDate = request.ReturnDate ?? DateTime.UtcNow,
            Reason = request.Reason.Trim()
        };

        // Saved first (inside the stock transaction) so the movements can point at its id.
        _tenantDb.PurchaseReturns.Add(purchaseReturn);
        await _tenantDb.SaveChangesAsync();

        foreach (var line in lines)
        {
            var orderLine = order.Lines.First(l => l.PurchaseOrderLineId == line.PurchaseOrderLineId);

            line.StockMovement = stock.Post(
                orderLine.ProductId,
                order.WarehouseId,
                -line.BaseQuantity,
                MovementType.PurchaseReturn,
                purchaseReturn.PurchaseReturnId,
                $"Purchase return #{purchaseReturn.PurchaseReturnId}",
                batchId: line.ProductBatchId);

            purchaseReturn.Lines.Add(line);
        }

        await stock.CommitAsync();

        return CreatedAtAction(
            nameof(PurchaseOrdersController.Get),
            "PurchaseOrders",
            new { id = orderId },
            purchaseReturn);
    }

    // What the order's goods receipts brought in, per (PO line, batch), in the line's unit.
    private async Task<Dictionary<(int LineId, int BatchId), decimal>> ReceivedByBatchAsync(int orderId)
    {
        var rows = await _tenantDb.GoodsReceipts.AsNoTracking()
            .Where(r => r.PurchaseOrderId == orderId)
            .SelectMany(r => r.Lines)
            .Where(l => l.ProductBatchId != null)
            .GroupBy(l => new { l.PurchaseOrderLineId, BatchId = l.ProductBatchId!.Value })
            .Select(g => new { g.Key.PurchaseOrderLineId, g.Key.BatchId, Quantity = g.Sum(l => l.Quantity) })
            .ToListAsync();

        return rows.ToDictionary(r => (r.PurchaseOrderLineId, r.BatchId), r => r.Quantity);
    }

    private async Task<Dictionary<(int LineId, int BatchId), decimal>> ReturnedByBatchAsync(int orderId)
    {
        var rows = await _tenantDb.PurchaseReturns.AsNoTracking()
            .Where(r => r.PurchaseOrderId == orderId)
            .SelectMany(r => r.Lines)
            .Where(l => l.ProductBatchId != null)
            .GroupBy(l => new { l.PurchaseOrderLineId, BatchId = l.ProductBatchId!.Value })
            .Select(g => new { g.Key.PurchaseOrderLineId, g.Key.BatchId, Quantity = g.Sum(l => l.Quantity) })
            .ToListAsync();

        return rows.ToDictionary(r => (r.PurchaseOrderLineId, r.BatchId), r => r.Quantity);
    }
}
