import { Link } from "react-router-dom";
import { useGetPurchasingPurchaseOrders } from "../api/generated/purchase-orders/purchase-orders";
import { StatusBadge } from "../components/StatusBadge";

export function PurchaseOrdersPage() {
  const ordersQuery = useGetPurchasingPurchaseOrders();

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <h2 className="text-lg font-semibold text-slate-900">Purchase Orders</h2>
        <Link
          to="/purchase-orders/new"
          className="rounded-md bg-indigo-600 px-4 py-2 text-sm font-medium text-white transition hover:bg-indigo-700"
        >
          New Purchase Order
        </Link>
      </div>

      <div className="rounded-xl bg-white p-6 shadow-sm ring-1 ring-slate-200">
        <table className="w-full text-left text-sm">
          <thead>
            <tr className="border-b border-slate-200 text-slate-500">
              <th className="pb-2 pr-4 font-medium">Supplier</th>
              <th className="pb-2 pr-4 font-medium">Warehouse</th>
              <th className="pb-2 pr-4 font-medium">Order date</th>
              <th className="pb-2 pr-4 font-medium">Status</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            {ordersQuery.data?.map((order) => (
              <tr key={order.purchaseOrderId} className="border-b border-slate-100 last:border-0">
                <td className="py-2 pr-4 text-slate-700">{order.supplier?.name}</td>
                <td className="py-2 pr-4 text-slate-700">{order.warehouse?.name}</td>
                <td className="py-2 pr-4 text-slate-500">
                  {order.orderDate && new Date(order.orderDate).toLocaleDateString()}
                </td>
                <td className="py-2 pr-4">
                  <StatusBadge status={order.status} />
                </td>
                <td className="py-2 text-right">
                  <Link
                    to={`/purchase-orders/${order.purchaseOrderId}`}
                    className="text-sm font-medium text-indigo-600 hover:underline"
                  >
                    View
                  </Link>
                </td>
              </tr>
            ))}
            {ordersQuery.data?.length === 0 && (
              <tr>
                <td colSpan={5} className="py-4 text-slate-400">
                  No purchase orders yet.
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>
    </div>
  );
}
