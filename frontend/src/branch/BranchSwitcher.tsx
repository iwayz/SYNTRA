import { useBranch } from "./BranchContext";

export function BranchSwitcher() {
  const { branches, branchId, setBranchId } = useBranch();

  // Nothing to choose between with zero or one branch.
  if (branches.length < 2) return null;

  return (
    <select
      value={branchId ?? ""}
      onChange={(e) => setBranchId(e.target.value === "" ? null : Number(e.target.value))}
      aria-label="Branch"
      className="rounded-md border border-slate-300 px-2 py-1.5 text-sm text-slate-700 focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
    >
      <option value="">All branches</option>
      {branches.map((branch) => (
        <option key={branch.branchId} value={branch.branchId}>
          {branch.name}
          {branch.isActive ? "" : " (inactive)"}
        </option>
      ))}
    </select>
  );
}
