using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Syntra.Infrastructure.TenantData;

// Used only by `dotnet ef migrations` at design time. The connection string
// here is a placeholder - at runtime every tenant supplies its own via
// Tenant.ConnectionString, never this one.
public class TenantDbContextFactory : IDesignTimeDbContextFactory<TenantDbContext>
{
    public TenantDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<TenantDbContext>();
        optionsBuilder.UseSqlServer("Data Source=.;Initial Catalog=Syntra_DesignTime;Integrated Security=True;TrustServerCertificate=True");
        return new TenantDbContext(optionsBuilder.Options);
    }
}
