import { useQueryClient } from "@tanstack/react-query";
import { Link, useParams } from "react-router-dom";
import {
  getGetPurchasingPurchaseOrdersIdQueryKey,
  useGetPurchasingPurchaseOrdersId,
  usePostPurchasingPurchaseOrdersIdCancel,
  usePostPurchasingPurchaseOrdersIdConfirm,
  usePostPurchasingPurchaseOrdersIdReceive,
} from "../api/generated/purchase-orders/purchase-orders";
import { getGetInventoryStockMovementsQueryKey } from "../api/generated/stock/stock";
import { getGetInventoryStockBalanceQueryKey } from "../api/generated/stock/stock";
import { StatusBadge } from "../components/StatusBadge";
import { PurchaseOrderStatus } from "../api/generated/syntraApiV1.schemas";

export function PurchaseOrderDetailPage() {
  const { id } = useParams<{ id: string }>();
  const orderId = Number(id);
  const queryClient = useQueryClient();

  const orderQuery = useGetPurchasingPurchaseOrdersId(orderId);

  function invalidateOrder() {
    queryClient.invalidateQueries({ queryKey: getGetPurchasingPurchaseOrdersIdQueryKey(orderId) });
  }

  const confirmMutation = usePostPurchasingPurchaseOrdersIdConfirm({
    mutation: { onSuccess: invalidateOrder },
  });

  const receiveMutation = usePostPurchasingPurchaseOrdersIdReceive({
    mutation: {
      onSuccess: () => {
        invalidateOrder();
        queryClient.invalidateQueries({ queryKey: getGetInventoryStockMovementsQueryKey() });
        queryClient.invalidateQueries({ queryKey: getGetInventoryStockBalanceQueryKey() });
      },
    },
  });

  const cancelMutation = usePostPurchasingPurchaseOrdersIdCancel({
    mutation: { onSuccess: invalidateOrder },
  });

  const order = orderQuery.data;

  if (!order) {
    return <p className="text-slate-500">Loading...</p>;
  }

  const isPending = confirmMutation.isPending || receiveMutation.isPending || cancelMutation.isPending;

  return (
    <div className="space-y-6">
      <div>
        <Link to="/purchase-orders" className="text-sm text-indigo-600 hover:underline">
          ← Purchase Orders
        </Link>
        <div className="mt-1 flex items-center gap-3">
          <h2 className="text-lg font-semibold text-slate-900">
            {order.supplier?.name} <span className="font-normal text-slate-400">#{order.purchaseOrderId}</span>
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
              <tr key={line.purchaseOrderLineId} className="border-b border-slate-100 last:border-0">
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
        <div className="flex gap-3">
          {order.status === PurchaseOrderStatus.Draft && (
            <button
              onClick={() => confirmMutation.mutate({ id: orderId })}
              disabled={isPending}
              className="rounded-md bg-indigo-600 px-4 py-2 text-sm font-medium text-white transition hover:bg-indigo-700 disabled:cursor-not-allowed disabled:opacity-60"
            >
              Confirm
            </button>
          )}
          {order.status === PurchaseOrderStatus.Confirmed && (
            <button
              onClick={() => receiveMutation.mutate({ id: orderId })}
              disabled={isPending}
              className="rounded-md bg-emerald-600 px-4 py-2 text-sm font-medium text-white transition hover:bg-emerald-700 disabled:cursor-not-allowed disabled:opacity-60"
            >
              Receive
            </button>
          )}
          {(order.status === PurchaseOrderStatus.Draft || order.status === PurchaseOrderStatus.Confirmed) && (
            <button
              onClick={() => cancelMutation.mutate({ id: orderId })}
              disabled={isPending}
              className="rounded-md border border-slate-300 px-4 py-2 text-sm font-medium text-slate-700 transition hover:bg-slate-100 disabled:cursor-not-allowed disabled:opacity-60"
            >
              Cancel
            </button>
          )}
          {(order.status === PurchaseOrderStatus.Received || order.status === PurchaseOrderStatus.Cancelled) && (
            <p className="text-sm text-slate-400">This order is finalized - no further actions available.</p>
          )}
        </div>
      </div>
    </div>
  );
}
