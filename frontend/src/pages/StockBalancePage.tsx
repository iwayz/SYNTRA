import { useGetInventoryStockBalance } from "../api/generated/stock/stock";
import { useBranch } from "../branch/BranchContext";
import { ExpiryBadge } from "../components/ExpiryBadge";

export function StockBalancePage() {
  const { branchId } = useBranch();
  const balanceQuery = useGetInventoryStockBalance({ branchId });

  return (
    <div className="space-y-6">
      <h2 className="text-lg font-semibold text-slate-900">Stock Balance</h2>

      <div className="rounded-xl bg-white p-6 shadow-sm ring-1 ring-slate-200">
        <table className="w-full text-left text-sm">
          <thead>
            <tr className="border-b border-slate-200 text-slate-500">
              <th className="pb-2 font-medium">SKU</th>
              <th className="pb-2 font-medium">Product</th>
              <th className="pb-2 font-medium">Branch</th>
              <th className="pb-2 font-medium">Warehouse</th>
              <th className="pb-2 font-medium">Batch</th>
              <th className="pb-2 font-medium">Expiry</th>
              <th className="pb-2 text-right font-medium">On hand</th>
            </tr>
          </thead>
          <tbody>
            {balanceQuery.data?.map((balance) => (
              <tr
                key={`${balance.productId}-${balance.warehouseId}-${balance.batchId ?? "none"}`}
                className="border-b border-slate-100 last:border-0"
              >
                <td className="py-2 text-slate-700">{balance.productSku}</td>
                <td className="py-2 text-slate-700">{balance.productName}</td>
                <td className="py-2 text-slate-700">{balance.branchName}</td>
                <td className="py-2 text-slate-700">{balance.warehouseName}</td>
                <td className="py-2 text-slate-700">{balance.batchNumber ?? <span className="text-slate-400">-</span>}</td>
                <td className="py-2">
                  <ExpiryBadge expiry={balance.expiryDate} />
                </td>
                <td className="py-2 text-right font-medium text-slate-900">{balance.quantityOnHand}</td>
              </tr>
            ))}
            {balanceQuery.data?.length === 0 && (
              <tr>
                <td colSpan={7} className="py-4 text-slate-400">
                  No stock movements recorded yet.
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>
    </div>
  );
}
