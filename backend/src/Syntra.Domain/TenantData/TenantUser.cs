using Microsoft.AspNetCore.Identity;

namespace Syntra.Domain.TenantData;

// A tenant's own end-user account. Lives inside that tenant's database only -
// never centralized in the Platform DB. Thin subclass now, room to extend later.
public class TenantUser : IdentityUser<int>
{
}
