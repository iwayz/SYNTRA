using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Syntra.Api.Auth;
using Syntra.Domain.Inventory;
using Syntra.Infrastructure.Inventory;
using Syntra.Infrastructure.TenantData;

namespace Syntra.Api.Controllers.Inventory;

public record CreateTransferLineRequest(int ProductId, decimal Quantity, string? BatchNumber = null);

public record CreateTransferRequest(
    int FromWarehouseId,
    int ToWarehouseId,
    string? Notes,
    List<CreateTransferLineRequest> Lines);

public record ReceiveTransferLineRequest(int StockTransferAllocationId, decimal Quantity);

public record ReceiveTransferRequest(List<ReceiveTransferLineRequest> Lines);

// Just enough to choose where a transfer goes - names, never stock levels.
public record TransferDestination(int WarehouseId, string WarehouseName, int BranchId, string BranchName);

public record StockInTransit(
    int StockTransferId,
    int ProductId,
    string ProductSku,
    string ProductName,
    int? BatchId,
    string? BatchNumber,
    DateOnly? ExpiryDate,
    int FromWarehouseId,
    string FromWarehouseName,
    int ToWarehouseId,
    string ToWarehouseName,
    decimal Quantity);

public class CreateTransferLineRequestValidator : AbstractValidator<CreateTransferLineRequest>
{
    public CreateTransferLineRequestValidator()
    {
        RuleFor(x => x.ProductId).GreaterThan(0);
        RuleFor(x => x.Quantity).GreaterThan(0);
        RuleFor(x => x.BatchNumber).MaximumLength(BatchResolver.MaxBatchNumberLength);
    }
}

public class CreateTransferRequestValidator : AbstractValidator<CreateTransferRequest>
{
    public CreateTransferRequestValidator()
    {
        RuleFor(x => x.FromWarehouseId).GreaterThan(0);
        RuleFor(x => x.ToWarehouseId).GreaterThan(0);
        RuleFor(x => x.Notes).MaximumLength(500);
        RuleFor(x => x.Lines).NotEmpty();
        RuleForEach(x => x.Lines).SetValidator(new CreateTransferLineRequestValidator());
    }
}

public class ReceiveTransferLineRequestValidator : AbstractValidator<ReceiveTransferLineRequest>
{
    public ReceiveTransferLineRequestValidator()
    {
        RuleFor(x => x.StockTransferAllocationId).GreaterThan(0);
        RuleFor(x => x.Quantity).GreaterThan(0);
    }
}

public class ReceiveTransferRequestValidator : AbstractValidator<ReceiveTransferRequest>
{
    public ReceiveTransferRequestValidator()
    {
        RuleFor(x => x.Lines).NotEmpty();
        RuleForEach(x => x.Lines).SetValidator(new ReceiveTransferLineRequestValidator());
    }
}

[ApiController]
[Authorize]
[Route("inventory")]
public class StockTransfersController : ControllerBase
{
    private readonly TenantDbContext _tenantDb;
    private readonly ICurrentUserAccess _userAccess;
    private readonly IBranchScope _branchScope;
    private readonly IStockLedger _stockLedger;
    private readonly IBatchResolver _batchResolver;

