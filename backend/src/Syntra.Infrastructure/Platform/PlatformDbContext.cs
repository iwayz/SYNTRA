using Microsoft.EntityFrameworkCore;
using Syntra.Domain.Platform;

namespace Syntra.Infrastructure.Platform;

public class PlatformDbContext : DbContext
{
    public PlatformDbContext(DbContextOptions<PlatformDbContext> options) : base(options)
    {
    }

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<ModuleDefinition> Modules => Set<ModuleDefinition>();
    public DbSet<TenantModule> TenantModules => Set<TenantModule>();
    public DbSet<TenantSchemaVersion> TenantSchemaVersions => Set<TenantSchemaVersion>();
    public DbSet<TenantOnboardingLogEntry> TenantOnboardingLogs => Set<TenantOnboardingLogEntry>();
    public DbSet<PlatformUser> PlatformUsers => Set<PlatformUser>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Tenant>(e =>
        {
            e.HasIndex(t => t.Slug).IsUnique();
            e.Property(t => t.Status).HasConversion<string>().HasMaxLength(20);
        });

        modelBuilder.Entity<Subscription>(e =>
        {
            e.HasIndex(s => s.TenantId).IsUnique();
            e.Property(s => s.MaintenanceFeeStatus).HasConversion<string>().HasMaxLength(20);
            e.HasOne(s => s.Tenant)
                .WithOne(t => t.Subscription!)
                .HasForeignKey<Subscription>(s => s.TenantId);
        });

        modelBuilder.Entity<ModuleDefinition>(e =>
        {
            e.HasKey(m => m.ModuleId);
            e.HasIndex(m => m.Code).IsUnique();
            e.Property(m => m.Category).HasConversion<string>().HasMaxLength(20);
        });

        modelBuilder.Entity<TenantModule>(e =>
        {
            e.HasKey(tm => new { tm.TenantId, tm.ModuleId });
            e.HasOne(tm => tm.Tenant)
                .WithMany(t => t.TenantModules)
                .HasForeignKey(tm => tm.TenantId);
            e.HasOne(tm => tm.Module)
                .WithMany(m => m.TenantModules)
                .HasForeignKey(tm => tm.ModuleId);
        });

        modelBuilder.Entity<TenantSchemaVersion>(e =>
        {
            e.HasKey(v => v.TenantId);
            e.Property(v => v.LastStatus).HasConversion<string>().HasMaxLength(20);
            e.HasOne(v => v.Tenant).WithOne().HasForeignKey<TenantSchemaVersion>(v => v.TenantId);
        });

        modelBuilder.Entity<TenantOnboardingLogEntry>(e =>
        {
            e.HasKey(l => l.LogId);
            e.Property(l => l.Status).HasConversion<string>().HasMaxLength(20);
            e.HasOne(l => l.Tenant).WithMany().HasForeignKey(l => l.TenantId);
        });

        modelBuilder.Entity<PlatformUser>(e =>
        {
            e.HasIndex(u => u.Email).IsUnique();
        });

        // Seed data: one demo tenant so the tenant-resolution middleware can be
        // exercised locally via abc.localhost without a full provisioning pipeline yet.
        modelBuilder.Entity<Tenant>().HasData(new Tenant
        {
            TenantId = 1,
            Name = "Demo Tenant",
            Slug = "abc",
            DatabaseServer = ".",
            DatabaseName = "Syntra_Abc",
            ConnectionString = "Data Source=.;Initial Catalog=Syntra_Abc;Integrated Security=True;TrustServerCertificate=True",
            Status = TenantStatus.Active,
            CreatedAt = new DateTime(2026, 7, 26, 0, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 7, 26, 0, 0, 0, DateTimeKind.Utc)
        });
    }
}
