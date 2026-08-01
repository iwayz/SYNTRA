using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Syntra.Domain.TenantData;

namespace Syntra.Infrastructure.TenantData;

// Represents a single tenant's own business database. One instance of this
// context is built per tenant connection string - never shared across tenants.
// Inherits IdentityDbContext so tenant end-user auth (login, password hashing,
// roles) is handled by the framework rather than hand-rolled.
public class TenantDbContext : IdentityDbContext<TenantUser, TenantRole, int>
{
    public TenantDbContext(DbContextOptions<TenantDbContext> options) : base(options)
    {
    }

    public DbSet<SchemaProbe> SchemaProbes => Set<SchemaProbe>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<TenantRole>().HasData(new TenantRole
        {
            Id = TenantRoles.AdminRoleId,
            Name = TenantRoles.AdminRoleName,
            NormalizedName = TenantRoles.AdminRoleName.ToUpperInvariant(),
            ConcurrencyStamp = "8f14e45f-ceea-467e-9b1a-4c1f3d3d7e01"
        });
    }
}
