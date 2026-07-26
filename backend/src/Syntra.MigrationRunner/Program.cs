using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Syntra.Domain.Platform;
using Syntra.Infrastructure.Platform;
using Syntra.Infrastructure.TenantData;

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .Build();

var platformOptions = new DbContextOptionsBuilder<PlatformDbContext>()
    .UseSqlServer(configuration.GetConnectionString("PlatformDb"))
    .Options;

using var platformDb = new PlatformDbContext(platformOptions);

var tenants = await platformDb.Tenants
    .Where(t => t.Status != TenantStatus.Cancelled)
    .ToListAsync();

Console.WriteLine($"Found {tenants.Count} tenant(s) to check.");

var succeeded = 0;
var failed = 0;
var upToDate = 0;
var failedSlugs = new List<string>();

foreach (var tenant in tenants)
{
    Console.WriteLine($"--- {tenant.Slug} ({tenant.Name}) ---");

    var tenantOptions = new DbContextOptionsBuilder<TenantDbContext>()
        .UseSqlServer(tenant.ConnectionString)
        .Options;

    try
    {
        using var tenantDb = new TenantDbContext(tenantOptions);

        var pending = (await tenantDb.Database.GetPendingMigrationsAsync()).ToList();

        if (pending.Count == 0)
        {
            Console.WriteLine("  Already up to date.");
            upToDate++;
            continue;
        }

        await tenantDb.Database.MigrateAsync();

        var applied = (await tenantDb.Database.GetAppliedMigrationsAsync()).LastOrDefault() ?? "unknown";

        await UpsertSchemaVersionAsync(platformDb, tenant.TenantId, applied, MigrationRunStatus.Success, null);

        Console.WriteLine($"  Migrated successfully to '{applied}'.");
        succeeded++;
    }
    catch (Exception ex)
    {
        await UpsertSchemaVersionAsync(platformDb, tenant.TenantId, "unknown", MigrationRunStatus.Failed, ex.Message);
        Console.WriteLine($"  FAILED: {ex.Message}");
        failed++;
        failedSlugs.Add(tenant.Slug);
    }
}

Console.WriteLine();
Console.WriteLine($"Summary: {succeeded} succeeded, {failed} failed, {upToDate} already up to date.");
if (failedSlugs.Count > 0)
{
    Console.WriteLine($"Failed tenants: {string.Join(", ", failedSlugs)}");
}

static async Task UpsertSchemaVersionAsync(
    PlatformDbContext platformDb,
    int tenantId,
    string currentMigration,
    MigrationRunStatus status,
    string? error)
{
    var existing = await platformDb.TenantSchemaVersions.FindAsync(tenantId);
    var now = DateTime.UtcNow;

    if (existing is null)
    {
        platformDb.TenantSchemaVersions.Add(new TenantSchemaVersion
        {
            TenantId = tenantId,
            CurrentMigration = currentMigration,
            LastAttemptAt = now,
            LastStatus = status,
            LastError = error
        });
    }
    else
    {
        if (status == MigrationRunStatus.Success)
        {
            existing.CurrentMigration = currentMigration;
        }

        existing.LastAttemptAt = now;
        existing.LastStatus = status;
        existing.LastError = error;
    }

    await platformDb.SaveChangesAsync();
}
