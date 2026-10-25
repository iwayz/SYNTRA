using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Syntra.Api.Auth;
using Syntra.Domain.Inventory;
using Syntra.Infrastructure.Inventory;
using Syntra.Infrastructure.TenantData;

namespace Syntra.Api.Controllers.Inventory;

public record CreateStockCountRequest(int WarehouseId, string? Notes);

// Null clears the row's count (it goes back to "not counted").
public record SetCountedQuantityRequest(decimal? CountedQuantity);

public record AddStockCountRowRequest(int ProductId, string? BatchNumber, decimal CountedQuantity);

public record StockCountSummary(
    int StockCountId,
    int WarehouseId,
    string WarehouseName,
    int BranchId,
    string BranchName,
    StockCountStatus Status,
    string? Notes,
    DateTime CreatedAt,
    DateTime? PostedAt,
    int RowCount,
    int CountedCount);

// CurrentQuantity is what the books say now; Variance (counted - current) is what
// posting would apply. IsStale means stock for the row moved after it was counted,
// so it must be counted again before the count can be posted.
public record StockCountRowDto(
    int StockCountLineId,
    int ProductId,
    string ProductSku,
    string ProductName,
    int? BatchId,
    string? BatchNumber,
    DateOnly? ExpiryDate,
    decimal ExpectedQuantity,
    decimal? CountedQuantity,
    DateTime? CountedAt,
    decimal? SystemQuantityAtCount,
    decimal CurrentQuantity,
    bool IsStale,
    decimal? Variance,
    decimal? PostedVariance);

public record StockCountDetail(StockCountSummary Summary, IReadOnlyList<StockCountRowDto> Rows);

public class CreateStockCountRequestValidator : AbstractValidator<CreateStockCountRequest>
{
    public CreateStockCountRequestValidator()
    {
        RuleFor(x => x.WarehouseId).GreaterThan(0);
        RuleFor(x => x.Notes).MaximumLength(500);
    }
}

public class SetCountedQuantityRequestValidator : AbstractValidator<SetCountedQuantityRequest>
{
    public SetCountedQuantityRequestValidator()
    {
        RuleFor(x => x.CountedQuantity).GreaterThanOrEqualTo(0).When(x => x.CountedQuantity is not null);
    }
}

public class AddStockCountRowRequestValidator : AbstractValidator<AddStockCountRowRequest>
{
    public AddStockCountRowRequestValidator()
    {
        RuleFor(x => x.ProductId).GreaterThan(0);
        RuleFor(x => x.CountedQuantity).GreaterThanOrEqualTo(0);
        RuleFor(x => x.BatchNumber).MaximumLength(BatchResolver.MaxBatchNumberLength);
    }
}

[ApiController]
[Authorize]
[Route("inventory/stock-counts")]
public class StockCountsController : ControllerBase
{
    private readonly TenantDbContext _tenantDb;
    private readonly ICurrentUserAccess _userAccess;
    private readonly IBranchScope _branchScope;
    private readonly IStockLedger _stockLedger;
    private readonly IBatchResolver _batchResolver;

