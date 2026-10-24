import { useGetTenancyBranches } from "../api/generated/branches/branches";
import { useCurrentUser } from "../auth/useCurrentUser";

export function DashboardPage() {
  const { isLoaded, email, isAdmin } = useCurrentUser();
  const branchesQuery = useGetTenancyBranches();

  // The branches endpoint already returns only the branches this user may see.
  const branches = branchesQuery.data ?? [];

  return (
    <div className="rounded-xl bg-white p-6 shadow-sm ring-1 ring-slate-200">
      <h2 className="text-lg font-semibold text-slate-900">Welcome to Syntra</h2>
      {isLoaded && (
        <div className="mt-3 space-y-1 text-sm text-slate-600">
          <p>
            Signed in as <span className="font-medium text-slate-900">{email}</span> ·{" "}
            <span className="font-medium text-slate-900">{isAdmin ? "Admin" : "Staff"}</span>
          </p>
          <p>
            {isAdmin ? "You can see every branch" : "You can see only your assigned branches"}
            {branches.length > 0 && (
              <>
                :{" "}
                <span className="font-medium text-slate-900">
                  {branches.map((branch) => (branch.isActive ? branch.name : `${branch.name} (inactive)`)).join(", ")}
                </span>
              </>
            )}
            .
          </p>
        </div>
      )}
    </div>
  );
}
