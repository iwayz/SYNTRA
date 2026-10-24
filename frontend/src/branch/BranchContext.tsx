import { createContext, useContext, useState, type ReactNode } from "react";
import { useGetTenancyBranches } from "../api/generated/branches/branches";
import type { Branch } from "../api/generated/syntraApiV1.schemas";
import { useCurrentUser } from "../auth/useCurrentUser";

// The header's "All branches / Branch X" choice. Pages read `branchId` and pass
// it as the `branchId` query param, so one setting narrows every list and the
// warehouse dropdowns in the create forms. The server still enforces access;
// this only narrows what a user is allowed to see anyway.
type BranchContextValue = {
  // False until the user and their visible branches have loaded, so pages don't
  // fire a first request without the filter and then repeat it with it.
  ready: boolean;
  // undefined = all (visible) branches.
  branchId: number | undefined;
  setBranchId: (id: number | null) => void;
  // The branches this user can see (the API already limits the list).
  branches: Branch[];
  branchName: (id: number | string | undefined) => string | undefined;
  isBranchActive: (id: number | string | undefined) => boolean;
};

const NOT_READY: BranchContextValue = {
  ready: false,
  branchId: undefined,
  setBranchId: () => {},
  branches: [],
  branchName: () => undefined,
  isBranchActive: () => true,
};

const BranchContext = createContext<BranchContextValue>(NOT_READY);

const storageKey = (userId: number) => `syntra.branch.${userId}`;

function readStored(userId: number): number | null {
  try {
    const raw = localStorage.getItem(storageKey(userId));
    return raw ? Number(raw) : null;
  } catch {
    return null;
  }
}

function BranchProviderInner({ userId, children }: { userId: number; children: ReactNode }) {
  const branchesQuery = useGetTenancyBranches();
  const branches = branchesQuery.data ?? [];
  const [selected, setSelected] = useState<number | null>(() => readStored(userId));

  // A remembered branch only counts while it is still one of the user's visible
  // branches (access may have been revoked), and the choice only means anything
  // when there is more than one branch to choose between.
  const visibleIds = branches.map((branch) => Number(branch.branchId));
  const branchId = branches.length > 1 && selected !== null && visibleIds.includes(selected) ? selected : undefined;

  function setBranchId(id: number | null) {
    setSelected(id);
    try {
      if (id === null) localStorage.removeItem(storageKey(userId));
      else localStorage.setItem(storageKey(userId), String(id));
    } catch {
      // Remembering the choice is a convenience; ignore storage failures.
    }
  }

  const find = (id: number | string | undefined) =>
    id === undefined ? undefined : branches.find((branch) => Number(branch.branchId) === Number(id));

  const value: BranchContextValue = {
    ready: !branchesQuery.isPending,
    branchId,
    setBranchId,
    branches,
    branchName: (id) => find(id)?.name,
    isBranchActive: (id) => find(id)?.isActive ?? true,
  };

  return <BranchContext.Provider value={value}>{children}</BranchContext.Provider>;
}

export function BranchProvider({ children }: { children: ReactNode }) {
  const { isLoaded, userId } = useCurrentUser();

  if (!isLoaded || userId === null) {
    return <BranchContext.Provider value={NOT_READY}>{children}</BranchContext.Provider>;
  }

  // Keyed by user so a different user signing in on the same page starts fresh.
  return (
    <BranchProviderInner key={userId} userId={userId}>
      {children}
    </BranchProviderInner>
  );
}

export function useBranch() {
  return useContext(BranchContext);
}
