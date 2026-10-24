import { useQueryClient } from "@tanstack/react-query";
import { useState, type FormEvent } from "react";
import { errorMessage } from "../api/errors";
import {
  getGetTenancyBranchesQueryKey,
  useGetTenancyBranches,
  usePostTenancyBranches,
  usePutTenancyBranchesId,
} from "../api/generated/branches/branches";

const inputClasses =
  "rounded-md border border-slate-300 px-3 py-2 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500";

export function BranchesPage() {
  const queryClient = useQueryClient();
  const branchesQuery = useGetTenancyBranches();

  const [code, setCode] = useState("");
  const [name, setName] = useState("");

  const [editingId, setEditingId] = useState<number | null>(null);
  const [editCode, setEditCode] = useState("");
  const [editName, setEditName] = useState("");
  const [editActive, setEditActive] = useState(true);

  function refresh() {
    queryClient.invalidateQueries({ queryKey: getGetTenancyBranchesQueryKey() });
  }

  const createMutation = usePostTenancyBranches({
    mutation: {
      onSuccess: () => {
        setCode("");
        setName("");
        refresh();
      },
    },
  });

  const updateMutation = usePutTenancyBranchesId({
    mutation: {
      onSuccess: () => {
        setEditingId(null);
        refresh();
      },
    },
  });

  function handleCreate(event: FormEvent) {
    event.preventDefault();
    createMutation.mutate({ data: { code, name } });
  }

  function startEdit(branch: { branchId?: number | string; code?: string; name?: string; isActive?: boolean }) {
    updateMutation.reset();
    setEditingId(Number(branch.branchId));
    setEditCode(branch.code ?? "");
    setEditName(branch.name ?? "");
    setEditActive(branch.isActive ?? true);
  }

  function handleSave(id: number) {
    updateMutation.mutate({ id, data: { code: editCode, name: editName, isActive: editActive } });
  }

  return (
    <div className="space-y-6">
      <h2 className="text-lg font-semibold text-slate-900">Branches</h2>

      <div className="rounded-xl bg-white p-6 shadow-sm ring-1 ring-slate-200">
        <table className="w-full text-left text-sm">
          <thead>
            <tr className="border-b border-slate-200 text-slate-500">
              <th className="pb-2 pr-4 font-medium">Code</th>
              <th className="pb-2 pr-4 font-medium">Name</th>
              <th className="pb-2 pr-4 font-medium">Status</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            {branchesQuery.data?.map((branch) => {
              const id = Number(branch.branchId);
              const isEditing = editingId === id;

              return (
                <tr key={id} className="border-b border-slate-100 last:border-0">
                  {isEditing ? (
                    <>
                      <td className="py-2 pr-4">
                        <input
                          value={editCode}
                          onChange={(e) => setEditCode(e.target.value)}
                          maxLength={20}
                          className={`${inputClasses} w-28`}
                        />
                      </td>
                      <td className="py-2 pr-4">
                        <input value={editName} onChange={(e) => setEditName(e.target.value)} className={inputClasses} />
                      </td>
                      <td className="py-2 pr-4">
                        <label className="flex items-center gap-2 text-slate-700">
                          <input
                            type="checkbox"
                            checked={editActive}
                            onChange={(e) => setEditActive(e.target.checked)}
                          />
                          Active
                        </label>
                      </td>
                      <td className="py-2 text-right whitespace-nowrap">
                        <button
                          onClick={() => handleSave(id)}
                          disabled={updateMutation.isPending}
                          className="mr-3 text-sm font-medium text-indigo-600 hover:underline disabled:opacity-60"
                        >
                          Save
                        </button>
                        <button onClick={() => setEditingId(null)} className="text-sm text-slate-500 hover:underline">
                          Cancel
                        </button>
                      </td>
                    </>
                  ) : (
                    <>
                      <td className="py-2 pr-4 text-slate-700">{branch.code}</td>
                      <td className="py-2 pr-4 text-slate-700">{branch.name}</td>
                      <td className="py-2 pr-4">
                        <span
                          className={`rounded-full px-2.5 py-0.5 text-xs font-medium ${
                            branch.isActive ? "bg-emerald-100 text-emerald-700" : "bg-slate-100 text-slate-500"
                          }`}
                        >
                          {branch.isActive ? "Active" : "Inactive"}
                        </span>
                      </td>
                      <td className="py-2 text-right">
                        <button
                          onClick={() => startEdit(branch)}
                          className="text-sm font-medium text-indigo-600 hover:underline"
                        >
                          Edit
                        </button>
                      </td>
                    </>
                  )}
                </tr>
              );
            })}
            {branchesQuery.data?.length === 0 && (
              <tr>
                <td colSpan={4} className="py-4 text-slate-400">
                  No branches yet.
                </td>
              </tr>
            )}
          </tbody>
        </table>
        {updateMutation.isError && <p className="mt-3 text-sm text-red-600">{errorMessage(updateMutation.error)}</p>}
        <p className="mt-4 text-xs text-slate-400">
          An inactive branch keeps its history and lets existing orders be finished, but takes no new warehouses, orders
          or stock adjustments.
        </p>
      </div>

      <div className="rounded-xl bg-white p-6 shadow-sm ring-1 ring-slate-200">
        <h3 className="mb-4 text-sm font-semibold text-slate-900">Add a branch</h3>
        <form onSubmit={handleCreate} className="flex flex-wrap items-end gap-3">
          <div>
            <label className="mb-1 block text-sm font-medium text-slate-700">Code</label>
            <input
              value={code}
              onChange={(e) => {
                setCode(e.target.value);
                createMutation.reset();
              }}
              required
              maxLength={20}
              className={`${inputClasses} w-28`}
            />
          </div>
          <div>
            <label className="mb-1 block text-sm font-medium text-slate-700">Name</label>
            <input
              value={name}
              onChange={(e) => {
                setName(e.target.value);
                createMutation.reset();
              }}
              required
              className={inputClasses}
            />
          </div>
          <button
            type="submit"
            disabled={createMutation.isPending}
            className="rounded-md bg-indigo-600 px-4 py-2 text-sm font-medium text-white transition hover:bg-indigo-700 disabled:cursor-not-allowed disabled:opacity-60"
          >
            Add
          </button>
        </form>
        {createMutation.isError && (
          <p className="mt-2 text-sm text-red-600">
            {errorMessage(createMutation.error, "Could not create branch - check the values and try again.")}
          </p>
        )}
      </div>
    </div>
  );
}
