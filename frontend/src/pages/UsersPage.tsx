import { useQueryClient } from "@tanstack/react-query";
import { Fragment, useState, type FormEvent } from "react";
import { errorMessage } from "../api/errors";
import { useGetTenancyBranches } from "../api/generated/branches/branches";
import {
  getGetTenancyUsersQueryKey,
  useGetTenancyUsers,
  usePostTenancyUsers,
  usePostTenancyUsersIdResetPassword,
  usePutTenancyUsersIdActive,
  usePutTenancyUsersIdAdmin,
  usePutTenancyUsersIdBranches,
} from "../api/generated/users/users";
import type { Branch } from "../api/generated/syntraApiV1.schemas";
import { useCurrentUser } from "../auth/useCurrentUser";

const inputClasses =
  "rounded-md border border-slate-300 px-3 py-2 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500";
const linkButtonClasses = "text-sm font-medium text-indigo-600 hover:underline disabled:cursor-not-allowed disabled:text-slate-300 disabled:no-underline";

type Panel = { userId: number; kind: "branches" | "password" } | null;

function BranchCheckboxes({
  branches,
  selected,
  onChange,
}: {
  branches: Branch[];
  selected: number[];
  onChange: (next: number[]) => void;
}) {
  return (
    <div className="flex flex-wrap gap-x-4 gap-y-1">
      {branches.map((branch) => {
        const id = Number(branch.branchId);
        return (
          <label key={id} className="flex items-center gap-2 text-sm text-slate-700">
            <input
              type="checkbox"
              checked={selected.includes(id)}
              onChange={(e) => onChange(e.target.checked ? [...selected, id] : selected.filter((b) => b !== id))}
            />
            {branch.name}
            {!branch.isActive && <span className="text-xs text-slate-400">(inactive)</span>}
          </label>
        );
      })}
      {branches.length === 0 && <span className="text-sm text-slate-400">No branches yet.</span>}
    </div>
  );
}

