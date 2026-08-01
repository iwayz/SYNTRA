using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Syntra.Domain.Platform;
using Syntra.Domain.TenantData;
using Syntra.Infrastructure.Platform;
using Syntra.Infrastructure.TenantData;

var options = ParseArgs(args);
if (options is null)
{
    Console.WriteLine(
        "Usage: Syntra.TenantProvisioner --name \"Company Name\" --slug slug --admin-email admin@example.com --admin-password P@ssword1");
    return 1;
}

if (options.Slug.Contains('.'))
{
    Console.WriteLine("Slug must not contain '.' - it becomes the subdomain and is split on '.' during tenant resolution.");
    return 1;
}

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .Build();

var platformOptions = new DbContextOptionsBuilder<PlatformDbContext>()
    .UseSqlServer(configuration.GetConnectionString("PlatformDb"))
    .Options;

using var platformDb = new PlatformDbContext(platformOptions);

if (await platformDb.Tenants.AnyAsync(t => t.Slug == options.Slug))
{
    Console.WriteLine($"A tenant with slug '{options.Slug}' already exists. Aborting.");
    return 1;
}

var databaseName = $"Syntra_{options.Slug}";
var connectionString = $"Data Source=.;Initial Catalog={databaseName};Integrated Security=True;TrustServerCertificate=True";

var tenant = new Tenant
{
    Name = options.Name,
    Slug = options.Slug,
    DatabaseServer = ".",
    DatabaseName = databaseName,
    ConnectionString = connectionString,
    Status = TenantStatus.Provisioning,
    CreatedAt = DateTime.UtcNow,
    UpdatedAt = DateTime.UtcNow
};

platformDb.Tenants.Add(tenant);
await platformDb.SaveChangesAsync();

Console.WriteLine($"Registered tenant '{tenant.Name}' (slug: {tenant.Slug}, TenantId: {tenant.TenantId}), status Provisioning.");

try
{
    var tenantOptions = new DbContextOptionsBuilder<TenantDbContext>()
        .UseSqlServer(tenant.ConnectionString)
        .Options;

    using var tenantDb = new TenantDbContext(tenantOptions);

    await tenantDb.Database.MigrateAsync();

    await LogStepAsync(platformDb, tenant.TenantId, "DatabaseCreated", OnboardingStepStatus.Success, null);
    await LogStepAsync(platformDb, tenant.TenantId, "MigrationsApplied", OnboardingStepStatus.Success, null);
    Console.WriteLine("  Database created and migrated.");

    // Step 3: seed reference data. No-op - Phase 1 hasn't defined master data yet.
    await LogStepAsync(
        platformDb,
        tenant.TenantId,
        "SeedDataLoaded",
        OnboardingStepStatus.Success,
        "No-op: no reference/master data defined yet (Phase 1 not started).");
    Console.WriteLine("  Seed reference data: skipped (nothing defined yet).");

    // Step 4: create the admin user. Direct insert rather than UserManager,
    // since this console app has no Identity DI stack - PasswordHasher is the
    // same underlying mechanism Identity itself uses, so the result is fully
    // compatible with UserManager/SignInManager used later by Syntra.Api.
    var adminUser = new TenantUser
    {
        UserName = options.AdminEmail,
        NormalizedUserName = options.AdminEmail.ToUpperInvariant(),
        Email = options.AdminEmail,
        NormalizedEmail = options.AdminEmail.ToUpperInvariant(),
        EmailConfirmed = true,
        SecurityStamp = Guid.NewGuid().ToString()
    };

    adminUser.PasswordHash = new PasswordHasher<TenantUser>().HashPassword(adminUser, options.AdminPassword);

    tenantDb.Users.Add(adminUser);
    await tenantDb.SaveChangesAsync();

    tenantDb.UserRoles.Add(new IdentityUserRole<int>
    {
        UserId = adminUser.Id,
        RoleId = TenantRoles.AdminRoleId
    });
    await tenantDb.SaveChangesAsync();

    await LogStepAsync(
        platformDb,
        tenant.TenantId,
        "AdminUserCreated",
        OnboardingStepStatus.Success,
        $"Created admin user '{options.AdminEmail}' with role '{TenantRoles.AdminRoleName}'.");
    Console.WriteLine($"  Admin user '{options.AdminEmail}' created with role '{TenantRoles.AdminRoleName}'.");
}
catch (Exception ex)
{
    await LogStepAsync(platformDb, tenant.TenantId, "MigrationsApplied", OnboardingStepStatus.Failed, ex.Message);
    Console.WriteLine($"FAILED: {ex.Message}");
    Console.WriteLine($"Tenant '{tenant.Name}' (TenantId {tenant.TenantId}) left in status '{tenant.Status}' for manual investigation.");
    return 1;
}

// Step 5: activate.
tenant.Status = TenantStatus.Active;
tenant.UpdatedAt = DateTime.UtcNow;
await platformDb.SaveChangesAsync();
await LogStepAsync(platformDb, tenant.TenantId, "RegisteredInPlatform", OnboardingStepStatus.Success, null);

Console.WriteLine($"Tenant '{tenant.Name}' activated (slug: {tenant.Slug}).");

return 0;

static ProvisionOptions? ParseArgs(string[] args)
{
    string? name = null;
    string? slug = null;
    string? adminEmail = null;
    string? adminPassword = null;

    for (var i = 0; i < args.Length - 1; i++)
    {
        switch (args[i])
        {
            case "--name":
                name = args[i + 1];
                break;
            case "--slug":
                slug = args[i + 1].ToLowerInvariant();
                break;
            case "--admin-email":
                adminEmail = args[i + 1];
                break;
            case "--admin-password":
                adminPassword = args[i + 1];
                break;
        }
    }

    if (string.IsNullOrWhiteSpace(name)
        || string.IsNullOrWhiteSpace(slug)
        || string.IsNullOrWhiteSpace(adminEmail)
        || string.IsNullOrWhiteSpace(adminPassword))
    {
        return null;
    }

    return new ProvisionOptions(name, slug, adminEmail, adminPassword);
}

static async Task LogStepAsync(
    PlatformDbContext platformDb,
    int tenantId,
    string step,
    OnboardingStepStatus status,
    string? details)
{
    platformDb.TenantOnboardingLogs.Add(new TenantOnboardingLogEntry
    {
        TenantId = tenantId,
        Step = step,
        Status = status,
        Timestamp = DateTime.UtcNow,
        Details = details
    });

    await platformDb.SaveChangesAsync();
}

record ProvisionOptions(string Name, string Slug, string AdminEmail, string AdminPassword);
