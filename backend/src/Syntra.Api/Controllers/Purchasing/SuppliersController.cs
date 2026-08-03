using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Syntra.Domain.Purchasing;
using Syntra.Infrastructure.TenantData;

namespace Syntra.Api.Controllers.Purchasing;

public record CreateSupplierRequest(string Name);

public class CreateSupplierRequestValidator : AbstractValidator<CreateSupplierRequest>
{
    public CreateSupplierRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
    }
}

[ApiController]
[Authorize]
[Route("purchasing/suppliers")]
public class SuppliersController : ControllerBase
{
    private readonly TenantDbContext _tenantDb;

    public SuppliersController(TenantDbContext tenantDb)
    {
        _tenantDb = tenantDb;
    }

    [HttpGet]
    public async Task<IActionResult> List()
    {
        var suppliers = await _tenantDb.Suppliers.AsNoTracking().ToListAsync();
        return Ok(suppliers);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        var supplier = await _tenantDb.Suppliers.AsNoTracking().FirstOrDefaultAsync(s => s.SupplierId == id);
        return supplier is null ? NotFound() : Ok(supplier);
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateSupplierRequest request)
    {
        var supplier = new Supplier { Name = request.Name };

        _tenantDb.Suppliers.Add(supplier);
        await _tenantDb.SaveChangesAsync();

        return CreatedAtAction(nameof(Get), new { id = supplier.SupplierId }, supplier);
    }
}
