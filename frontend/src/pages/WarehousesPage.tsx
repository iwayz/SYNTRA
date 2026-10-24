import { useQueryClient } from "@tanstack/react-query";
import { useState, type FormEvent } from "react";
import {
  getGetInventoryWarehousesQueryKey,
  useGetInventoryWarehouses,
  usePostInventoryWarehouses,
} from "../api/generated/warehouses/warehouses";
import { useGetTenancyBranches } from "../api/generated/branches/branches";

export function WarehousesPage() {
  const queryClient = useQueryClient();
  const warehousesQuery = useGetInventoryWarehouses();
  const branchesQuery = useGetTenancyBranches();
  const [name, setName] = useState("");
  const [branchId, setBranchId] = useState("");

  const createMutation = usePostInventoryWarehouses({
    mutation: {
      onSuccess: () => {
        setName("");
        queryClient.invalidateQueries({ queryKey: getGetInventoryWarehousesQueryKey() });
      },
    },
  });

  function handleSubmit(event: FormEvent) {
    event.preventDefault();
    createMutation.mutate({ data: { name, branchId: Number(branchId) } });
  }

  return (
    <div className="space-y-6">
      <h2 className="text-lg font-semibold text-slate-900">Warehouses</h2>

      <div className="rounded-xl bg-white p-6 shadow-sm ring-1 ring-slate-200">
        <table className="w-full text-left text-sm">
          <thead>
            <tr className="border-b border-slate-200 text-slate-500">
              <th className="pb-2 pr-4 font-medium">Name</th>
              <th className="pb-2 font-medium">Branch</th>
            </tr>
          </thead>
          <tbody>
            {warehousesQuery.data?.map((warehouse) => (
              <tr key={warehouse.warehouseId} className="border-b border-slate-100 last:border-0">
                <td className="py-2 pr-4 text-slate-700">{warehouse.name}</td>
                <td className="py-2 text-slate-700">{warehouse.branch?.name}</td>
              </tr>
            ))}
            {warehousesQuery.data?.length === 0 && (
              <tr>
                <td colSpan={2} className="py-4 text-slate-400">No warehouses yet.</td>
              </tr>
            )}
          </tbody>
        </table>
      </div>

      <div className="rounded-xl bg-white p-6 shadow-sm ring-1 ring-slate-200">
        <h3 className="mb-4 text-sm font-semibold text-slate-900">Add a warehouse</h3>
        <form onSubmit={handleSubmit} className="flex items-end gap-3">
          <div>
            <label className="mb-1 block text-sm font-medium text-slate-700">Name</label>
            <input
              value={name}
              onChange={(e) => setName(e.target.value)}
              required
              className="rounded-md border border-slate-300 px-3 py-2 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
            />
          </div>
          <div>
            <label className="mb-1 block text-sm font-medium text-slate-700">Branch</label>
            <select
              value={branchId}
              onChange={(e) => setBranchId(e.target.value)}
              required
              className="rounded-md border border-slate-300 px-3 py-2 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
            >
              <option value="" disabled>
                Select...
              </option>
              {branchesQuery.data
                ?.filter((branch) => branch.isActive)
                .map((branch) => (
                  <option key={branch.branchId} value={branch.branchId}>
                    {branch.name}
                  </option>
                ))}
            </select>
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
          <p className="mt-2 text-sm text-red-600">Could not create warehouse - check the values and try again.</p>
        )}
      </div>
    </div>
  );
}
