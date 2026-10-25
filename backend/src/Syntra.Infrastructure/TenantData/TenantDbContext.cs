using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Syntra.Domain.Inventory;
using Syntra.Domain.Purchasing;
using Syntra.Domain.Sales;
using Syntra.Domain.TenantData;
using Syntra.Domain.Tenancy;

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
    public DbSet<ProductBatch> ProductBatches => Set<ProductBatch>();
    public DbSet<ProductUnitConversion> ProductUnitConversions => Set<ProductUnitConversion>();
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<UserBranch> UserBranches => Set<UserBranch>();
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<StockCount> StockCounts => Set<StockCount>();
    public DbSet<StockCountLine> StockCountLines => Set<StockCountLine>();
    public DbSet<StockTransfer> StockTransfers => Set<StockTransfer>();
    public DbSet<StockTransferLine> StockTransferLines => Set<StockTransferLine>();
    public DbSet<StockTransferAllocation> StockTransferAllocations => Set<StockTransferAllocation>();
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<PurchaseOrderLine> PurchaseOrderLines => Set<PurchaseOrderLine>();
    public DbSet<GoodsReceipt> GoodsReceipts => Set<GoodsReceipt>();
    public DbSet<GoodsReceiptLine> GoodsReceiptLines => Set<GoodsReceiptLine>();
    public DbSet<SalesOrder> SalesOrders => Set<SalesOrder>();
    public DbSet<SalesOrderLine> SalesOrderLines => Set<SalesOrderLine>();
    public DbSet<SalesReturn> SalesReturns => Set<SalesReturn>();
    public DbSet<SalesReturnLine> SalesReturnLines => Set<SalesReturnLine>();
    public DbSet<PurchaseReturn> PurchaseReturns => Set<PurchaseReturn>();
    public DbSet<PurchaseReturnLine> PurchaseReturnLines => Set<PurchaseReturnLine>();

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

        modelBuilder.Entity<Branch>(e =>
        {
            e.HasIndex(b => b.Code).IsUnique();
            e.Property(b => b.Code).HasMaxLength(20);
            e.Property(b => b.Name).HasMaxLength(200);
            e.HasData(new Branch
            {
                BranchId = Branch.MainBranchId,
                Code = Branch.MainBranchCode,
                Name = Branch.MainBranchName,
                IsActive = true
            });
        });

        modelBuilder.Entity<UserBranch>(e =>
        {
            e.HasKey(ub => new { ub.UserId, ub.BranchId });
            e.HasOne<TenantUser>()
                .WithMany()
                .HasForeignKey(ub => ub.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Branch>()
                .WithMany()
                .HasForeignKey(ub => ub.BranchId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Warehouse>(e =>
        {
            e.HasOne(w => w.Branch)
                .WithMany()
                .HasForeignKey(w => w.BranchId)
                .OnDelete(DeleteBehavior.Restrict);
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
            e.Property(p => p.BatchTracking)
                .HasConversion<string>()
                .HasMaxLength(20)
                .HasDefaultValue(BatchTracking.None);
        });

        modelBuilder.Entity<ProductBatch>(e =>
        {
            // The database's default collation is case-insensitive, so this also
            // makes "ab1" and "AB1" the same batch.
            e.HasIndex(b => new { b.ProductId, b.BatchNumber }).IsUnique();
            e.Property(b => b.BatchNumber).HasMaxLength(50);
            e.HasOne<Product>()
                .WithMany()
                .HasForeignKey(b => b.ProductId)
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
            e.HasIndex(m => new { m.ProductId, m.WarehouseId, m.BatchId });
            e.HasOne(m => m.Batch)
                .WithMany()
                .HasForeignKey(m => m.BatchId)
                .OnDelete(DeleteBehavior.Restrict);
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
            e.Property(o => o.Status).HasConversion<string>().HasMaxLength(20).IsConcurrencyToken();
            e.HasOne(o => o.Supplier)
                .WithMany()
                .HasForeignKey(o => o.SupplierId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(o => o.Warehouse)
                .WithMany()
                .HasForeignKey(o => o.WarehouseId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<StockCount>(e =>
        {
            // A concurrency token, like the other documents: posting racing a
            // cancel (or another post) can't both succeed.
            e.Property(c => c.Status).HasConversion<string>().HasMaxLength(20).IsConcurrencyToken();
            e.Property(c => c.Notes).HasMaxLength(500);
            e.HasOne(c => c.Warehouse)
                .WithMany()
                .HasForeignKey(c => c.WarehouseId)
                .OnDelete(DeleteBehavior.Restrict);

            // One open count per warehouse, enforced by the database too.
            e.HasIndex(c => c.WarehouseId)
                .IsUnique()
                .HasFilter("[Status] = 'Open'");
        });

        modelBuilder.Entity<StockCountLine>(e =>
        {
            e.Property(l => l.ExpectedQuantity).HasPrecision(18, 4);
            e.Property(l => l.CountedQuantity).HasPrecision(18, 4);
            e.Property(l => l.SystemQuantityAtCount).HasPrecision(18, 4);
            e.Property(l => l.PostedVariance).HasPrecision(18, 4);

            // A thing appears once per count (a NULL batch counts as one value here).
            // HasFilter(null) switches off EF's default "WHERE ProductBatchId IS NOT NULL"
            // on a unique index over a nullable column, so unbatched rows are covered too.
            e.HasIndex(l => new { l.StockCountId, l.ProductId, l.ProductBatchId }).IsUnique().HasFilter(null);

            e.HasOne<StockCount>()
                .WithMany(c => c.Lines)
                .HasForeignKey(l => l.StockCountId);
            e.HasOne(l => l.Product)
                .WithMany()
                .HasForeignKey(l => l.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(l => l.Batch)
                .WithMany()
                .HasForeignKey(l => l.ProductBatchId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(l => l.AdjustmentMovement)
                .WithMany()
                .HasForeignKey("AdjustmentMovementId")
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<StockTransfer>(e =>
        {
            // A concurrency token, like order statuses: a recall (cancel) racing a
            // receive can't both win - the second save fails and rolls back.
            e.Property(t => t.Status).HasConversion<string>().HasMaxLength(20).IsConcurrencyToken();
            e.Property(t => t.Notes).HasMaxLength(500);
            e.HasOne(t => t.FromWarehouse)
                .WithMany()
                .HasForeignKey(t => t.FromWarehouseId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(t => t.ToWarehouse)
                .WithMany()
                .HasForeignKey(t => t.ToWarehouseId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<StockTransferLine>(e =>
        {
            e.Property(l => l.Quantity).HasPrecision(18, 4);
            e.HasOne<StockTransfer>()
                .WithMany(t => t.Lines)
                .HasForeignKey(l => l.StockTransferId);
            e.HasOne(l => l.Product)
                .WithMany()
                .HasForeignKey(l => l.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(l => l.PinnedBatch)
                .WithMany()
                .HasForeignKey(l => l.PinnedBatchId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<StockTransferAllocation>(e =>
        {
            e.Property(a => a.Quantity).HasPrecision(18, 4);
            e.Property(a => a.QuantityReceived).HasPrecision(18, 4);
            e.HasOne<StockTransferLine>()
                .WithMany(l => l.Allocations)
                .HasForeignKey(a => a.StockTransferLineId);
            e.HasOne(a => a.Batch)
                .WithMany()
                .HasForeignKey(a => a.ProductBatchId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(a => a.DispatchMovement)
                .WithMany()
                .HasForeignKey("DispatchMovementId")
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<GoodsReceipt>(e =>
        {
            e.HasOne<PurchaseOrder>()
                .WithMany(o => o.GoodsReceipts)
                .HasForeignKey(r => r.PurchaseOrderId)
                .OnDelete(DeleteBehavior.Restrict);
            e.Property(r => r.Notes).HasMaxLength(500);
        });

        modelBuilder.Entity<GoodsReceiptLine>(e =>
        {
            e.Property(l => l.Quantity).HasPrecision(18, 4);
            e.Property(l => l.Notes).HasMaxLength(500);
            e.HasOne<GoodsReceipt>()
                .WithMany(r => r.Lines)
                .HasForeignKey(l => l.GoodsReceiptId);
            e.HasOne<PurchaseOrderLine>()
                .WithMany()
                .HasForeignKey(l => l.PurchaseOrderLineId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(l => l.StockMovement)
                .WithMany()
                .HasForeignKey("StockMovementId")
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(l => l.ProductBatch)
                .WithMany()
                .HasForeignKey(l => l.ProductBatchId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PurchaseOrderLine>(e =>
        {
            e.Property(l => l.Quantity).HasPrecision(18, 4);
            e.Property(l => l.QuantityReceived).HasPrecision(18, 4);
            e.Property(l => l.QuantityReturned).HasPrecision(18, 4);
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
            e.Property(o => o.Status).HasConversion<string>().HasMaxLength(20).IsConcurrencyToken();
            e.HasOne(o => o.Customer)
                .WithMany()
                .HasForeignKey(o => o.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(o => o.Warehouse)
                .WithMany()
                .HasForeignKey(o => o.WarehouseId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SalesReturn>(e =>
        {
            e.Property(r => r.Reason).HasMaxLength(200);
            e.HasOne<SalesOrder>()
                .WithMany(o => o.Returns)
                .HasForeignKey(r => r.SalesOrderId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SalesReturnLine>(e =>
        {
            e.Property(l => l.Quantity).HasPrecision(18, 4);
            e.Property(l => l.BaseQuantity).HasPrecision(18, 4);
            e.Property(l => l.Disposition).HasConversion<string>().HasMaxLength(20);
            e.HasOne<SalesReturn>()
                .WithMany(r => r.Lines)
                .HasForeignKey(l => l.SalesReturnId);
            e.HasOne<SalesOrderLine>()
                .WithMany()
                .HasForeignKey(l => l.SalesOrderLineId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Product>()
                .WithMany()
                .HasForeignKey(l => l.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(l => l.ProductBatch)
                .WithMany()
                .HasForeignKey(l => l.ProductBatchId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(l => l.StockMovement)
                .WithMany()
                .HasForeignKey("StockMovementId")
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PurchaseReturn>(e =>
        {
            e.Property(r => r.Reason).HasMaxLength(200);
            e.HasOne<PurchaseOrder>()
                .WithMany(o => o.Returns)
                .HasForeignKey(r => r.PurchaseOrderId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PurchaseReturnLine>(e =>
        {
            e.Property(l => l.Quantity).HasPrecision(18, 4);
            e.Property(l => l.BaseQuantity).HasPrecision(18, 4);
            e.HasOne<PurchaseReturn>()
                .WithMany(r => r.Lines)
                .HasForeignKey(l => l.PurchaseReturnId);
            e.HasOne<PurchaseOrderLine>()
                .WithMany()
                .HasForeignKey(l => l.PurchaseOrderLineId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(l => l.ProductBatch)
                .WithMany()
                .HasForeignKey(l => l.ProductBatchId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(l => l.StockMovement)
                .WithMany()
                .HasForeignKey("StockMovementId")
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SalesOrderLine>(e =>
        {
            e.Property(l => l.Quantity).HasPrecision(18, 4);
            e.Property(l => l.QuantityReturned).HasPrecision(18, 4);
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
