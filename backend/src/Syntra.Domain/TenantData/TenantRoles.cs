namespace Syntra.Domain.TenantData;

// Well-known role seeded into every tenant database. Shared constant so both
// the schema seed (TenantDbContext) and anything assigning the role
// (Syntra.TenantProvisioner) stay in sync without hardcoding the id twice.
public static class TenantRoles
{
    public const int AdminRoleId = 1;
    public const string AdminRoleName = "Admin";
}
