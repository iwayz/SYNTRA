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

public record GoodsReceiptLineRequest(
    int PurchaseOrderLineId,
    decimal Quantity,
    string? Notes,
    string? BatchNumber = null,
    DateOnly? ExpiryDate = null);

public record CreateGoodsReceiptRequest(DateTime? ReceiptDate, string? Notes, List<GoodsReceiptLineRequest> Lines);

public class GoodsReceiptLineRequestValidator : AbstractValidator<GoodsReceiptLineRequest>
{
    public GoodsReceiptLineRequestValidator()
    {
        RuleFor(x => x.PurchaseOrderLineId).GreaterThan(0);
        RuleFor(x => x.Quantity).GreaterThan(0);
        RuleFor(x => x.Notes).MaximumLength(500);
        RuleFor(x => x.BatchNumber).MaximumLength(BatchResolver.MaxBatchNumberLength);
    }
}

public class CreateGoodsReceiptRequestValidator : AbstractValidator<CreateGoodsReceiptRequest>
{
    public CreateGoodsReceiptRequestValidator()
    {
        RuleFor(x => x.Notes).MaximumLength(500);
        RuleFor(x => x.Lines).NotEmpty();
        RuleForEach(x => x.Lines).SetValidator(new GoodsReceiptLineRequestValidator());
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
    private readonly IStockLedger _stockLedger;
    private readonly IBatchResolver _batchResolver;

    public PurchaseOrdersController(
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
            .Include(o => o.GoodsReceipts).ThenInclude(r => r.Lines).ThenInclude(l => l.ProductBatch)
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

    // Receive everything still outstanding in one goods receipt. Kept so the
    // simple "the whole delivery arrived" case stays a single call.
    [HttpPost("{id:int}/receive")]
    public async Task<ActionResult<PurchaseOrder>> Receive(int id)
    {
        var (error, _, order) = await PostReceiptAsync(id, request: null);
        return error ?? Ok(order);
    }

    // One physical delivery, possibly covering only some lines or only part of
    // a line. The PO becomes PartiallyReceived until every line is fully in.
    [HttpPost("{id:int}/goods-receipts")]
    public async Task<ActionResult<GoodsReceipt>> CreateGoodsReceipt(int id, CreateGoodsReceiptRequest request)
    {
        var (error, receipt, _) = await PostReceiptAsync(id, request);
        return error ?? CreatedAtAction(nameof(Get), new { id }, receipt);
    }

    // Closes a partially received order for good: the supplier will not deliver
    // the rest. The shortfall stays visible as ordered minus received per line.
    [HttpPost("{id:int}/close")]
    public async Task<ActionResult<PurchaseOrder>> Close(int id)
    {
        var access = await _userAccess.GetAsync();
        var order = await _tenantDb.PurchaseOrders.VisibleTo(access).FirstOrDefaultAsync(o => o.PurchaseOrderId == id);
        if (order is null)
        {
            return NotFound();
        }

        if (order.Status != PurchaseOrderStatus.PartiallyReceived)
        {
            return ValidationProblem($"Only a PartiallyReceived purchase order can be closed; current status is {order.Status}.");
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

        // Once any goods have arrived the order can no longer be cancelled; close it instead.
        if (order.Status is not (PurchaseOrderStatus.Draft or PurchaseOrderStatus.Confirmed))
        {
            return ValidationProblem($"Purchase order cannot be cancelled from status {order.Status}.");
        }

        order.Status = PurchaseOrderStatus.Cancelled;
        await _tenantDb.SaveChangesAsync();

        return Ok(order);
    }

    // Shared by "receive all" (request == null: every outstanding quantity) and
    // explicit goods receipts. Runs under the stock ledger so concurrent receipts
    // can't each pass the "not more than outstanding" check against the same
    // starting quantity.
    private async Task<(ActionResult? Error, GoodsReceipt? Receipt, PurchaseOrder? Order)> PostReceiptAsync(
        int id,
        CreateGoodsReceiptRequest? request)
    {
        var access = await _userAccess.GetAsync();

        // Only to learn which stock to lock and to enforce branch scope; the
        // order is re-read below once the locks are held.
        var preview = await _tenantDb.PurchaseOrders.AsNoTracking()
            .VisibleTo(access)
            .Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.PurchaseOrderId == id);

        if (preview is null)
        {
            return (NotFound(), null, null);
        }

        await using var stock = await _stockLedger.BeginAsync(
            preview.Lines.Select(l => (l.ProductId, preview.WarehouseId)));

        var order = await _tenantDb.PurchaseOrders
            .Include(o => o.Lines)
            .FirstAsync(o => o.PurchaseOrderId == id);

        if (order.Status is not (PurchaseOrderStatus.Confirmed or PurchaseOrderStatus.PartiallyReceived))
        {
            return (ValidationProblem(
                $"Purchase order must be Confirmed or PartiallyReceived to receive goods; current status is {order.Status}."),
                null, null);
        }

        var wanted = request is null
            ? order.Lines
                .Where(l => l.Quantity - l.QuantityReceived > 0)
                .Select(l => new GoodsReceiptLineRequest(l.PurchaseOrderLineId, l.Quantity - l.QuantityReceived, null))
                .ToList()
            : request.Lines;

        if (wanted.Count == 0)
        {
            return (ValidationProblem("Nothing is outstanding on this purchase order."), null, null);
        }

        // A line can appear more than once only when each entry names a different
        // batch (one delivery of one product that spans several lots).
        var distinctEntries = wanted
            .Select(w => (w.PurchaseOrderLineId, Batch: (w.BatchNumber ?? string.Empty).Trim().ToUpperInvariant()))
            .Distinct()
            .Count();

        if (distinctEntries != wanted.Count)
        {
            return (ValidationProblem(
                "Each purchase order line can appear only once per goods receipt (once per batch for batch-tracked products)."),
                null, null);
        }

        var receiptLines = new List<GoodsReceiptLine>();

        foreach (var want in wanted)
        {
            var line = order.Lines.FirstOrDefault(l => l.PurchaseOrderLineId == want.PurchaseOrderLineId);
            if (line is null)
            {
                return (ValidationProblem($"Line {want.PurchaseOrderLineId} does not belong to this purchase order."), null, null);
            }

            var outstanding = line.Quantity - line.QuantityReceived;
            if (want.Quantity > outstanding)
            {
                return (ValidationProblem(
                    $"Line {line.PurchaseOrderLineId}: cannot receive {Quantities.Format(want.Quantity)}, only {Quantities.Format(outstanding)} is outstanding."),
                    null, null);
            }

            var product = await _tenantDb.Products.AsNoTracking()
                .FirstOrDefaultAsync(p => p.ProductId == line.ProductId);

            // "Receive all outstanding" has nowhere to say which batch arrived.
            if (request is null && product!.BatchTracking != BatchTracking.None)
            {
                return (ValidationProblem(
                    $"Product '{product.Name}' is batch-tracked, so its goods must be received with a batch " +
                    "number through a goods receipt rather than 'receive all'."),
                    null, null);
            }

            var (batch, batchError) = await _batchResolver.ResolveForStockInAsync(product!, want.BatchNumber, want.ExpiryDate);
            if (batchError is not null)
            {
                return (ValidationProblem(batchError), null, null);
            }

            var factor = 1m;

            if (line.UnitOfMeasureId != product!.BaseUnitOfMeasureId)
            {
                var conversion = await _tenantDb.ProductUnitConversions.AsNoTracking().FirstOrDefaultAsync(c =>
                    c.ProductId == line.ProductId && c.UnitOfMeasureId == line.UnitOfMeasureId);

                factor = conversion!.ConversionFactor;
            }

            var movement = stock.Post(
                line.ProductId,
                order.WarehouseId,
                want.Quantity * factor,
                MovementType.Purchase,
                order.PurchaseOrderId,
                batch: batch);

            line.QuantityReceived += want.Quantity;

            receiptLines.Add(new GoodsReceiptLine
            {
                PurchaseOrderLineId = line.PurchaseOrderLineId,
                Quantity = want.Quantity,
                Notes = want.Notes,
                ProductBatch = batch,
                StockMovement = movement
            });
        }

        var receipt = new GoodsReceipt
        {
            PurchaseOrderId = order.PurchaseOrderId,
            ReceiptDate = request?.ReceiptDate ?? DateTime.UtcNow,
            Notes = request?.Notes,
            Lines = receiptLines
        };

        _tenantDb.GoodsReceipts.Add(receipt);

        order.Status = order.Lines.All(l => l.QuantityReceived >= l.Quantity)
            ? PurchaseOrderStatus.Received
            : PurchaseOrderStatus.PartiallyReceived;

        await stock.CommitAsync();

        return (null, receipt, order);
    }
}
