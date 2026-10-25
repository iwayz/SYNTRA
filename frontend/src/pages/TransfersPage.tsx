import { Link } from "react-router-dom";
import { useGetInventoryTransfers } from "../api/generated/stock-transfers/stock-transfers";
import { useBranch } from "../branch/BranchContext";
import { StatusBadge } from "../components/StatusBadge";

export function TransfersPage() {
  const { branchId } = useBranch();
  const transfersQuery = useGetInventoryTransfers({ branchId });

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <h2 className="text-lg font-semibold text-slate-900">Stock Transfers</h2>
        <Link
          to="/transfers/new"
          className="rounded-md bg-indigo-600 px-4 py-2 text-sm font-medium text-white transition hover:bg-indigo-700"
        >
          New Transfer
        </Link>
      </div>

      <div className="rounded-xl bg-white p-6 shadow-sm ring-1 ring-slate-200">
        <table className="w-full text-left text-sm">
          <thead>
            <tr className="border-b border-slate-200 text-slate-500">
              <th className="pb-2 pr-4 font-medium">#</th>
              <th className="pb-2 pr-4 font-medium">From</th>
              <th className="pb-2 pr-4 font-medium">To</th>
              <th className="pb-2 pr-4 font-medium">Created</th>
              <th className="pb-2 pr-4 font-medium">Status</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            {transfersQuery.data?.map((transfer) => (
              <tr key={transfer.stockTransferId} className="border-b border-slate-100 last:border-0">
                <td className="py-2 pr-4 text-slate-500">{transfer.stockTransferId}</td>
                <td className="py-2 pr-4 text-slate-700">
                  {transfer.fromWarehouse?.name}{" "}
                  <span className="text-slate-400">· {transfer.fromWarehouse?.branch?.name}</span>
                </td>
                <td className="py-2 pr-4 text-slate-700">
                  {transfer.toWarehouse?.name} <span className="text-slate-400">· {transfer.toWarehouse?.branch?.name}</span>
                </td>
                <td className="py-2 pr-4 text-slate-500">
                  {transfer.createdAt && new Date(transfer.createdAt).toLocaleDateString()}
                </td>
                <td className="py-2 pr-4">
                  <StatusBadge status={transfer.status} />
                </td>
                <td className="py-2 text-right">
                  <Link
                    to={`/transfers/${transfer.stockTransferId}`}
                    className="text-sm font-medium text-indigo-600 hover:underline"
                  >
                    View
                  </Link>
                </td>
              </tr>
            ))}
            {transfersQuery.data?.length === 0 && (
              <tr>
                <td colSpan={6} className="py-4 text-slate-400">
                  No transfers yet.
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>
    </div>
  );
}