    public StockCountsController(
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

    [HttpGet]
    public async Task<ActionResult<IEnumerable<StockCountSummary>>> List(
        [FromQuery] int? branchId,
        [FromQuery] StockCountStatus? status)
    {
        var access = await _userAccess.GetAsync();

        var query = _tenantDb.StockCounts.AsNoTracking()
            .VisibleTo(access, branchId)
            .AsQueryable();

        if (status is not null)
        {
            query = query.Where(c => c.Status == status);
        }

        var summaries = await query
            .OrderByDescending(c => c.StockCountId)
            .Select(c => new StockCountSummary(
                c.StockCountId,
                c.WarehouseId,
                c.Warehouse.Name,
                c.Warehouse.BranchId,
                c.Warehouse.Branch!.Name,
                c.Status,
                c.Notes,
                c.CreatedAt,
                c.PostedAt,
                c.Lines.Count,
                c.Lines.Count(l => l.CountedQuantity != null)))
            .ToListAsync();

        return Ok(summaries);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<StockCountDetail>> Get(int id)
    {
        var access = await _userAccess.GetAsync();
        var detail = await BuildDetailAsync(id, access);
        return detail is null ? NotFound() : Ok(detail);
    }

    // ---- creating ----

    // Snapshots what the warehouse holds right now: one row per product, or per
    // product and batch for batch-tracked products, for everything with stock.
    [HttpPost]
    public async Task<ActionResult<StockCountDetail>> Create(CreateStockCountRequest request)
    {
        var (warehouse, warehouseError) = await _branchScope.ResolveWarehouseForNewActivityAsync(request.WarehouseId);
        if (warehouse is null)
        {
            return ValidationProblem(warehouseError!);
        }

        var openCount = await _tenantDb.StockCounts.AsNoTracking()
            .Where(c => c.WarehouseId == request.WarehouseId && c.Status == StockCountStatus.Open)
            .Select(c => (int?)c.StockCountId)
            .FirstOrDefaultAsync();

        if (openCount is not null)
        {
            return ValidationProblem($"Warehouse '{warehouse.Name}' already has an open count (#{openCount}); post or cancel it first.");
        }

        var stock = await _tenantDb.StockMovements.AsNoTracking()
            .Where(m => m.WarehouseId == request.WarehouseId)
            .GroupBy(m => new { m.ProductId, m.BatchId })
            .Select(g => new { g.Key.ProductId, g.Key.BatchId, Quantity = g.Sum(m => m.Quantity) })
            .Where(x => x.Quantity > 0)
            .ToListAsync();

        var count = new StockCount
        {
            WarehouseId = request.WarehouseId,
            Status = StockCountStatus.Open,
            Notes = request.Notes,
            CreatedAt = DateTime.UtcNow,
            Lines = stock
                .Select(s => new StockCountLine
                {
                    ProductId = s.ProductId,
                    ProductBatchId = s.BatchId,
                    ExpectedQuantity = s.Quantity
                })
                .ToList()
        };

        _tenantDb.StockCounts.Add(count);
        await _tenantDb.SaveChangesAsync();

        var access = await _userAccess.GetAsync();
        var detail = await BuildDetailAsync(count.StockCountId, access);
        return CreatedAtAction(nameof(Get), new { id = count.StockCountId }, detail);
    }

    // ---- counting ----

    // Records (or clears) what was counted for one row. The count is stamped while
    // holding the product's stock lock, so any movement afterwards is provably later.
    [HttpPut("{id:int}/rows/{rowId:int}")]
    public async Task<ActionResult<StockCountDetail>> SetCount(int id, int rowId, SetCountedQuantityRequest request)
    {
        var access = await _userAccess.GetAsync();

        var preview = await _tenantDb.StockCounts.AsNoTracking()
            .VisibleTo(access)
            .Include(c => c.Lines)
            .FirstOrDefaultAsync(c => c.StockCountId == id);

        var previewRow = preview?.Lines.FirstOrDefault(l => l.StockCountLineId == rowId);
        if (preview is null || previewRow is null)
        {
            return NotFound();
        }

        await using var stock = await _stockLedger.BeginAsync([(previewRow.ProductId, preview.WarehouseId)]);

        var count = await _tenantDb.StockCounts.Include(c => c.Lines).FirstAsync(c => c.StockCountId == id);
        var row = count.Lines.First(l => l.StockCountLineId == rowId);

        if (count.Status != StockCountStatus.Open)
        {
            return ValidationProblem($"Only an open count can be changed; this one is {count.Status}.");
        }

        var consistencyError = await CheckTrackingAsync(row);
        if (consistencyError is not null)
        {
            return ValidationProblem(consistencyError);
        }

        await StampAsync(stock, count, row, request.CountedQuantity);
        await stock.CommitAsync();

        return Ok(await BuildDetailAsync(id, access));
    }

    // Adds something found that is not on the sheet (it had no stock when the
    // count was created). A batch-tracked product must name an existing batch;
    // new batches are created by an ordinary adjustment, never by a count.
    [HttpPost("{id:int}/rows")]
    public async Task<ActionResult<StockCountDetail>> AddRow(int id, AddStockCountRowRequest request)
    {
        var access = await _userAccess.GetAsync();

        var preview = await _tenantDb.StockCounts.AsNoTracking()
            .VisibleTo(access)
            .FirstOrDefaultAsync(c => c.StockCountId == id);

        if (preview is null)
        {
            return NotFound();
        }

        if (!await _tenantDb.Products.AnyAsync(p => p.ProductId == request.ProductId))
        {
            return ValidationProblem($"Product {request.ProductId} does not exist.");
        }

        await using var stock = await _stockLedger.BeginAsync([(request.ProductId, preview.WarehouseId)]);

        var count = await _tenantDb.StockCounts.Include(c => c.Lines).FirstAsync(c => c.StockCountId == id);

        if (count.Status != StockCountStatus.Open)
        {
            return ValidationProblem($"Only an open count can be changed; this one is {count.Status}.");
        }

        // Read after the locks are held, so the product's tracking mode can't change under us.
        var product = await _tenantDb.Products.AsNoTracking().FirstAsync(p => p.ProductId == request.ProductId);

        var (batch, batchError) = await _batchResolver.FindForStockOutAsync(product, request.BatchNumber);
        if (batchError is not null)
        {
            return ValidationProblem(batchError);
        }

        if (count.Lines.Any(l => l.ProductId == request.ProductId && l.ProductBatchId == batch?.ProductBatchId))
        {
            return ValidationProblem("This is already on the count sheet - enter its quantity there.");
        }

        var row = new StockCountLine
        {
            ProductId = request.ProductId,
            ProductBatchId = batch?.ProductBatchId,
            ExpectedQuantity = 0
        };

        count.Lines.Add(row);
        await StampAsync(stock, count, row, request.CountedQuantity);
        await stock.CommitAsync();

        return Ok(await BuildDetailAsync(id, access));
    }

    // ---- posting (admin) ----

    // Applies every counted row's difference to stock in one go. Rows that stock
    // has moved on from since they were counted must be counted again first -
    // that is what makes the result exact without freezing the warehouse.
    [HttpPost("{id:int}/post")]
    [Authorize(Policy = AuthPolicies.TenantAdmin)]
    public async Task<ActionResult<StockCountDetail>> Post(int id)
    {
        var access = await _userAccess.GetAsync();

        var preview = await _tenantDb.StockCounts.AsNoTracking()
            .VisibleTo(access)
            .Include(c => c.Lines)
            .FirstOrDefaultAsync(c => c.StockCountId == id);

        if (preview is null)
        {
            return NotFound();
        }

        var countedKeys = preview.Lines
            .Where(l => l.CountedQuantity is not null)
            .Select(l => (l.ProductId, preview.WarehouseId))
            .ToList();

        if (countedKeys.Count == 0)
        {
            return ValidationProblem("Nothing has been counted yet.");
        }

        await using var stock = await _stockLedger.BeginAsync(countedKeys);

        var count = await _tenantDb.StockCounts
            .Include(c => c.Lines).ThenInclude(l => l.Product)
            .Include(c => c.Lines).ThenInclude(l => l.Batch)
            .FirstAsync(c => c.StockCountId == id);

        if (count.Status != StockCountStatus.Open)
        {
            return ValidationProblem($"Only an open count can be posted; this one is {count.Status}.");
        }

        var counted = count.Lines.Where(l => l.CountedQuantity is not null).ToList();

        foreach (var row in counted)
        {
            var consistencyError = await CheckTrackingAsync(row);
            if (consistencyError is not null)
            {
                return ValidationProblem(consistencyError);
            }
        }

        var stats = await LoadStatsAsync(count.WarehouseId, counted.Select(l => l.ProductId).Distinct().ToList());

        var stale = counted.Where(l => IsStale(l, stats)).ToList();
        if (stale.Count > 0)
        {
            var names = string.Join(", ", stale.Select(Describe));
            return ValidationProblem(
                $"{stale.Count} row(s) changed since they were counted and must be counted again before posting: {names}.");
        }

        foreach (var row in counted)
        {
            var current = stats.TryGetValue((row.ProductId, row.ProductBatchId), out var s) ? s.Quantity : 0;
            var variance = row.CountedQuantity!.Value - current;
            row.PostedVariance = variance;

            if (variance != 0)
            {
                row.AdjustmentMovement = stock.Post(
                    row.ProductId,
                    count.WarehouseId,
                    variance,
                    MovementType.StockCount,
                    count.StockCountId,
                    $"Stock count #{count.StockCountId}",
                    batchId: row.ProductBatchId);
            }
        }

        count.Status = StockCountStatus.Posted;
        count.PostedAt = DateTime.UtcNow;
        await stock.CommitAsync();

        return Ok(await BuildDetailAsync(id, access));
    }

    [HttpPost("{id:int}/cancel")]
    public async Task<ActionResult<StockCountDetail>> Cancel(int id)
    {
        var access = await _userAccess.GetAsync();

        var count = await _tenantDb.StockCounts
            .VisibleTo(access)
            .FirstOrDefaultAsync(c => c.StockCountId == id);

        if (count is null)
        {
            return NotFound();
        }

        if (count.Status != StockCountStatus.Open)
        {
            return ValidationProblem($"Only an open count can be cancelled; this one is {count.Status}.");
        }

        count.Status = StockCountStatus.Cancelled;
        await _tenantDb.SaveChangesAsync();

        return Ok(await BuildDetailAsync(id, access));
    }

    // ---- helpers ----

    private record Stats(decimal Quantity, DateTime LastMovement);

    // For each (product, batch) in the warehouse: what the books hold and when stock last moved.
    private async Task<Dictionary<(int ProductId, int? BatchId), Stats>> LoadStatsAsync(int warehouseId, List<int> productIds)
    {
        var rows = await _tenantDb.StockMovements.AsNoTracking()
            .Where(m => m.WarehouseId == warehouseId && productIds.Contains(m.ProductId))
            .GroupBy(m => new { m.ProductId, m.BatchId })
            .Select(g => new { g.Key.ProductId, g.Key.BatchId, Quantity = g.Sum(m => m.Quantity), Last = g.Max(m => m.OccurredAt) })
            .ToListAsync();

        return rows.ToDictionary(r => (r.ProductId, r.BatchId), r => new Stats(r.Quantity, r.Last));
    }

    private static bool IsStale(StockCountLine row, Dictionary<(int ProductId, int? BatchId), Stats> stats) =>
        row.CountedAt is { } countedAt
        && stats.TryGetValue((row.ProductId, row.ProductBatchId), out var s)
        && s.LastMovement > countedAt;

    private static string Describe(StockCountLine row) =>
        row.Batch is null ? row.Product.Name : $"{row.Product.Name} (batch {row.Batch.BatchNumber})";

    // The sheet was built for the product as it was. If batch tracking was switched on
    // since, an unbatched row no longer matches how the stock is held.
    private async Task<string?> CheckTrackingAsync(StockCountLine row)
    {
        var tracked = (await _tenantDb.Products.AsNoTracking()
            .Where(p => p.ProductId == row.ProductId)
            .Select(p => p.BatchTracking)
            .FirstAsync()) != BatchTracking.None;

        return tracked != (row.ProductBatchId is not null)
            ? "This product's batch tracking changed after the count was created; cancel it and start a new count."
            : null;
    }

    // Records the count on a row. Must be called while holding the stock lock.
    private async Task StampAsync(StockTransaction stock, StockCount count, StockCountLine row, decimal? counted)
    {
        if (counted is null)
        {
            row.CountedQuantity = null;
            row.CountedAt = null;
            row.SystemQuantityAtCount = null;
            return;
        }

        row.SystemQuantityAtCount = row.ProductBatchId is null
            ? await stock.OnHandAsync(row.ProductId, count.WarehouseId)
            : await stock.OnHandInBatchAsync(row.ProductId, count.WarehouseId, row.ProductBatchId.Value);
        row.CountedQuantity = counted;
        row.CountedAt = DateTime.UtcNow;
    }

    private async Task<StockCountDetail?> BuildDetailAsync(int id, UserAccess access)
    {
        var count = await _tenantDb.StockCounts.AsNoTracking()
            .VisibleTo(access)
            .Include(c => c.Warehouse).ThenInclude(w => w.Branch)
            .Include(c => c.Lines).ThenInclude(l => l.Product)
            .Include(c => c.Lines).ThenInclude(l => l.Batch)
            .FirstOrDefaultAsync(c => c.StockCountId == id);

        if (count is null)
        {
            return null;
        }

        var stats = await LoadStatsAsync(count.WarehouseId, count.Lines.Select(l => l.ProductId).Distinct().ToList());
        var open = count.Status == StockCountStatus.Open;

        var rows = count.Lines
            .Select(l =>
            {
                var current = stats.TryGetValue((l.ProductId, l.ProductBatchId), out var s) ? s.Quantity : 0;

                return new StockCountRowDto(
                    l.StockCountLineId,
                    l.ProductId,
                    l.Product.Sku,
                    l.Product.Name,
                    l.ProductBatchId,
                    l.Batch?.BatchNumber,
                    l.Batch?.ExpiryDate,
                    l.ExpectedQuantity,
                    l.CountedQuantity,
                    l.CountedAt,
                    l.SystemQuantityAtCount,
                    current,
                    open && IsStale(l, stats),
                    l.CountedQuantity is null ? null : l.CountedQuantity - current,
                    l.PostedVariance);
            })
            .OrderBy(r => r.ProductName)
            .ThenBy(r => r.ExpiryDate == null)
            .ThenBy(r => r.ExpiryDate)
            .ThenBy(r => r.BatchNumber)
            .ToList();

        var summary = new StockCountSummary(
            count.StockCountId,
            count.WarehouseId,
            count.Warehouse.Name,
            count.Warehouse.BranchId,
            count.Warehouse.Branch!.Name,
            count.Status,
            count.Notes,
            count.CreatedAt,
            count.PostedAt,
            count.Lines.Count,
            count.Lines.Count(l => l.CountedQuantity is not null));

        return new StockCountDetail(summary, rows);
    }
}
