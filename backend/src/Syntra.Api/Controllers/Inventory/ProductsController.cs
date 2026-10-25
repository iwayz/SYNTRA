using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Syntra.Api.Auth;
using Syntra.Domain.Inventory;
using Syntra.Infrastructure.Inventory;
using Syntra.Infrastructure.TenantData;

namespace Syntra.Api.Controllers.Inventory;

public record CreateProductRequest(
    string Sku,
    string Name,
    int BaseUnitOfMeasureId,
    BatchTracking BatchTracking = BatchTracking.None);

public record SetBatchTrackingRequest(BatchTracking Mode);

public class SetBatchTrackingRequestValidator : AbstractValidator<SetBatchTrackingRequest>
{
    public SetBatchTrackingRequestValidator()
    {
        RuleFor(x => x.Mode).IsInEnum();
    }
}

public class CreateProductRequestValidator : AbstractValidator<CreateProductRequest>
{
    public CreateProductRequestValidator()
    {
        RuleFor(x => x.Sku).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.BaseUnitOfMeasureId).GreaterThan(0);
        RuleFor(x => x.BatchTracking).IsInEnum();
    }
}

public record CreateProductUnitConversionRequest(int UnitOfMeasureId, decimal ConversionFactor);

public class CreateProductUnitConversionRequestValidator : AbstractValidator<CreateProductUnitConversionRequest>
{
    public CreateProductUnitConversionRequestValidator()
    {
        RuleFor(x => x.UnitOfMeasureId).GreaterThan(0);
        RuleFor(x => x.ConversionFactor).GreaterThan(0);
    }
}

[ApiController]
[Authorize]
[Route("inventory/products")]
public class ProductsController : ControllerBase
{
    private readonly TenantDbContext _tenantDb;
    private readonly IStockLedger _stockLedger;

    public ProductsController(TenantDbContext tenantDb, IStockLedger stockLedger)
    {
        _tenantDb = tenantDb;
        _stockLedger = stockLedger;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<Product>>> List()
    {
        var products = await _tenantDb.Products.AsNoTracking()
            .Include(p => p.BaseUnitOfMeasure)
            .ToListAsync();

        return Ok(products);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<Product>> Get(int id)
    {
        var product = await _tenantDb.Products.AsNoTracking()
            .Include(p => p.BaseUnitOfMeasure)
            .Include(p => p.UnitConversions)
            .ThenInclude(c => c.UnitOfMeasure)
            .FirstOrDefaultAsync(p => p.ProductId == id);

        return product is null ? NotFound() : Ok(product);
    }

    [HttpPost]
    public async Task<ActionResult<Product>> Create(CreateProductRequest request)
    {
        var baseUnitExists = await _tenantDb.UnitOfMeasures.AnyAsync(u => u.UnitOfMeasureId == request.BaseUnitOfMeasureId);
        if (!baseUnitExists)
        {
            return ValidationProblem($"Unit of measure {request.BaseUnitOfMeasureId} does not exist.");
        }

        var product = new Product
        {
            Sku = request.Sku,
            Name = request.Name,
            BaseUnitOfMeasureId = request.BaseUnitOfMeasureId,
            BatchTracking = request.BatchTracking
        };

        _tenantDb.Products.Add(product);
        await _tenantDb.SaveChangesAsync();

        return CreatedAtAction(nameof(Get), new { id = product.ProductId }, product);
    }

    [HttpPost("{id:int}/unit-conversions")]
    public async Task<ActionResult<ProductUnitConversion>> AddUnitConversion(int id, CreateProductUnitConversionRequest request)
    {
        var product = await _tenantDb.Products.FirstOrDefaultAsync(p => p.ProductId == id);
        if (product is null)
        {
            return NotFound();
        }

        if (request.UnitOfMeasureId == product.BaseUnitOfMeasureId)
        {
            return ValidationProblem("Cannot add a conversion for the product's own base unit.");
        }

        var unitExists = await _tenantDb.UnitOfMeasures.AnyAsync(u => u.UnitOfMeasureId == request.UnitOfMeasureId);
        if (!unitExists)
        {
            return ValidationProblem($"Unit of measure {request.UnitOfMeasureId} does not exist.");
        }

        var conversion = new ProductUnitConversion
        {
            ProductId = id,
            UnitOfMeasureId = request.UnitOfMeasureId,
            ConversionFactor = request.ConversionFactor
        };

        _tenantDb.ProductUnitConversions.Add(conversion);
        await _tenantDb.SaveChangesAsync();

        return CreatedAtAction(nameof(Get), new { id }, conversion);
    }

    // Switching tracking on is one-way and rewrites history, so it is admin-only
    // and runs under the stock locks of every warehouse for this product.
    // Pre-existing stock moves into one OPENING batch (balances don't change),
    // which keeps the rule "every movement of a tracked product has a batch".
    [HttpPut("{id:int}/batch-tracking")]
    [Authorize(Policy = AuthPolicies.TenantAdmin)]
    public async Task<ActionResult<Product>> SetBatchTracking(int id, SetBatchTrackingRequest request)
    {
        if (!await _tenantDb.Products.AnyAsync(p => p.ProductId == id))
        {
            return NotFound();
        }

        // Every warehouse, not only those holding stock: a receipt into a
        // warehouse with no history must not slip an unbatched movement in
        // between the backfill and the mode change.
        var warehouseIds = await _tenantDb.Warehouses.Select(w => w.WarehouseId).ToListAsync();

        await using var stock = await _stockLedger.BeginAsync(warehouseIds.Select(w => (id, w)));

        var product = await _tenantDb.Products.FirstAsync(p => p.ProductId == id);

        if (product.BatchTracking == request.Mode)
        {
            return Ok(product);
        }

        if (request.Mode == BatchTracking.None)
        {
            return ValidationProblem(
                "Batch tracking cannot be turned off once it is on: the product's stock history is recorded by batch.");
        }

        if (product.BatchTracking == BatchTracking.None && await _tenantDb.StockMovements.AnyAsync(m => m.ProductId == id))
        {
            var opening = new ProductBatch
            {
                ProductId = id,
                BatchNumber = ProductBatch.OpeningBatchNumber,
                CreatedAt = DateTime.UtcNow
            };

            _tenantDb.ProductBatches.Add(opening);
            await _tenantDb.SaveChangesAsync();

            await _tenantDb.StockMovements
                .Where(m => m.ProductId == id && m.BatchId == null)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.BatchId, opening.ProductBatchId));
        }

        product.BatchTracking = request.Mode;
        await stock.CommitAsync();

        return Ok(product);
    }
}
