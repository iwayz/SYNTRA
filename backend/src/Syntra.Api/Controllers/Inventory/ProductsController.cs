using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Syntra.Domain.Inventory;
using Syntra.Infrastructure.TenantData;

namespace Syntra.Api.Controllers.Inventory;

public record CreateProductRequest(string Sku, string Name, int BaseUnitOfMeasureId);

public class CreateProductRequestValidator : AbstractValidator<CreateProductRequest>
{
    public CreateProductRequestValidator()
    {
        RuleFor(x => x.Sku).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.BaseUnitOfMeasureId).GreaterThan(0);
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

    public ProductsController(TenantDbContext tenantDb)
    {
        _tenantDb = tenantDb;
    }

    [HttpGet]
    public async Task<IActionResult> List()
    {
        var products = await _tenantDb.Products.AsNoTracking()
            .Include(p => p.BaseUnitOfMeasure)
            .ToListAsync();

        return Ok(products);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        var product = await _tenantDb.Products.AsNoTracking()
            .Include(p => p.BaseUnitOfMeasure)
            .Include(p => p.UnitConversions)
            .ThenInclude(c => c.UnitOfMeasure)
            .FirstOrDefaultAsync(p => p.ProductId == id);

        return product is null ? NotFound() : Ok(product);
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateProductRequest request)
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
            BaseUnitOfMeasureId = request.BaseUnitOfMeasureId
        };

        _tenantDb.Products.Add(product);
        await _tenantDb.SaveChangesAsync();

        return CreatedAtAction(nameof(Get), new { id = product.ProductId }, product);
    }

    [HttpPost("{id:int}/unit-conversions")]
    public async Task<IActionResult> AddUnitConversion(int id, CreateProductUnitConversionRequest request)
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
}
