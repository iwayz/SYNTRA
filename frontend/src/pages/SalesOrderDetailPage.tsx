import { useQueryClient } from "@tanstack/react-query";
import { Link, useParams } from "react-router-dom";
import { errorMessage } from "../api/errors";
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
import {
  getGetSalesSalesOrdersOrderIdReturnableQueryKey,
  useGetSalesSalesOrdersOrderIdReturnable,
  usePostSalesSalesOrdersOrderIdReturns,
} from "../api/generated/sales-returns/sales-returns";
import { SalesOrderStatus } from "../api/generated/syntraApiV1.schemas";
import { ReturnForm } from "../components/ReturnForm";
import { StatusBadge } from "../components/StatusBadge";

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

  const confirmed = orderQuery.data?.status === SalesOrderStatus.Confirmed;
  const returnableQuery = useGetSalesSalesOrdersOrderIdReturnable(orderId, { query: { enabled: confirmed } });

  const returnMutation = usePostSalesSalesOrdersOrderIdReturns({
    mutation: {
      onSuccess: () => {
        invalidateOrder();
        queryClient.invalidateQueries({ queryKey: getGetSalesSalesOrdersOrderIdReturnableQueryKey(orderId) });
        queryClient.invalidateQueries({ queryKey: getGetInventoryStockMovementsQueryKey() });
        queryClient.invalidateQueries({ queryKey: getGetInventoryStockBalanceQueryKey() });
      },
    },
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
              <th className="pb-2 pr-4 text-right font-medium">Returned</th>
              <th className="pb-2 text-right font-medium">Unit price</th>
            </tr>
          </thead>
          <tbody>
            {order.lines?.map((line) => (
              <tr key={line.salesOrderLineId} className="border-b border-slate-100 last:border-0">
                <td className="py-2 pr-4 text-slate-700">{line.product?.name}</td>
                <td className="py-2 pr-4 text-slate-700">{line.unitOfMeasure?.code}</td>
                <td className="py-2 pr-4 text-right text-slate-700">{line.quantity}</td>
                <td className="py-2 pr-4 text-right text-slate-700">{line.quantityReturned}</td>
                <td className="py-2 text-right text-slate-700">{line.unitPrice}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {(order.returns ?? []).length > 0 && (
        <div className="rounded-xl bg-white p-6 shadow-sm ring-1 ring-slate-200">
          <h3 className="mb-4 text-sm font-semibold text-slate-900">Returns</h3>
          <div className="space-y-4">
            {[...(order.returns ?? [])]
              .sort((a, b) => Number(b.salesReturnId) - Number(a.salesReturnId))
              .map((salesReturn) => (
                <div key={salesReturn.salesReturnId} className="text-sm">
                  <p className="font-medium text-slate-900">
                    Return #{salesReturn.salesReturnId}{" "}
                    <span className="font-normal text-slate-500">
                      · {salesReturn.returnDate && new Date(salesReturn.returnDate).toLocaleString()} · {salesReturn.reason}
                    </span>
                  </p>
                  <ul className="mt-1 space-y-0.5 text-slate-700">
                    {salesReturn.lines?.map((returnLine) => {
                      const orderLine = order.lines?.find((l) => l.salesOrderLineId === returnLine.salesOrderLineId);
                      return (
                        <li key={returnLine.salesReturnLineId}>
                          {returnLine.quantity} {orderLine?.unitOfMeasure?.code} × {orderLine?.product?.name}
                          {returnLine.productBatch && (
                            <span className="text-slate-500"> · batch {returnLine.productBatch.batchNumber}</span>
                          )}
                          <span
                            className={returnLine.disposition === "WriteOff" ? "text-amber-600" : "text-emerald-600"}
                          >
                            {" "}
                            · {returnLine.disposition === "WriteOff" ? "damaged, written off" : "back in stock"}
                          </span>
                        </li>
                      );
                    })}
                  </ul>
                </div>
              ))}
          </div>
        </div>
      )}

      {confirmed && (
        <div className="rounded-xl bg-white p-6 shadow-sm ring-1 ring-slate-200">
          <h3 className="mb-4 text-sm font-semibold text-slate-900">Return goods</h3>
          <ReturnForm
            lines={returnableQuery.data ?? []}
            withDisposition
            isPending={returnMutation.isPending}
            submitLabel="Record return"
            intro="Goods a customer brings back. Good items go back into this order's warehouse; damaged ones are recorded but never enter stock. A batch-tracked item must go back into a batch this order sold from."
            onSubmit={(entries, reason) =>
              returnMutation.mutateAsync({
                orderId,
                data: {
                  reason,
                  returnDate: null,
                  lines: entries.map((entry) => ({
                    salesOrderLineId: entry.lineId,
                    quantity: entry.quantity,
                    batchNumber: entry.batchNumber,
                    disposition: entry.disposition,
                  })),
                },
              })
            }
          />
          {returnMutation.isError && <p className="mt-3 text-sm text-red-600">{errorMessage(returnMutation.error)}</p>}
        </div>
      )}

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
