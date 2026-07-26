namespace Syntra.Domain.Platform;

// Schema only for now. Real password hashing, login, and JWT issuance
// belong to the auth-skeleton design (Phase 0 item 5), not yet built.
public class PlatformUser
{
    public int PlatformUserId { get; set; }
    public string Email { get; set; } = default!;
    public string PasswordHash { get; set; } = default!;
    public string Role { get; set; } = default!;
}