    public StockTransfersController(
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

    // ---- reading ----

    // A transfer is visible to users of either end's branch. The optional
    // branchId matches either end too.
    [HttpGet("transfers")]
    public async Task<ActionResult<IEnumerable<StockTransfer>>> List(
        [FromQuery] int? branchId,
        [FromQuery] StockTransferStatus? status)
    {
        var access = await _userAccess.GetAsync();

        var query = _tenantDb.StockTransfers.AsNoTracking()
            .VisibleTo(access, branchId)
            .Include(t => t.FromWarehouse).ThenInclude(w => w.Branch)
            .Include(t => t.ToWarehouse).ThenInclude(w => w.Branch)
            .AsQueryable();

        if (status is not null)
        {
            query = query.Where(t => t.Status == status);
        }

        return Ok(await query.OrderByDescending(t => t.StockTransferId).ToListAsync());
    }

    [HttpGet("transfers/{id:int}")]
    public async Task<ActionResult<StockTransfer>> Get(int id)
    {
        var access = await _userAccess.GetAsync();

        var transfer = await _tenantDb.StockTransfers.AsNoTracking()
            .VisibleTo(access)
            .Include(t => t.FromWarehouse).ThenInclude(w => w.Branch)
            .Include(t => t.ToWarehouse).ThenInclude(w => w.Branch)
            .Include(t => t.Lines).ThenInclude(l => l.Product)
            .Include(t => t.Lines).ThenInclude(l => l.PinnedBatch)
            .Include(t => t.Lines).ThenInclude(l => l.Allocations).ThenInclude(a => a.Batch)
            .FirstOrDefaultAsync(t => t.StockTransferId == id);

        return transfer is null ? NotFound() : Ok(transfer);
    }

    // Where a transfer can go: every warehouse of every active branch, by name.
    // Branch scoping hides other branches' warehouses everywhere else, so without
    // this a branch user could not name a destination. It exposes names only.
    [HttpGet("transfer-destinations")]
    public async Task<ActionResult<IEnumerable<TransferDestination>>> Destinations()
    {
        var destinations = await _tenantDb.Warehouses.AsNoTracking()
            .Where(w => w.Branch!.IsActive)
            .OrderBy(w => w.Branch!.Name)
            .ThenBy(w => w.Name)
            .Select(w => new TransferDestination(w.WarehouseId, w.Name, w.BranchId, w.Branch!.Name))
            .ToListAsync();

        return Ok(destinations);
    }

    // What is on its way: dispatched and not yet received. It is in no
    // warehouse's balance while it travels.
    [HttpGet("stock-in-transit")]
    public async Task<ActionResult<IEnumerable<StockInTransit>>> InTransit(
        [FromQuery] int? productId,
        [FromQuery] int? branchId)
    {
        var access = await _userAccess.GetAsync();

        var transfers = await _tenantDb.StockTransfers.AsNoTracking()
            .VisibleTo(access, branchId)
            .Where(t => t.Status == StockTransferStatus.InTransit || t.Status == StockTransferStatus.PartiallyReceived)
            .Include(t => t.FromWarehouse)
            .Include(t => t.ToWarehouse)
            .Include(t => t.Lines).ThenInclude(l => l.Product)
            .Include(t => t.Lines).ThenInclude(l => l.Allocations).ThenInclude(a => a.Batch)
            .ToListAsync();

        var rows = transfers
            .SelectMany(t => t.Lines
                .Where(l => productId is null || l.ProductId == productId)
                .SelectMany(l => l.Allocations
                    .Where(a => a.Quantity - a.QuantityReceived > 0)
                    .Select(a => new StockInTransit(
                        t.StockTransferId,
                        l.ProductId,
                        l.Product.Sku,
                        l.Product.Name,
                        a.ProductBatchId,
                        a.Batch?.BatchNumber,
                        a.Batch?.ExpiryDate,
                        t.FromWarehouseId,
                        t.FromWarehouse.Name,
                        t.ToWarehouseId,
                        t.ToWarehouse.Name,
                        a.Quantity - a.QuantityReceived))))
            .OrderBy(r => r.StockTransferId)
            .ThenBy(r => r.ProductName)
            .ToList();

        return Ok(rows);
    }

    // ---- creating ----

    // A draft moves and reserves nothing; stock is checked when it is dispatched.
    [HttpPost("transfers")]
    public async Task<ActionResult<StockTransfer>> Create(CreateTransferRequest request)
    {
        if (request.FromWarehouseId == request.ToWarehouseId)
        {
            return ValidationProblem("A transfer needs two different warehouses.");
        }

        // The source must be a warehouse the caller can use (visible, active branch).
        var (from, fromError) = await _branchScope.ResolveWarehouseForNewActivityAsync(request.FromWarehouseId);
        if (from is null)
        {
            return ValidationProblem(fromError!);
        }

        // The destination may be in any active branch, including ones the caller
        // cannot otherwise see (that is what the destinations lookup is for).
        var to = await _tenantDb.Warehouses.AsNoTracking()
            .Include(w => w.Branch)
            .FirstOrDefaultAsync(w => w.WarehouseId == request.ToWarehouseId);

        if (to is null)
        {
            return ValidationProblem($"Warehouse {request.ToWarehouseId} does not exist.");
        }

        if (to.Branch is { IsActive: false })
        {
            return ValidationProblem($"Branch '{to.Branch.Name}' is inactive; no new transfers can be sent to it.");
        }

        var lines = new List<StockTransferLine>();

        foreach (var lineRequest in request.Lines)
        {
            var product = await _tenantDb.Products.AsNoTracking()
                .FirstOrDefaultAsync(p => p.ProductId == lineRequest.ProductId);

            if (product is null)
            {
                return ValidationProblem($"Product {lineRequest.ProductId} does not exist.");
            }

            int? pinnedBatchId = null;

            if (!string.IsNullOrWhiteSpace(lineRequest.BatchNumber))
            {
                var (batch, batchError) = await _batchResolver.FindForStockOutAsync(product, lineRequest.BatchNumber);
                if (batchError is not null)
                {
                    return ValidationProblem(batchError);
                }

                pinnedBatchId = batch!.ProductBatchId;
            }

            lines.Add(new StockTransferLine
            {
                ProductId = lineRequest.ProductId,
                Quantity = lineRequest.Quantity,
                PinnedBatchId = pinnedBatchId
            });
        }

        var transfer = new StockTransfer
        {
            FromWarehouseId = request.FromWarehouseId,
            ToWarehouseId = request.ToWarehouseId,
            Status = StockTransferStatus.Draft,
            Notes = request.Notes,
            CreatedAt = DateTime.UtcNow,
            Lines = lines
        };

        _tenantDb.StockTransfers.Add(transfer);
        await _tenantDb.SaveChangesAsync();

        return CreatedAtAction(nameof(Get), new { id = transfer.StockTransferId }, transfer);
    }

    // ---- dispatching (source branch) ----

    [HttpPost("transfers/{id:int}/dispatch")]
    public async Task<ActionResult<StockTransfer>> Dispatch(int id)
    {
        var access = await _userAccess.GetAsync();

        // This first look only finds the stock to lock and checks who is asking;
        // the transfer is re-read below once the locks are held.
        var preview = await _tenantDb.StockTransfers.AsNoTracking()
            .VisibleTo(access)
            .Include(t => t.FromWarehouse)
            .Include(t => t.Lines)
            .FirstOrDefaultAsync(t => t.StockTransferId == id);

        if (preview is null)
        {
            return NotFound();
        }

        if (!access.CanAccessBranch(preview.FromWarehouse.BranchId))
        {
            return Problem(statusCode: StatusCodes.Status403Forbidden, title: "Only users of the source branch can dispatch this transfer.");
        }

        await using var stock = await _stockLedger.BeginAsync(
            preview.Lines.Select(l => (l.ProductId, preview.FromWarehouseId)));

        var transfer = await _tenantDb.StockTransfers
            .Include(t => t.Lines)
            .FirstAsync(t => t.StockTransferId == id);

        if (transfer.Status != StockTransferStatus.Draft)
        {
            return ValidationProblem($"Only a Draft transfer can be dispatched; current status is {transfer.Status}.");
        }

        // Pass 1: work out what leaves, from a working copy of the source's stock
        // that every line shares, so no two lines can claim the same units.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var pools = new Dictionary<int, List<BatchStock>>();
        var claimed = new Dictionary<int, decimal>();
        var plan = new List<(StockTransferLine Line, List<(int? BatchId, decimal Quantity)> Parts)>();
        var errors = new List<string>();

        foreach (var line in transfer.Lines)
        {
            var product = await _tenantDb.Products.AsNoTracking().FirstAsync(p => p.ProductId == line.ProductId);
            var where = $"product {line.ProductId} at warehouse {transfer.FromWarehouseId}";

            if (product.BatchTracking == BatchTracking.None)
            {
                var onHand = await stock.OnHandAsync(line.ProductId, transfer.FromWarehouseId);
                var available = onHand - claimed.GetValueOrDefault(line.ProductId);

                if (line.Quantity > available)
                {
                    errors.Add($"Insufficient stock for {where}: requested {Quantities.Format(line.Quantity)}, available {Quantities.Format(available)}.");
                    continue;
                }

                claimed[line.ProductId] = claimed.GetValueOrDefault(line.ProductId) + line.Quantity;
                plan.Add((line, [(null, line.Quantity)]));
                continue;
            }

            if (!pools.TryGetValue(line.ProductId, out var pool))
            {
                pool = await stock.BatchStockAsync(line.ProductId, transfer.FromWarehouseId);
                pools[line.ProductId] = pool;
            }

            if (line.PinnedBatchId is { } pinnedId)
            {
                // A pinned batch is taken as named, even if expired (that is how
                // expired stock is moved out to a quarantine warehouse).
                var entry = pool.FirstOrDefault(b => b.BatchId == pinnedId);
                if (entry is null || entry.Remaining < line.Quantity)
                {
                    errors.Add(
                        $"Insufficient stock in the chosen batch for {where}: requested {Quantities.Format(line.Quantity)}, " +
                        $"available {Quantities.Format(entry?.Remaining ?? 0)}.");
                    continue;
                }

                entry.Remaining -= line.Quantity;
                plan.Add((line, [(pinnedId, line.Quantity)]));
                continue;
            }

            var allocation = FefoAllocator.TryAllocate(line.Quantity, pool, today);
            if (allocation is null)
            {
                var expired = FefoAllocator.ExpiredQuantity(pool, today);
                errors.Add(
                    $"Insufficient stock for {where}: requested {Quantities.Format(line.Quantity)}, " +
                    $"available {Quantities.Format(FefoAllocator.SellableQuantity(pool, today))}" +
                    (expired > 0 ? $" (a further {Quantities.Format(expired)} is expired; pin that batch to move it)." : "."));
                continue;
            }

            plan.Add((line, allocation.Select(a => ((int?)a.BatchId, a.Quantity)).ToList()));
        }

        if (errors.Count > 0)
        {
            return ValidationProblem(string.Join(" ", errors));
        }

        // Pass 2: everything fits - take it all out and record what left.
        foreach (var (line, parts) in plan)
        {
            foreach (var (batchId, quantity) in parts)
            {
                var movement = stock.Post(
                    line.ProductId,
                    transfer.FromWarehouseId,
                    -quantity,
                    MovementType.TransferOut,
                    transfer.StockTransferId,
                    batchId: batchId);

                line.Allocations.Add(new StockTransferAllocation
                {
                    ProductBatchId = batchId,
                    Quantity = quantity,
                    DispatchMovement = movement
                });
            }
        }

        transfer.Status = StockTransferStatus.InTransit;
        transfer.DispatchedAt = DateTime.UtcNow;
        await stock.CommitAsync();

        return Ok(transfer);
    }

    // ---- receiving (destination branch) ----

    // Records what actually arrived, per batch. It can be done in several steps;
    // the transfer stays PartiallyReceived until everything is in or it is closed.
    [HttpPost("transfers/{id:int}/receive")]
    public async Task<ActionResult<StockTransfer>> Receive(int id, ReceiveTransferRequest request)
    {
        var access = await _userAccess.GetAsync();

        var preview = await _tenantDb.StockTransfers.AsNoTracking()
            .VisibleTo(access)
            .Include(t => t.ToWarehouse)
            .Include(t => t.Lines)
            .FirstOrDefaultAsync(t => t.StockTransferId == id);

        if (preview is null)
        {
            return NotFound();
        }

        if (!access.CanAccessBranch(preview.ToWarehouse.BranchId))
        {
            return Problem(statusCode: StatusCodes.Status403Forbidden, title: "Only users of the destination branch can receive this transfer.");
        }

        await using var stock = await _stockLedger.BeginAsync(
            preview.Lines.Select(l => (l.ProductId, preview.ToWarehouseId)));

        var transfer = await _tenantDb.StockTransfers
            .Include(t => t.Lines).ThenInclude(l => l.Allocations)
            .FirstAsync(t => t.StockTransferId == id);

        if (transfer.Status is not (StockTransferStatus.InTransit or StockTransferStatus.PartiallyReceived))
        {
            return ValidationProblem($"Only a transfer that is in transit can be received; current status is {transfer.Status}.");
        }

        if (request.Lines.Select(l => l.StockTransferAllocationId).Distinct().Count() != request.Lines.Count)
        {
            return ValidationProblem("Each batch of a transfer can appear only once per receipt.");
        }

        var allocations = transfer.Lines
            .SelectMany(l => l.Allocations.Select(a => (Line: l, Allocation: a)))
            .ToDictionary(x => x.Allocation.StockTransferAllocationId);

        foreach (var received in request.Lines)
        {
            if (!allocations.TryGetValue(received.StockTransferAllocationId, out var entry))
            {
                return ValidationProblem($"Item {received.StockTransferAllocationId} does not belong to this transfer.");
            }

            var outstanding = entry.Allocation.Quantity - entry.Allocation.QuantityReceived;
            if (received.Quantity > outstanding)
            {
                return ValidationProblem(
                    $"Item {received.StockTransferAllocationId}: cannot receive {Quantities.Format(received.Quantity)}, " +
                    $"only {Quantities.Format(outstanding)} is outstanding.");
            }

            stock.Post(
                entry.Line.ProductId,
                transfer.ToWarehouseId,
                received.Quantity,
                MovementType.TransferIn,
                transfer.StockTransferId,
                batchId: entry.Allocation.ProductBatchId);

            entry.Allocation.QuantityReceived += received.Quantity;
        }

        var complete = allocations.Values.All(x => x.Allocation.QuantityReceived >= x.Allocation.Quantity);
        transfer.Status = complete ? StockTransferStatus.Received : StockTransferStatus.PartiallyReceived;
        if (complete)
        {
            transfer.CompletedAt = DateTime.UtcNow;
        }

        await stock.CommitAsync();

        return Ok(transfer);
    }

    // The rest is not coming: close the transfer. What was dispatched but never
    // received stays recorded as dispatched minus received, i.e. written off as
    // lost in transit. No movement is needed - that stock already left the source.
    [HttpPost("transfers/{id:int}/close")]
    public async Task<ActionResult<StockTransfer>> Close(int id)
    {
        var access = await _userAccess.GetAsync();

        var transfer = await _tenantDb.StockTransfers
            .VisibleTo(access)
            .Include(t => t.ToWarehouse)
            .FirstOrDefaultAsync(t => t.StockTransferId == id);

        if (transfer is null)
        {
            return NotFound();
        }

        if (!access.CanAccessBranch(transfer.ToWarehouse.BranchId))
        {
            return Problem(statusCode: StatusCodes.Status403Forbidden, title: "Only users of the destination branch can close this transfer.");
        }

        if (transfer.Status != StockTransferStatus.PartiallyReceived)
        {
            return ValidationProblem($"Only a PartiallyReceived transfer can be closed; current status is {transfer.Status}.");
        }

        transfer.Status = StockTransferStatus.Received;
        transfer.CompletedAt = DateTime.UtcNow;
        await _tenantDb.SaveChangesAsync();

        return Ok(transfer);
    }

    // ---- cancelling (source branch) ----

    // A draft is simply dropped. An in-transit transfer can be recalled while
    // nothing has been received: the dispatched stock goes back into the source,
    // in the same batches.
    [HttpPost("transfers/{id:int}/cancel")]
    public async Task<ActionResult<StockTransfer>> Cancel(int id)
    {
        var access = await _userAccess.GetAsync();

        var preview = await _tenantDb.StockTransfers.AsNoTracking()
            .VisibleTo(access)
            .Include(t => t.FromWarehouse)
            .Include(t => t.Lines)
            .FirstOrDefaultAsync(t => t.StockTransferId == id);

        if (preview is null)
        {
            return NotFound();
        }

        if (!access.CanAccessBranch(preview.FromWarehouse.BranchId))
        {
            return Problem(statusCode: StatusCodes.Status403Forbidden, title: "Only users of the source branch can cancel this transfer.");
        }

        if (preview.Status == StockTransferStatus.Draft)
        {
            var draft = await _tenantDb.StockTransfers.FirstAsync(t => t.StockTransferId == id);
            draft.Status = StockTransferStatus.Cancelled;
            await _tenantDb.SaveChangesAsync();
            return Ok(draft);
        }

        if (preview.Status != StockTransferStatus.InTransit)
        {
            return ValidationProblem(
                $"A transfer can only be cancelled from Draft, or from InTransit before anything is received; current status is {preview.Status}.");
        }

        await using var stock = await _stockLedger.BeginAsync(
            preview.Lines.Select(l => (l.ProductId, preview.FromWarehouseId)));

        var transfer = await _tenantDb.StockTransfers
            .Include(t => t.Lines).ThenInclude(l => l.Allocations)
            .FirstAsync(t => t.StockTransferId == id);

        // Re-checked under the lock. A receive that slips in at the destination
        // takes different locks; the status concurrency token makes whichever of
        // the two saves second fail instead of both succeeding.
        if (transfer.Status != StockTransferStatus.InTransit
            || transfer.Lines.SelectMany(l => l.Allocations).Any(a => a.QuantityReceived > 0))
        {
            return ValidationProblem("This transfer can no longer be cancelled because some of it has been received. Close it instead.");
        }

        foreach (var line in transfer.Lines)
        {
            foreach (var allocation in line.Allocations)
            {
                stock.Post(
                    line.ProductId,
                    transfer.FromWarehouseId,
                    allocation.Quantity,
                    MovementType.TransferReversal,
                    transfer.StockTransferId,
                    batchId: allocation.ProductBatchId);
            }
        }

        transfer.Status = StockTransferStatus.Cancelled;
        await stock.CommitAsync();

        return Ok(transfer);
    }
}
