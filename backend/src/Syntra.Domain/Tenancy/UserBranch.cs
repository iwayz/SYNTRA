namespace Syntra.Domain.Tenancy;

// Grants a non-admin user access to one branch. Admins see every branch and
// need no rows here; any rows they do have are ignored while they are admin.
public class UserBranch
{
    public int UserId { get; set; }
    public int BranchId { get; set; }
}
