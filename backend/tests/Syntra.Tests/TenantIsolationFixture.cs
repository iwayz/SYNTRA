using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Syntra.Domain.Platform;
using Syntra.Domain.TenantData;
using Syntra.Infrastructure.Platform;
using Syntra.Infrastructure.TenantData;

namespace Syntra.Tests;

// Provisions dedicated, ephemeral test tenants (real databases, real Identity
// migration, real admin users) rather than reusing abc/xyz, and tears
// everything down afterward - same pattern as the manual baru/kedua cleanup
// used throughout this project, just automated.
public class TenantIsolationFixture : IAsyncLifetime
{
    private const string PlatformConnectionString =
        "Data Source=.;Initial Catalog=SyntraPlatform;Integrated Security=True;TrustServerCertificate=True";

    public const string AdminEmail = "admin@isolation-test.local";
    public const string AdminPassword = "P@ssw0rd123!";

    private readonly string _runId = Guid.NewGuid().ToString("N")[..8];
    private readonly List<int> _createdTenantIds = [];
    private readonly List<string> _createdDatabaseNames = [];

    public WebApplicationFactory<Program> Factory { get; private set; } = default!;
    public HttpClient Client { get; private set; } = default!;

    public string TenantASlug => $"itest-a-{_runId}";
    public string TenantBSlug => $"itest-b-{_runId}";
    public string SuspendedSlug => $"itest-sus-{_runId}";
    public string CancelledSlug => $"itest-can-{_runId}";
    public string ProvisioningSlug => $"itest-prov-{_runId}";
    public string UnknownSlug => $"itest-none-{_runId}";

    public string TenantAToken { get; private set; } = default!;
    public string TenantBToken { get; private set; } = default!;

    public async Task InitializeAsync()
    {
        Factory = new WebApplicationFactory<Program>();
        Client = Factory.CreateClient();

        var platformOptions = new DbContextOptionsBuilder<PlatformDbContext>()
            .UseSqlServer(PlatformConnectionString)
            .Options;

        using var platformDb = new PlatformDbContext(platformOptions);

        await CreateActiveTenantAsync(platformDb, TenantASlug);
        await CreateActiveTenantAsync(platformDb, TenantBSlug);

        await CreateBlockedTenantAsync(platformDb, SuspendedSlug, TenantStatus.Suspended);
        await CreateBlockedTenantAsync(platformDb, CancelledSlug, TenantStatus.Cancelled);
        await CreateBlockedTenantAsync(platformDb, ProvisioningSlug, TenantStatus.Provisioning);

        TenantAToken = await LoginAsync(TenantASlug);
        TenantBToken = await LoginAsync(TenantBSlug);
    }

    // Connection string of an active test tenant's own database, for tests
    // that need to look at the data directly instead of through the API.
    public static string ConnectionStringFor(string slug) =>
        $"Data Source=.;Initial Catalog=Syntra_{slug.Replace('-', '_')};Integrated Security=True;TrustServerCertificate=True";

    private async Task CreateActiveTenantAsync(PlatformDbContext platformDb, string slug)
    {
        var databaseName = $"Syntra_{slug.Replace('-', '_')}";
        var connectionString = ConnectionStringFor(slug);

        var tenant = new Tenant
        {
            Name = $"Isolation Test Tenant {slug}",
            Slug = slug,
            DatabaseServer = ".",
            DatabaseName = databaseName,
            ConnectionString = connectionString,
            Status = TenantStatus.Active,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        platformDb.Tenants.Add(tenant);
        await platformDb.SaveChangesAsync();

        _createdTenantIds.Add(tenant.TenantId);
        _createdDatabaseNames.Add(databaseName);

        var tenantOptions = new DbContextOptionsBuilder<TenantDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        using var tenantDb = new TenantDbContext(tenantOptions);
        await tenantDb.Database.MigrateAsync();

        var adminUser = new TenantUser
        {
            UserName = AdminEmail,
            NormalizedUserName = AdminEmail.ToUpperInvariant(),
            Email = AdminEmail,
            NormalizedEmail = AdminEmail.ToUpperInvariant(),
            EmailConfirmed = true,
            SecurityStamp = Guid.NewGuid().ToString()
        };
        adminUser.PasswordHash = new PasswordHasher<TenantUser>().HashPassword(adminUser, AdminPassword);

        tenantDb.Users.Add(adminUser);
        await tenantDb.SaveChangesAsync();

        tenantDb.UserRoles.Add(new IdentityUserRole<int> { UserId = adminUser.Id, RoleId = TenantRoles.AdminRoleId });
        await tenantDb.SaveChangesAsync();
    }

    private async Task CreateBlockedTenantAsync(PlatformDbContext platformDb, string slug, TenantStatus status)
    {
        var tenant = new Tenant
        {
            Name = $"Isolation Test Tenant {slug}",
            Slug = slug,
            DatabaseServer = ".",
            DatabaseName = $"Syntra_{slug.Replace('-', '_')}",
            // Never actually connected to - TenantResolutionMiddleware rejects
            // on Status before the tenant's own database is ever touched.
            ConnectionString =
                "Data Source=.;Initial Catalog=Syntra_NeverCreated;Integrated Security=True;TrustServerCertificate=True",
            Status = status,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        platformDb.Tenants.Add(tenant);
        await platformDb.SaveChangesAsync();

        _createdTenantIds.Add(tenant.TenantId);
    }

    private async Task<string> LoginAsync(string slug)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/auth/login")
        {
            Headers = { Host = $"{slug}.localhost" },
            Content = JsonContent.Create(new { email = AdminEmail, password = AdminPassword })
        };

        var response = await Client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        return body!.Token;
    }

    public HttpRequestMessage CreateRequest(HttpMethod method, string slug, string path, string? bearerToken = null)
    {
        var request = new HttpRequestMessage(method, path)
        {
            Headers = { Host = $"{slug}.localhost" }
        };

        if (bearerToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        }

        return request;
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();
        Factory.Dispose();

        var platformOptions = new DbContextOptionsBuilder<PlatformDbContext>()
            .UseSqlServer(PlatformConnectionString)
            .Options;

        using (var platformDb = new PlatformDbContext(platformOptions))
        {
            var tenants = await platformDb.Tenants
                .Where(t => _createdTenantIds.Contains(t.TenantId))
                .ToListAsync();

            platformDb.Tenants.RemoveRange(tenants);
            await platformDb.SaveChangesAsync();
        }

        foreach (var databaseName in _createdDatabaseNames)
        {
            using var connection = new SqlConnection(
                "Data Source=.;Initial Catalog=master;Integrated Security=True;TrustServerCertificate=True");
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText =
                $"IF DB_ID('{databaseName}') IS NOT NULL " +
                $"BEGIN ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{databaseName}]; END";
            await command.ExecuteNonQueryAsync();
        }
    }

    private record LoginResponse(string Token);
}
