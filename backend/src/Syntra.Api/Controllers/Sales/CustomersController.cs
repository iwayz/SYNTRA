using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Syntra.Domain.Sales;
using Syntra.Infrastructure.TenantData;

namespace Syntra.Api.Controllers.Sales;

public record CreateCustomerRequest(string Name);

public class CreateCustomerRequestValidator : AbstractValidator<CreateCustomerRequest>
{
    public CreateCustomerRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
    }
}

[ApiController]
[Authorize]
[Route("sales/customers")]
public class CustomersController : ControllerBase
{
    private readonly TenantDbContext _tenantDb;

    public CustomersController(TenantDbContext tenantDb)
    {
        _tenantDb = tenantDb;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<Customer>>> List()
    {
        var customers = await _tenantDb.Customers.AsNoTracking().ToListAsync();
        return Ok(customers);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<Customer>> Get(int id)
    {
        var customer = await _tenantDb.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.CustomerId == id);
        return customer is null ? NotFound() : Ok(customer);
    }

    [HttpPost]
    public async Task<ActionResult<Customer>> Create(CreateCustomerRequest request)
    {
        var customer = new Customer { Name = request.Name };

        _tenantDb.Customers.Add(customer);
        await _tenantDb.SaveChangesAsync();

        return CreatedAtAction(nameof(Get), new { id = customer.CustomerId }, customer);
    }
}
