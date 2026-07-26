using Microsoft.EntityFrameworkCore;
using Syntra.Domain.TenantData;

namespace Syntra.Infrastructure.TenantData;

// Represents a single tenant's own business database. One instance of this
// context is built per tenant connection string - never shared across tenants.
public class TenantDbContext : DbContext
{
    public TenantDbContext(DbContextOptions<TenantDbContext> options) : base(options)
    {
    }

    public DbSet<SchemaProbe> SchemaProbes => Set<SchemaProbe>();
}
