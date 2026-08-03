using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Syntra.Domain.Inventory;
using Syntra.Domain.Purchasing;
using Syntra.Domain.Sales;
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

    public DbSet<UnitOfMeasure> UnitOfMeasures => Set<UnitOfMeasure>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductUnitConversion> ProductUnitConversions => Set<ProductUnitConversion>();
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();

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

        modelBuilder.Entity<UnitOfMeasure>(e =>
        {
            e.HasIndex(u => u.Code).IsUnique();
        });

        modelBuilder.Entity<Product>(e =>
        {
            e.HasIndex(p => p.Sku).IsUnique();
            e.HasOne(p => p.BaseUnitOfMeasure)
                .WithMany()
                .HasForeignKey(p => p.BaseUnitOfMeasureId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ProductUnitConversion>(e =>
        {
            e.HasIndex(c => new { c.ProductId, c.UnitOfMeasureId }).IsUnique();
            e.HasOne(c => c.Product)
                .WithMany(p => p.UnitConversions)
                .HasForeignKey(c => c.ProductId);
            e.HasOne(c => c.UnitOfMeasure)
                .WithMany()
                .HasForeignKey(c => c.UnitOfMeasureId)
                .OnDelete(DeleteBehavior.Restrict);
            e.Property(c => c.ConversionFactor).HasPrecision(18, 4);
        });
    }
}
