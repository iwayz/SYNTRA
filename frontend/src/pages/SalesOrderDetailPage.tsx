import { useQueryClient } from "@tanstack/react-query";
import { isAxiosError } from "axios";
import { Link, useParams } from "react-router-dom";
import {
  getGetSalesSalesOrdersIdQueryKey,
  useGetSalesSalesOrdersId,
  usePostSalesSalesOrdersIdCancel,
  usePostSalesSalesOrdersIdConfirm,
} from "../api/generated/sales-orders/sales-orders";
import {
  getGetInventoryStockBalanceQueryKey,
  getGetInventoryStockMovementsQueryKey,
} from "../api/generated/stock/stock";
import { SalesOrderStatus } from "../api/generated/syntraApiV1.schemas";
import { StatusBadge } from "../components/StatusBadge";

// The API returns RFC 9457 problem details; surface its `detail` text (e.g. the
// oversell message) instead of a generic failure.
function errorMessage(error: unknown): string {
  if (isAxiosError(error) && typeof error.response?.data?.detail === "string") {
    return error.response.data.detail;
  }
  return "Something went wrong - please try again.";
}

export function SalesOrderDetailPage() {
  const { id } = useParams<{ id: string }>();
  const orderId = Number(id);
  const queryClient = useQueryClient();

  const orderQuery = useGetSalesSalesOrdersId(orderId);

  function invalidateOrder() {
    queryClient.invalidateQueries({ queryKey: getGetSalesSalesOrdersIdQueryKey(orderId) });
  }

  const confirmMutation = usePostSalesSalesOrdersIdConfirm({
    mutation: {
      onSuccess: () => {
        invalidateOrder();
        queryClient.invalidateQueries({ queryKey: getGetInventoryStockMovementsQueryKey() });
        queryClient.invalidateQueries({ queryKey: getGetInventoryStockBalanceQueryKey() });
      },
    },
  });

  const cancelMutation = usePostSalesSalesOrdersIdCancel({
    mutation: { onSuccess: invalidateOrder },
  });

  const order = orderQuery.data;

  if (!order) {
    return <p className="text-slate-500">Loading...</p>;
  }

  const isPending = confirmMutation.isPending || cancelMutation.isPending;

  return (
    <div className="space-y-6">
      <div>
        <Link to="/sales-orders" className="text-sm text-indigo-600 hover:underline">
          ← Sales Orders
        </Link>
        <div className="mt-1 flex items-center gap-3">
          <h2 className="text-lg font-semibold text-slate-900">
            {order.customer?.name} <span className="font-normal text-slate-400">#{order.salesOrderId}</span>
          </h2>
          <StatusBadge status={order.status} />
        </div>
        <p className="text-sm text-slate-500">
          Warehouse: {order.warehouse?.name} · Order date:{" "}
          {order.orderDate && new Date(order.orderDate).toLocaleDateString()}
        </p>
      </div>

      <div className="rounded-xl bg-white p-6 shadow-sm ring-1 ring-slate-200">
        <h3 className="mb-4 text-sm font-semibold text-slate-900">Lines</h3>
        <table className="w-full text-left text-sm">
          <thead>
            <tr className="border-b border-slate-200 text-slate-500">
              <th className="pb-2 pr-4 font-medium">Product</th>
              <th className="pb-2 pr-4 font-medium">Unit</th>
              <th className="pb-2 pr-4 text-right font-medium">Quantity</th>
              <th className="pb-2 text-right font-medium">Unit price</th>
            </tr>
          </thead>
          <tbody>
            {order.lines?.map((line) => (
              <tr key={line.salesOrderLineId} className="border-b border-slate-100 last:border-0">
                <td className="py-2 pr-4 text-slate-700">{line.product?.name}</td>
                <td className="py-2 pr-4 text-slate-700">{line.unitOfMeasure?.code}</td>
                <td className="py-2 pr-4 text-right text-slate-700">{line.quantity}</td>
                <td className="py-2 text-right text-slate-700">{line.unitPrice}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      <div className="rounded-xl bg-white p-6 shadow-sm ring-1 ring-slate-200">
        <h3 className="mb-4 text-sm font-semibold text-slate-900">Actions</h3>
        {order.status === SalesOrderStatus.Draft && (
          <div className="flex gap-3">
            <button
              onClick={() => confirmMutation.mutate({ id: orderId })}
              disabled={isPending}
              className="rounded-md bg-indigo-600 px-4 py-2 text-sm font-medium text-white transition hover:bg-indigo-700 disabled:cursor-not-allowed disabled:opacity-60"
            >
              Confirm
            </button>
            <button
              onClick={() => cancelMutation.mutate({ id: orderId })}
              disabled={isPending}
              className="rounded-md border border-slate-300 px-4 py-2 text-sm font-medium text-slate-700 transition hover:bg-slate-100 disabled:cursor-not-allowed disabled:opacity-60"
            >
              Cancel
            </button>
          </div>
        )}
        {order.status === SalesOrderStatus.Draft && (
          <p className="mt-3 text-xs text-slate-400">
            Confirming deducts stock immediately, so a confirmed order can no longer be cancelled.
          </p>
        )}
        {order.status !== SalesOrderStatus.Draft && (
          <p className="text-sm text-slate-400">This order is finalized - no further actions available.</p>
        )}
        {order.status === SalesOrderStatus.Draft && confirmMutation.isError && (
          <p className="mt-3 text-sm text-red-600">{errorMessage(confirmMutation.error)}</p>
        )}
        {order.status === SalesOrderStatus.Draft && cancelMutation.isError && (
          <p className="mt-3 text-sm text-red-600">{errorMessage(cancelMutation.error)}</p>
        )}
      </div>
    </div>
  );
}
