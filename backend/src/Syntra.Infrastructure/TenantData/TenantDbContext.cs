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
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<PurchaseOrderLine> PurchaseOrderLines => Set<PurchaseOrderLine>();
    public DbSet<SalesOrder> SalesOrders => Set<SalesOrder>();
    public DbSet<SalesOrderLine> SalesOrderLines => Set<SalesOrderLine>();

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

        modelBuilder.Entity<StockMovement>(e =>
        {
            e.HasIndex(m => new { m.ProductId, m.WarehouseId });
            e.Property(m => m.MovementType).HasConversion<string>().HasMaxLength(20);
            e.Property(m => m.Quantity).HasPrecision(18, 4);
            e.HasOne(m => m.Product)
                .WithMany()
                .HasForeignKey(m => m.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(m => m.Warehouse)
                .WithMany()
                .HasForeignKey(m => m.WarehouseId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PurchaseOrder>(e =>
        {
            e.Property(o => o.Status).HasConversion<string>().HasMaxLength(20);
            e.HasOne(o => o.Supplier)
                .WithMany()
                .HasForeignKey(o => o.SupplierId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(o => o.Warehouse)
                .WithMany()
                .HasForeignKey(o => o.WarehouseId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PurchaseOrderLine>(e =>
        {
            e.Property(l => l.Quantity).HasPrecision(18, 4);
            e.Property(l => l.UnitPrice).HasPrecision(18, 4);
            e.HasOne(l => l.PurchaseOrder)
                .WithMany(o => o.Lines)
                .HasForeignKey(l => l.PurchaseOrderId);
            e.HasOne(l => l.Product)
                .WithMany()
                .HasForeignKey(l => l.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(l => l.UnitOfMeasure)
                .WithMany()
                .HasForeignKey(l => l.UnitOfMeasureId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SalesOrder>(e =>
        {
            e.Property(o => o.Status).HasConversion<string>().HasMaxLength(20);
            e.HasOne(o => o.Customer)
                .WithMany()
                .HasForeignKey(o => o.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(o => o.Warehouse)
                .WithMany()
                .HasForeignKey(o => o.WarehouseId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SalesOrderLine>(e =>
        {
            e.Property(l => l.Quantity).HasPrecision(18, 4);
            e.Property(l => l.UnitPrice).HasPrecision(18, 4);
            e.HasOne(l => l.SalesOrder)
                .WithMany(o => o.Lines)
                .HasForeignKey(l => l.SalesOrderId);
            e.HasOne(l => l.Product)
                .WithMany()
                .HasForeignKey(l => l.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(l => l.UnitOfMeasure)
                .WithMany()
                .HasForeignKey(l => l.UnitOfMeasureId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
