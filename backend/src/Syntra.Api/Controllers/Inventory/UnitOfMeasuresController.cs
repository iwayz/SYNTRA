using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Syntra.Domain.Inventory;
using Syntra.Infrastructure.TenantData;

namespace Syntra.Api.Controllers.Inventory;

public record CreateUnitOfMeasureRequest(string Code, string Name);

public class CreateUnitOfMeasureRequestValidator : AbstractValidator<CreateUnitOfMeasureRequest>
{
    public CreateUnitOfMeasureRequestValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(20);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
    }
}

[ApiController]
[Authorize]
[Route("inventory/units-of-measure")]
public class UnitOfMeasuresController : ControllerBase
{
    private readonly TenantDbContext _tenantDb;

    public UnitOfMeasuresController(TenantDbContext tenantDb)
    {
        _tenantDb = tenantDb;
    }

    [HttpGet]
    public async Task<IActionResult> List()
    {
        var units = await _tenantDb.UnitOfMeasures.AsNoTracking().ToListAsync();
        return Ok(units);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        var unit = await _tenantDb.UnitOfMeasures.AsNoTracking().FirstOrDefaultAsync(u => u.UnitOfMeasureId == id);
        return unit is null ? NotFound() : Ok(unit);
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateUnitOfMeasureRequest request)
    {
        var unit = new UnitOfMeasure { Code = request.Code, Name = request.Name };

        _tenantDb.UnitOfMeasures.Add(unit);
        await _tenantDb.SaveChangesAsync();

        return CreatedAtAction(nameof(Get), new { id = unit.UnitOfMeasureId }, unit);
    }
}