export function UsersPage() {
  const queryClient = useQueryClient();
  const { userId: currentUserId } = useCurrentUser();
  const usersQuery = useGetTenancyUsers();
  const branchesQuery = useGetTenancyBranches();
  const branches = branchesQuery.data ?? [];

  const [panel, setPanel] = useState<Panel>(null);
  const [panelBranchIds, setPanelBranchIds] = useState<number[]>([]);
  const [panelPassword, setPanelPassword] = useState("");
  const [actionError, setActionError] = useState<string | null>(null);

  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [isAdmin, setIsAdmin] = useState(false);
  const [newBranchIds, setNewBranchIds] = useState<number[]>([]);

  function refresh() {
    queryClient.invalidateQueries({ queryKey: getGetTenancyUsersQueryKey() });
  }

  const actionHandlers = {
    onSuccess: () => {
      setActionError(null);
      setPanel(null);
      setPanelPassword("");
      refresh();
    },
    onError: (error: unknown) => setActionError(errorMessage(error)),
  };

  const createMutation = usePostTenancyUsers({
    mutation: {
      onSuccess: () => {
        setEmail("");
        setPassword("");
        setIsAdmin(false);
        setNewBranchIds([]);
        refresh();
      },
    },
  });
  const branchesMutation = usePutTenancyUsersIdBranches({ mutation: actionHandlers });
  const activeMutation = usePutTenancyUsersIdActive({ mutation: actionHandlers });
  const adminMutation = usePutTenancyUsersIdAdmin({ mutation: actionHandlers });
  const passwordMutation = usePostTenancyUsersIdResetPassword({ mutation: actionHandlers });

  const isBusy =
    branchesMutation.isPending || activeMutation.isPending || adminMutation.isPending || passwordMutation.isPending;

  function branchNames(ids: (number | string)[]) {
    const names = ids
      .map((id) => branches.find((branch) => Number(branch.branchId) === Number(id))?.name)
      .filter((name): name is string => !!name);
    return names.length > 0 ? names.join(", ") : "None";
  }

  function openPanel(userId: number, kind: "branches" | "password", currentBranchIds: (number | string)[] = []) {
    setActionError(null);
    setPanel({ userId, kind });
    setPanelBranchIds(currentBranchIds.map(Number));
    setPanelPassword("");
  }

  function handleCreate(event: FormEvent) {
    event.preventDefault();
    createMutation.mutate({ data: { email, password, isAdmin, branchIds: isAdmin ? [] : newBranchIds } });
  }

  return (
    <div className="space-y-6">
      <h2 className="text-lg font-semibold text-slate-900">Users</h2>

      <div className="rounded-xl bg-white p-6 shadow-sm ring-1 ring-slate-200">
        {actionError && <p className="mb-3 text-sm text-red-600">{actionError}</p>}
        <table className="w-full text-left text-sm">
          <thead>
            <tr className="border-b border-slate-200 text-slate-500">
              <th className="pb-2 pr-4 font-medium">Email</th>
              <th className="pb-2 pr-4 font-medium">Role</th>
              <th className="pb-2 pr-4 font-medium">Branches</th>
              <th className="pb-2 pr-4 font-medium">Status</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            {usersQuery.data?.map((user) => {
              const id = Number(user.userId);
              const isSelf = id === currentUserId;
              const isPanelOpen = panel?.userId === id;

              return (
                <Fragment key={id}>
                  <tr className="border-b border-slate-100 last:border-0">
                    <td className="py-2 pr-4 text-slate-700">
                      {user.email}
                      {isSelf && <span className="ml-2 text-xs text-slate-400">(you)</span>}
                    </td>
                    <td className="py-2 pr-4">
                      <span
                        className={`rounded-full px-2.5 py-0.5 text-xs font-medium ${
                          user.isAdmin ? "bg-indigo-100 text-indigo-700" : "bg-slate-100 text-slate-700"
                        }`}
                      >
                        {user.isAdmin ? "Admin" : "Staff"}
                      </span>
                    </td>
                    <td className="py-2 pr-4 text-slate-700">
                      {user.isAdmin ? <span className="text-slate-400">All branches</span> : branchNames(user.branchIds)}
                    </td>
                    <td className="py-2 pr-4">
                      <span
                        className={`rounded-full px-2.5 py-0.5 text-xs font-medium ${
                          user.isActive ? "bg-emerald-100 text-emerald-700" : "bg-slate-100 text-slate-500"
                        }`}
                      >
                        {user.isActive ? "Active" : "Inactive"}
                      </span>
                    </td>
                    <td className="py-2 text-right whitespace-nowrap">
                      <span className="inline-flex gap-4">
                        <button
                          onClick={() => openPanel(id, "branches", user.branchIds)}
                          disabled={isBusy}
                          className={linkButtonClasses}
                        >
                          Branches
                        </button>
                        <button
                          onClick={() => adminMutation.mutate({ id, data: { isAdmin: !user.isAdmin } })}
                          disabled={isBusy || (isSelf && user.isAdmin)}
                          title={isSelf && user.isAdmin ? "You cannot remove your own admin role" : undefined}
                          className={linkButtonClasses}
                        >
                          {user.isAdmin ? "Remove admin" : "Make admin"}
                        </button>
                        <button
                          onClick={() => activeMutation.mutate({ id, data: { isActive: !user.isActive } })}
                          disabled={isBusy || (isSelf && user.isActive)}
                          title={isSelf && user.isActive ? "You cannot deactivate your own account" : undefined}
                          className={linkButtonClasses}
                        >
                          {user.isActive ? "Deactivate" : "Activate"}
                        </button>
                        <button onClick={() => openPanel(id, "password")} disabled={isBusy} className={linkButtonClasses}>
                          Reset password
                        </button>
                      </span>
                    </td>
                  </tr>
                  {isPanelOpen && (
                    <tr className="border-b border-slate-100 bg-slate-50">
                      <td colSpan={5} className="px-4 py-3">
                        {panel.kind === "branches" ? (
                          <div className="space-y-3">
                            <p className="text-sm text-slate-600">
                              Branches for {user.email}
                              {user.isAdmin && " (admins see every branch; this only applies if they stop being an admin)"}
                            </p>
                            <BranchCheckboxes branches={branches} selected={panelBranchIds} onChange={setPanelBranchIds} />
                            <div className="flex gap-3">
                              <button
                                onClick={() => branchesMutation.mutate({ id, data: { branchIds: panelBranchIds } })}
                                disabled={isBusy}
                                className="rounded-md bg-indigo-600 px-3 py-1.5 text-sm font-medium text-white hover:bg-indigo-700 disabled:opacity-60"
                              >
                                Save
                              </button>
                              <button onClick={() => setPanel(null)} className="text-sm text-slate-500 hover:underline">
                                Cancel
                              </button>
                            </div>
                          </div>
                        ) : (
                          <form
                            onSubmit={(e) => {
                              e.preventDefault();
                              passwordMutation.mutate({ id, data: { newPassword: panelPassword } });
                            }}
                            className="flex flex-wrap items-end gap-3"
                          >
                            <div>
                              <label className="mb-1 block text-sm text-slate-600">New password for {user.email}</label>
                              <input
                                type="password"
                                value={panelPassword}
                                onChange={(e) => setPanelPassword(e.target.value)}
                                required
                                autoComplete="new-password"
                                className={inputClasses}
                              />
                            </div>
                            <button
                              type="submit"
                              disabled={isBusy}
                              className="rounded-md bg-indigo-600 px-3 py-2 text-sm font-medium text-white hover:bg-indigo-700 disabled:opacity-60"
                            >
                              Set password
                            </button>
                            <button type="button" onClick={() => setPanel(null)} className="text-sm text-slate-500 hover:underline">
                              Cancel
                            </button>
                          </form>
                        )}
                      </td>
                    </tr>
                  )}
                </Fragment>
              );
            })}
            {usersQuery.data?.length === 0 && (
              <tr>
                <td colSpan={5} className="py-4 text-slate-400">
                  No users yet.
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>

      <div className="rounded-xl bg-white p-6 shadow-sm ring-1 ring-slate-200">
        <h3 className="mb-4 text-sm font-semibold text-slate-900">Add a user</h3>
        <form onSubmit={handleCreate} className="space-y-4">
          <div className="flex flex-wrap gap-4">
            <div>
              <label className="mb-1 block text-sm font-medium text-slate-700">Email</label>
              <input
                type="email"
                value={email}
                onChange={(e) => {
                  setEmail(e.target.value);
                  createMutation.reset();
                }}
                required
                className={`${inputClasses} w-64`}
              />
            </div>
            <div>
              <label className="mb-1 block text-sm font-medium text-slate-700">Initial password</label>
              <input
                type="password"
                value={password}
                onChange={(e) => {
                  setPassword(e.target.value);
                  createMutation.reset();
                }}
                required
                autoComplete="new-password"
                className={`${inputClasses} w-56`}
              />
            </div>
          </div>
          <label className="flex items-center gap-2 text-sm text-slate-700">
            <input type="checkbox" checked={isAdmin} onChange={(e) => setIsAdmin(e.target.checked)} />
            Admin (sees every branch and manages branches and users)
          </label>
          {!isAdmin && (
            <div>
              <label className="mb-1 block text-sm font-medium text-slate-700">Branches</label>
              <BranchCheckboxes branches={branches} selected={newBranchIds} onChange={setNewBranchIds} />
            </div>
          )}
          <button
            type="submit"
            disabled={createMutation.isPending}
            className="rounded-md bg-indigo-600 px-4 py-2 text-sm font-medium text-white transition hover:bg-indigo-700 disabled:cursor-not-allowed disabled:opacity-60"
          >
            Add user
          </button>
        </form>
        {createMutation.isError && (
          <p className="mt-2 text-sm text-red-600">
            {errorMessage(createMutation.error, "Could not create user - check the values and try again.")}
          </p>
        )}
      </div>
    </div>
  );
}
