import { useQueryClient } from "@tanstack/react-query";
import { useState, type FormEvent } from "react";
import { Link, useParams } from "react-router-dom";
import { errorMessage } from "../api/errors";
import { getGetInventoryBatchesQueryKey } from "../api/generated/batches/batches";
import {
  getGetInventoryStockInTransitQueryKey,
  getGetInventoryTransfersIdQueryKey,
  getGetInventoryTransfersQueryKey,
  useGetInventoryTransfersId,
  usePostInventoryTransfersIdCancel,
  usePostInventoryTransfersIdClose,
  usePostInventoryTransfersIdDispatch,
  usePostInventoryTransfersIdReceive,
} from "../api/generated/stock-transfers/stock-transfers";
import { getGetInventoryStockBalanceQueryKey, getGetInventoryStockMovementsQueryKey } from "../api/generated/stock/stock";
import { StockTransferStatus, type Warehouse } from "../api/generated/syntraApiV1.schemas";
import { useCurrentUser } from "../auth/useCurrentUser";
import { ExpiryBadge } from "../components/ExpiryBadge";
import { StatusBadge } from "../components/StatusBadge";
import { isTracked } from "../lib/batchTracking";

const buttonBase = "rounded-md px-4 py-2 text-sm font-medium transition disabled:cursor-not-allowed disabled:opacity-60";

