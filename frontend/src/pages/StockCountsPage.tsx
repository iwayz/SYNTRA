import { useQueryClient } from "@tanstack/react-query";
import { useState, type FormEvent } from "react";
import { Link, useNavigate } from "react-router-dom";
import { errorMessage } from "../api/errors";
import {
  getGetInventoryStockCountsQueryKey,
  useGetInventoryStockCounts,
  usePostInventoryStockCounts,
} from "../api/generated/stock-counts/stock-counts";
import { useGetInventoryWarehouses } from "../api/generated/warehouses/warehouses";
import { useBranch } from "../branch/BranchContext";
import { StatusBadge } from "../components/StatusBadge";

export function StockCountsPage() {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { branchId } = useBranch();

  const countsQuery = useGetInventoryStockCounts({ branchId });
  const warehousesQuery = useGetInventoryWarehouses({ branchId });
  const [warehouseId, setWarehouseId] = useState("");

  const createMutation = usePostInventoryStockCounts({
    mutation: {
      onSuccess: (detail) => {
        queryClient.invalidateQueries({ queryKey: getGetInventoryStockCountsQueryKey() });
        navigate(`/stock-counts/${detail.summary?.stockCountId}`);
      },
    },
  });

  function handleStart(event: FormEvent) {
    event.preventDefault();
    createMutation.mutate({ data: { warehouseId: Number(warehouseId), notes: null } });
  }

  return (
    <div className="space-y-6">
      <h2 className="text-lg font-semibold text-slate-900">Stock Counts</h2>

      <div className="rounded-xl bg-white p-6 shadow-sm ring-1 ring-slate-200">
        <table className="w-full text-left text-sm">
          <thead>
            <tr className="border-b border-slate-200 text-slate-500">
              <th className="pb-2 pr-4 font-medium">#</th>
              <th className="pb-2 pr-4 font-medium">Warehouse</th>
              <th className="pb-2 pr-4 font-medium">Started</th>
              <th className="pb-2 pr-4 font-medium">Counted</th>
              <th className="pb-2 pr-4 font-medium">Status</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            {countsQuery.data?.map((count) => (
              <tr key={count.stockCountId} className="border-b border-slate-100 last:border-0">
                <td className="py-2 pr-4 text-slate-500">{count.stockCountId}</td>
                <td className="py-2 pr-4 text-slate-700">
                  {count.warehouseName} <span className="text-slate-400">· {count.branchName}</span>
                </td>
                <td className="py-2 pr-4 text-slate-500">
                  {count.createdAt && new Date(count.createdAt).toLocaleDateString()}
                </td>
                <td className="py-2 pr-4 text-slate-700">
                  {count.countedCount} of {count.rowCount}
                </td>
                <td className="py-2 pr-4">
                  <StatusBadge status={count.status} />
                </td>
                <td className="py-2 text-right">
                  <Link
                    to={`/stock-counts/${count.stockCountId}`}
                    className="text-sm font-medium text-indigo-600 hover:underline"
                  >
                    {count.status === "Open" ? "Continue" : "View"}
                  </Link>
                </td>
              </tr>
            ))}
            {countsQuery.data?.length === 0 && (
              <tr>
                <td colSpan={6} className="py-4 text-slate-400">
                  No stock counts yet.
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>

      <div className="rounded-xl bg-white p-6 shadow-sm ring-1 ring-slate-200">
        <h3 className="mb-1 text-sm font-semibold text-slate-900">Start a count</h3>
        <p className="mb-4 text-xs text-slate-400">
          Lists everything the warehouse holds right now. Trading can carry on while you count; a row whose stock moves
          after you counted it will ask to be counted again.
        </p>
        <form onSubmit={handleStart} className="flex items-end gap-3">
          <div>
            <label className="mb-1 block text-sm font-medium text-slate-700">Warehouse</label>
            <select
              value={warehouseId}
              onChange={(e) => {
                setWarehouseId(e.target.value);
                createMutation.reset();
              }}
              required
              className="rounded-md border border-slate-300 px-3 py-2 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
            >
              <option value="" disabled>
                Select...
              </option>
              {warehousesQuery.data?.map((warehouse) => {
                const branchInactive = warehouse.branch?.isActive === false;
                return (
                  <option key={warehouse.warehouseId} value={warehouse.warehouseId} disabled={branchInactive}>
                    {warehouse.name} · {warehouse.branch?.name}
                    {branchInactive ? " (inactive branch)" : ""}
                  </option>
                );
              })}
            </select>
          </div>
          <button
            type="submit"
            disabled={createMutation.isPending}
            className="rounded-md bg-indigo-600 px-4 py-2 text-sm font-medium text-white transition hover:bg-indigo-700 disabled:cursor-not-allowed disabled:opacity-60"
          >
            Start count
          </button>
        </form>
        {createMutation.isError && (
          <p className="mt-2 text-sm text-red-600">{errorMessage(createMutation.error)}</p>
        )}
      </div>
    </div>
  );
}
