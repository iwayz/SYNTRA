using Microsoft.AspNetCore.Identity;

namespace Syntra.Domain.TenantData;

// A tenant's own end-user account. Lives inside that tenant's database only -
// never centralized in the Platform DB. Thin subclass now, room to extend later.
public class TenantUser : IdentityUser<int>
{
    // Deactivated users can't log in, and an already-issued token stops
    // working on the next request (checked per request, not read from the JWT).
    public bool IsActive { get; set; } = true;
}