export function TransferDetailPage() {
  const { id } = useParams<{ id: string }>();
  const transferId = Number(id);
  const queryClient = useQueryClient();
  const { isAdmin, branchIds } = useCurrentUser();

  const transferQuery = useGetInventoryTransfersId(transferId);

  // What the user typed to receive, by allocation id; untouched rows default to
  // what is still outstanding.
  const [received, setReceived] = useState<Record<string, string>>({});

  function refresh() {
    queryClient.invalidateQueries({ queryKey: getGetInventoryTransfersIdQueryKey(transferId) });
    queryClient.invalidateQueries({ queryKey: getGetInventoryTransfersQueryKey() });
    queryClient.invalidateQueries({ queryKey: getGetInventoryStockInTransitQueryKey() });
    queryClient.invalidateQueries({ queryKey: getGetInventoryStockBalanceQueryKey() });
    queryClient.invalidateQueries({ queryKey: getGetInventoryStockMovementsQueryKey() });
    queryClient.invalidateQueries({ queryKey: getGetInventoryBatchesQueryKey() });
  }

  const dispatchMutation = usePostInventoryTransfersIdDispatch({ mutation: { onSuccess: refresh } });
  const cancelMutation = usePostInventoryTransfersIdCancel({ mutation: { onSuccess: refresh } });
  const closeMutation = usePostInventoryTransfersIdClose({ mutation: { onSuccess: refresh } });
  const receiveMutation = usePostInventoryTransfersIdReceive({
    mutation: {
      onSuccess: () => {
        setReceived({});
        refresh();
      },
    },
  });

  const transfer = transferQuery.data;

  if (!transfer) {
    return <p className="text-slate-500">Loading...</p>;
  }

  const status = transfer.status;
  const lines = transfer.lines ?? [];
  const allocations = lines.flatMap((line) => line.allocations ?? []);
  const outstandingOf = (a: (typeof allocations)[number]) => Number(a.quantity ?? 0) - Number(a.quantityReceived ?? 0);

  // Both ends can see a transfer, but each end only acts on its own side.
  const canSource = isAdmin || branchIds.includes(Number(transfer.fromWarehouse?.branchId));
  const canDestination = isAdmin || branchIds.includes(Number(transfer.toWarehouse?.branchId));

  const nothingReceived = allocations.every((a) => Number(a.quantityReceived ?? 0) === 0);
  const canReceive =
    canDestination && (status === StockTransferStatus.InTransit || status === StockTransferStatus.PartiallyReceived);
  const canCancel =
    canSource && (status === StockTransferStatus.Draft || (status === StockTransferStatus.InTransit && nothingReceived));

  const isPending =
    dispatchMutation.isPending || cancelMutation.isPending || closeMutation.isPending || receiveMutation.isPending;

  const lost =
    status === StockTransferStatus.Received
      ? allocations.reduce((sum, a) => sum + outstandingOf(a), 0)
      : 0;

  const receiptLines = allocations
    .map((a) => ({
      stockTransferAllocationId: Number(a.stockTransferAllocationId),
      quantity: Number(received[String(a.stockTransferAllocationId)] ?? outstandingOf(a)),
    }))
    .filter((line) => line.quantity > 0);

  function handleReceive(event: FormEvent) {
    event.preventDefault();
    receiveMutation.mutate({ id: transferId, data: { lines: receiptLines } });
  }

  const mutationError =
    dispatchMutation.error ?? receiveMutation.error ?? closeMutation.error ?? cancelMutation.error;

  const nameOf = (warehouse?: Warehouse) =>
    `${warehouse?.name} · ${warehouse?.branch?.name}`;

  return (
    <div className="space-y-6">
      <div>
        <Link to="/transfers" className="text-sm text-indigo-600 hover:underline">
          ← Stock Transfers
        </Link>
        <div className="mt-1 flex items-center gap-3">
          <h2 className="text-lg font-semibold text-slate-900">
            Transfer <span className="font-normal text-slate-400">#{transfer.stockTransferId}</span>
          </h2>
          <StatusBadge status={status} />
        </div>
        <p className="text-sm text-slate-500">
          {nameOf(transfer.fromWarehouse)} <span className="text-slate-400">→</span> {nameOf(transfer.toWarehouse)}
        </p>
        {transfer.notes && <p className="text-sm text-slate-500">{transfer.notes}</p>}
      </div>

      <div className="rounded-xl bg-white p-6 shadow-sm ring-1 ring-slate-200">
        <h3 className="mb-4 text-sm font-semibold text-slate-900">Items</h3>
        <table className="w-full text-left text-sm">
          <thead>
            <tr className="border-b border-slate-200 text-slate-500">
              <th className="pb-2 pr-4 font-medium">Product</th>
              <th className="pb-2 pr-4 font-medium">Batch</th>
              <th className="pb-2 pr-4 font-medium">Expiry</th>
              <th className="pb-2 pr-4 text-right font-medium">{status === StockTransferStatus.Draft ? "Quantity" : "Sent"}</th>
              <th className="pb-2 pr-4 text-right font-medium">Received</th>
              <th className="pb-2 text-right font-medium">Outstanding</th>
            </tr>
          </thead>
          <tbody>
            {lines.flatMap((line) => {
              const rows = line.allocations ?? [];

              // Not dispatched yet: one row per line, showing how the batch will be chosen.
              if (rows.length === 0) {
                return (
                  <tr key={`line-${line.stockTransferLineId}`} className="border-b border-slate-100 last:border-0">
                    <td className="py-2 pr-4 text-slate-700">{line.product?.name}</td>
                    <td className="py-2 pr-4 text-slate-700">
                      {line.pinnedBatch?.batchNumber ??
                        (isTracked(line.product?.batchTracking) ? (
                          <span className="text-slate-400">earliest expiry first</span>
                        ) : (
                          <span className="text-slate-400">-</span>
                        ))}
                    </td>
                    <td className="py-2 pr-4">
                      <ExpiryBadge expiry={line.pinnedBatch?.expiryDate} />
                    </td>
                    <td className="py-2 pr-4 text-right text-slate-700">{line.quantity}</td>
                    <td className="py-2 pr-4 text-right text-slate-400">-</td>
                    <td className="py-2 text-right text-slate-400">-</td>
                  </tr>
                );
              }

              return rows.map((allocation) => (
                <tr key={allocation.stockTransferAllocationId} className="border-b border-slate-100 last:border-0">
                  <td className="py-2 pr-4 text-slate-700">{line.product?.name}</td>
                  <td className="py-2 pr-4 text-slate-700">
                    {allocation.batch?.batchNumber ?? <span className="text-slate-400">-</span>}
                  </td>
                  <td className="py-2 pr-4">
                    <ExpiryBadge expiry={allocation.batch?.expiryDate} />
                  </td>
                  <td className="py-2 pr-4 text-right text-slate-700">{allocation.quantity}</td>
                  <td className="py-2 pr-4 text-right text-slate-700">{allocation.quantityReceived}</td>
                  <td
                    className={`py-2 text-right font-medium ${
                      outstandingOf(allocation) > 0 && status === StockTransferStatus.Received
                        ? "text-amber-600"
                        : "text-slate-700"
                    }`}
                  >
                    {outstandingOf(allocation)}
                  </td>
                </tr>
              ));
            })}
          </tbody>
        </table>
        {lost > 0 && (
          <p className="mt-3 text-xs text-amber-600">
            {lost} unit{lost === 1 ? "" : "s"} never arrived and {lost === 1 ? "was" : "were"} written off as lost in
            transit when this transfer was closed.
          </p>
        )}
        {status === StockTransferStatus.InTransit || status === StockTransferStatus.PartiallyReceived ? (
          <p className="mt-3 text-xs text-slate-400">
            Stock that is on its way is in neither warehouse's balance; it shows under "In transit" on Stock Balance.
          </p>
        ) : null}
      </div>

      {canReceive && (
        <div className="rounded-xl bg-white p-6 shadow-sm ring-1 ring-slate-200">
          <h3 className="mb-1 text-sm font-semibold text-slate-900">Receive stock</h3>
          <p className="mb-4 text-xs text-slate-400">
            Enter what actually arrived. You can receive in several steps; whatever never arrives can be written off
            with Close.
          </p>
          <form onSubmit={handleReceive} className="space-y-4">
            <table className="w-full text-left text-sm">
              <thead>
                <tr className="text-slate-500">
                  <th className="pb-2 pr-4 font-medium">Product</th>
                  <th className="pb-2 pr-4 font-medium">Batch</th>
                  <th className="pb-2 pr-4 text-right font-medium">Outstanding</th>
                  <th className="pb-2 font-medium">Receiving now</th>
                </tr>
              </thead>
              <tbody>
                {lines.flatMap((line) =>
                  (line.allocations ?? [])
                    .filter((allocation) => outstandingOf(allocation) > 0)
                    .map((allocation) => {
                      const key = String(allocation.stockTransferAllocationId);
                      return (
                        <tr key={key}>
                          <td className="py-1 pr-4 text-slate-700">{line.product?.name}</td>
                          <td className="py-1 pr-4 text-slate-700">{allocation.batch?.batchNumber ?? "-"}</td>
                          <td className="py-1 pr-4 text-right text-slate-700">{outstandingOf(allocation)}</td>
                          <td className="py-1">
                            <input
                              type="number"
                              step="any"
                              min="0"
                              max={outstandingOf(allocation)}
                              value={received[key] ?? String(outstandingOf(allocation))}
                              onChange={(e) => {
                                receiveMutation.reset();
                                setReceived({ ...received, [key]: e.target.value });
                              }}
                              className="w-28 rounded-md border border-slate-300 px-2 py-1.5 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
                            />
                          </td>
                        </tr>
                      );
                    }),
                )}
              </tbody>
            </table>
            <button
              type="submit"
              disabled={isPending || receiptLines.length === 0}
              className={`${buttonBase} bg-emerald-600 text-white hover:bg-emerald-700`}
            >
              Record receipt
            </button>
          </form>
        </div>
      )}

      <div className="rounded-xl bg-white p-6 shadow-sm ring-1 ring-slate-200">
        <h3 className="mb-4 text-sm font-semibold text-slate-900">Actions</h3>
        <div className="flex flex-wrap gap-3">
          {status === StockTransferStatus.Draft && canSource && (
            <button
              onClick={() => dispatchMutation.mutate({ id: transferId })}
              disabled={isPending}
              className={`${buttonBase} bg-indigo-600 text-white hover:bg-indigo-700`}
            >
              Dispatch
            </button>
          )}
          {status === StockTransferStatus.PartiallyReceived && canDestination && (
            <button
              onClick={() => closeMutation.mutate({ id: transferId })}
              disabled={isPending}
              className={`${buttonBase} border border-slate-300 text-slate-700 hover:bg-slate-100`}
            >
              Close (the rest is not coming)
            </button>
          )}
          {canCancel && (
            <button
              onClick={() => cancelMutation.mutate({ id: transferId })}
              disabled={isPending}
              className={`${buttonBase} border border-slate-300 text-slate-700 hover:bg-slate-100`}
            >
              {status === StockTransferStatus.Draft ? "Cancel" : "Recall (cancel and return to source)"}
            </button>
          )}
          {(status === StockTransferStatus.Received || status === StockTransferStatus.Cancelled) && (
            <p className="text-sm text-slate-400">This transfer is finalized - no further actions available.</p>
          )}
          {status === StockTransferStatus.Draft && !canSource && (
            <p className="text-sm text-slate-400">Only the source branch can dispatch this transfer.</p>
          )}
          {(status === StockTransferStatus.InTransit || status === StockTransferStatus.PartiallyReceived) &&
            !canDestination &&
            !canCancel && <p className="text-sm text-slate-400">Waiting for the destination branch to receive it.</p>}
        </div>
        {status === StockTransferStatus.PartiallyReceived && (
          <p className="mt-3 text-xs text-slate-400">
            Part of this transfer has arrived, so it can no longer be recalled. Close it if the rest will not arrive;
            the missing quantity is then written off as lost in transit.
          </p>
        )}
        {mutationError != null && <p className="mt-3 text-sm text-red-600">{errorMessage(mutationError)}</p>}
      </div>
    </div>
  );
}
