import { useQueryClient } from "@tanstack/react-query";
import { useState, type FormEvent } from "react";
import { Link, useParams } from "react-router-dom";
import { errorMessage } from "../api/errors";
import { getGetInventoryBatchesQueryKey } from "../api/generated/batches/batches";
import {
  getGetPurchasingPurchaseOrdersIdQueryKey,
  useGetPurchasingPurchaseOrdersId,
  usePostPurchasingPurchaseOrdersIdCancel,
  usePostPurchasingPurchaseOrdersIdClose,
  usePostPurchasingPurchaseOrdersIdConfirm,
  usePostPurchasingPurchaseOrdersIdGoodsReceipts,
  usePostPurchasingPurchaseOrdersIdReceive,
} from "../api/generated/purchase-orders/purchase-orders";
import { getGetInventoryStockMovementsQueryKey } from "../api/generated/stock/stock";
import { getGetInventoryStockBalanceQueryKey } from "../api/generated/stock/stock";
import { StatusBadge } from "../components/StatusBadge";
import { BatchTracking, PurchaseOrderStatus } from "../api/generated/syntraApiV1.schemas";
import { isTracked } from "../lib/batchTracking";

const inputClasses =
  "rounded-md border border-slate-300 px-2 py-1.5 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500";

export function PurchaseOrderDetailPage() {
  const { id } = useParams<{ id: string }>();
  const orderId = Number(id);
  const queryClient = useQueryClient();

  const orderQuery = useGetPurchasingPurchaseOrdersId(orderId);

  // What the user typed, per receipt entry. An entry is "<line id>:<n>"; entry 0
  // is the line itself and further entries (n > 0) are extra batches of the same
  // line in the same delivery. Entry 0 with nothing typed falls back to the
  // line's outstanding quantity, so the form starts out as "everything still due
  // arrived", and clearing it after a save just re-derives from the new amounts.
  const [quantities, setQuantities] = useState<Record<string, string>>({});
  const [entryNotes, setEntryNotes] = useState<Record<string, string>>({});
  const [batchNumbers, setBatchNumbers] = useState<Record<string, string>>({});
  const [expiries, setExpiries] = useState<Record<string, string>>({});
  const [extraEntries, setExtraEntries] = useState<Record<string, number>>({});
  const [receiptNotes, setReceiptNotes] = useState("");

  function invalidateOrder() {
    queryClient.invalidateQueries({ queryKey: getGetPurchasingPurchaseOrdersIdQueryKey(orderId) });
  }

  function invalidateStock() {
    queryClient.invalidateQueries({ queryKey: getGetInventoryStockMovementsQueryKey() });
    queryClient.invalidateQueries({ queryKey: getGetInventoryStockBalanceQueryKey() });
    queryClient.invalidateQueries({ queryKey: getGetInventoryBatchesQueryKey() });
  }

  const confirmMutation = usePostPurchasingPurchaseOrdersIdConfirm({
    mutation: { onSuccess: invalidateOrder },
  });

  const receiveAllMutation = usePostPurchasingPurchaseOrdersIdReceive({
    mutation: {
      onSuccess: () => {
        invalidateOrder();
        invalidateStock();
      },
    },
  });

  const receiptMutation = usePostPurchasingPurchaseOrdersIdGoodsReceipts({
    mutation: {
      onSuccess: () => {
        setQuantities({});
        setEntryNotes({});
        setBatchNumbers({});
        setExpiries({});
        setExtraEntries({});
        setReceiptNotes("");
        invalidateOrder();
        invalidateStock();
      },
    },
  });

  const closeMutation = usePostPurchasingPurchaseOrdersIdClose({
    mutation: { onSuccess: invalidateOrder },
  });

  const cancelMutation = usePostPurchasingPurchaseOrdersIdCancel({
    mutation: { onSuccess: invalidateOrder },
  });

  const order = orderQuery.data;

  if (!order) {
    return <p className="text-slate-500">Loading...</p>;
  }

  const isPending =
    confirmMutation.isPending ||
    receiveAllMutation.isPending ||
    receiptMutation.isPending ||
    closeMutation.isPending ||
    cancelMutation.isPending;

  const lines = order.lines ?? [];
  type Line = (typeof lines)[number];

  const outstandingOf = (line: Line) => Number(line.quantity ?? 0) - Number(line.quantityReceived ?? 0);
  const canReceive =
    order.status === PurchaseOrderStatus.Confirmed || order.status === PurchaseOrderStatus.PartiallyReceived;
  const hasReceipts = (order.goodsReceipts ?? []).length > 0;
  const anyTracked = lines.some((line) => isTracked(line.product?.batchTracking));

  const entryKeys = (line: Line) =>
    Array.from({ length: 1 + (extraEntries[String(line.purchaseOrderLineId)] ?? 0) }, (_, n) => `${line.purchaseOrderLineId}:${n}`);

  function quantityFor(line: Line, key: string) {
    // Entry 0 defaults to what is still outstanding; extra entries start empty.
    return quantities[key] ?? (key.endsWith(":0") ? String(outstandingOf(line)) : "0");
  }

  const receiptLines = lines.flatMap((line) =>
    entryKeys(line)
      .map((key) => ({
        purchaseOrderLineId: Number(line.purchaseOrderLineId),
        quantity: Number(quantityFor(line, key)),
        notes: entryNotes[key]?.trim() || null,
        batchNumber: isTracked(line.product?.batchTracking) ? batchNumbers[key]?.trim() || null : null,
        expiryDate:
          line.product?.batchTracking === BatchTracking.BatchAndExpiry && expiries[key] ? expiries[key] : null,
      }))
      .filter((entry) => entry.quantity > 0),
  );

  function handleReceipt(event: FormEvent) {
    event.preventDefault();
    receiptMutation.mutate({
      id: orderId,
      data: { receiptDate: null, notes: receiptNotes.trim() || null, lines: receiptLines },
    });
  }

  const mutationError =
    confirmMutation.error ?? receiveAllMutation.error ?? receiptMutation.error ?? closeMutation.error ?? cancelMutation.error;

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
              <th className="pb-2 pr-4 text-right font-medium">Ordered</th>
              <th className="pb-2 pr-4 text-right font-medium">Received</th>
              <th className="pb-2 pr-4 text-right font-medium">Outstanding</th>
              <th className="pb-2 text-right font-medium">Unit price</th>
            </tr>
          </thead>
          <tbody>
            {lines.map((line) => (
              <tr key={line.purchaseOrderLineId} className="border-b border-slate-100 last:border-0">
                <td className="py-2 pr-4 text-slate-700">{line.product?.name}</td>
                <td className="py-2 pr-4 text-slate-700">{line.unitOfMeasure?.code}</td>
                <td className="py-2 pr-4 text-right text-slate-700">{line.quantity}</td>
                <td className="py-2 pr-4 text-right text-slate-700">{line.quantityReceived}</td>
                <td
                  className={`py-2 pr-4 text-right font-medium ${
                    outstandingOf(line) > 0 && order.status === PurchaseOrderStatus.Received
                      ? "text-amber-600"
                      : "text-slate-700"
                  }`}
                >
                  {outstandingOf(line)}
                </td>
                <td className="py-2 text-right text-slate-700">{line.unitPrice}</td>
              </tr>
            ))}
          </tbody>
        </table>
        {order.status === PurchaseOrderStatus.Received && lines.some((line) => outstandingOf(line) > 0) && (
          <p className="mt-3 text-xs text-amber-600">
            This order was closed with goods still outstanding (highlighted above) - the supplier did not deliver them.
          </p>
        )}
      </div>

      {hasReceipts && (
        <div className="rounded-xl bg-white p-6 shadow-sm ring-1 ring-slate-200">
          <h3 className="mb-4 text-sm font-semibold text-slate-900">Goods receipts</h3>
          <div className="space-y-4">
            {[...(order.goodsReceipts ?? [])]
              .sort((a, b) => Number(b.goodsReceiptId) - Number(a.goodsReceiptId))
              .map((receipt) => (
                <div key={receipt.goodsReceiptId} className="text-sm">
                  <p className="font-medium text-slate-900">
                    Receipt #{receipt.goodsReceiptId}{" "}
                    <span className="font-normal text-slate-500">
                      · {receipt.receiptDate && new Date(receipt.receiptDate).toLocaleString()}
                    </span>
                  </p>
                  {receipt.notes && <p className="text-slate-500">{receipt.notes}</p>}
                  <ul className="mt-1 space-y-0.5 text-slate-700">
                    {receipt.lines?.map((receiptLine) => {
                      const orderLine = lines.find((l) => l.purchaseOrderLineId === receiptLine.purchaseOrderLineId);
                      return (
                        <li key={receiptLine.goodsReceiptLineId}>
                          {receiptLine.quantity} {orderLine?.unitOfMeasure?.code} × {orderLine?.product?.name}
                          {receiptLine.productBatch && (
                            <span className="text-slate-500">
                              {" "}
                              · batch {receiptLine.productBatch.batchNumber}
                              {receiptLine.productBatch.expiryDate && ` (expires ${receiptLine.productBatch.expiryDate})`}
                            </span>
                          )}
                          {receiptLine.notes && <span className="text-slate-500"> - {receiptLine.notes}</span>}
                        </li>
                      );
                    })}
                  </ul>
                </div>
              ))}
          </div>
        </div>
      )}

      {canReceive && (
        <div className="rounded-xl bg-white p-6 shadow-sm ring-1 ring-slate-200">
          <h3 className="mb-1 text-sm font-semibold text-slate-900">Receive goods</h3>
          <p className="mb-4 text-xs text-slate-400">
            Enter what actually arrived in this delivery. Leave a line at 0 to skip it; add a note for anything damaged
            or short.
            {anyTracked && " Batch-tracked products need a batch number (and an expiry date for a new batch where required)."}
          </p>
          <form onSubmit={handleReceipt} className="space-y-4">
            <table className="w-full text-left text-sm">
              <thead>
                <tr className="text-slate-500">
                  <th className="pb-2 pr-4 font-medium">Product</th>
                  <th className="pb-2 pr-4 text-right font-medium">Outstanding</th>
                  <th className="pb-2 pr-4 font-medium">Receiving now</th>
                  {anyTracked && <th className="pb-2 pr-4 font-medium">Batch</th>}
                  {anyTracked && <th className="pb-2 pr-4 font-medium">Expiry</th>}
                  <th className="pb-2 font-medium">Note</th>
                </tr>
              </thead>
              <tbody>
                {lines.flatMap((line) => {
                  const mode = line.product?.batchTracking;
                  const tracked = isTracked(mode);

                  return entryKeys(line).map((key, n) => (
                    <tr key={key}>
                      <td className="py-1 pr-4 text-slate-700">
                        {n === 0 ? (
                          <>
                            {line.product?.name} <span className="text-slate-400">({line.unitOfMeasure?.code})</span>
                          </>
                        ) : (
                          <span className="pl-4 text-slate-400">+ another batch</span>
                        )}
                      </td>
                      <td className="py-1 pr-4 text-right text-slate-700">{n === 0 ? outstandingOf(line) : ""}</td>
                      <td className="py-1 pr-4">
                        <input
                          type="number"
                          step="any"
                          min="0"
                          max={outstandingOf(line)}
                          value={quantityFor(line, key)}
                          onChange={(e) => {
                            receiptMutation.reset();
                            setQuantities({ ...quantities, [key]: e.target.value });
                          }}
                          className={`${inputClasses} w-28`}
                        />
                      </td>
                      {anyTracked && (
                        <td className="py-1 pr-4">
                          {tracked && (
                            <input
                              value={batchNumbers[key] ?? ""}
                              onChange={(e) => {
                                receiptMutation.reset();
                                setBatchNumbers({ ...batchNumbers, [key]: e.target.value });
                              }}
                              maxLength={50}
                              placeholder="Batch no."
                              className={`${inputClasses} w-32`}
                            />
                          )}
                        </td>
                      )}
                      {anyTracked && (
                        <td className="py-1 pr-4">
                          {mode === BatchTracking.BatchAndExpiry && (
                            <input
                              type="date"
                              value={expiries[key] ?? ""}
                              onChange={(e) => setExpiries({ ...expiries, [key]: e.target.value })}
                              className={inputClasses}
                            />
                          )}
                        </td>
                      )}
                      <td className="py-1">
                        <input
                          value={entryNotes[key] ?? ""}
                          onChange={(e) => setEntryNotes({ ...entryNotes, [key]: e.target.value })}
                          maxLength={500}
                          className={`${inputClasses} w-56`}
                        />
                        {tracked && n === entryKeys(line).length - 1 && (
                          <button
                            type="button"
                            onClick={() =>
                              setExtraEntries({
                                ...extraEntries,
                                [String(line.purchaseOrderLineId)]: (extraEntries[String(line.purchaseOrderLineId)] ?? 0) + 1,
                              })
                            }
                            className="ml-3 text-xs font-medium text-indigo-600 hover:underline"
                          >
                            Split into another batch
                          </button>
                        )}
                      </td>
                    </tr>
                  ));
                })}
              </tbody>
            </table>
            <div>
              <label className="mb-1 block text-sm font-medium text-slate-700">Receipt note (optional)</label>
              <input
                value={receiptNotes}
                onChange={(e) => setReceiptNotes(e.target.value)}
                maxLength={500}
                className={`${inputClasses} w-96`}
              />
            </div>
            <button
              type="submit"
              disabled={isPending || receiptLines.length === 0}
              className="rounded-md bg-emerald-600 px-4 py-2 text-sm font-medium text-white transition hover:bg-emerald-700 disabled:cursor-not-allowed disabled:opacity-60"
            >
              Record receipt
            </button>
          </form>
        </div>
      )}

      <div className="rounded-xl bg-white p-6 shadow-sm ring-1 ring-slate-200">
        <h3 className="mb-4 text-sm font-semibold text-slate-900">Actions</h3>
        <div className="flex flex-wrap gap-3">
          {order.status === PurchaseOrderStatus.Draft && (
            <button
              onClick={() => confirmMutation.mutate({ id: orderId })}
              disabled={isPending}
              className="rounded-md bg-indigo-600 px-4 py-2 text-sm font-medium text-white transition hover:bg-indigo-700 disabled:cursor-not-allowed disabled:opacity-60"
            >
              Confirm
            </button>
          )}
          {canReceive && !anyTracked && (
            <button
              onClick={() => receiveAllMutation.mutate({ id: orderId })}
              disabled={isPending}
              className="rounded-md border border-emerald-600 px-4 py-2 text-sm font-medium text-emerald-700 transition hover:bg-emerald-50 disabled:cursor-not-allowed disabled:opacity-60"
            >
              Receive all outstanding
            </button>
          )}
          {order.status === PurchaseOrderStatus.PartiallyReceived && (
            <button
              onClick={() => closeMutation.mutate({ id: orderId })}
              disabled={isPending}
              className="rounded-md border border-slate-300 px-4 py-2 text-sm font-medium text-slate-700 transition hover:bg-slate-100 disabled:cursor-not-allowed disabled:opacity-60"
            >
              Close order (no more goods coming)
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
        {order.status === PurchaseOrderStatus.PartiallyReceived && (
          <p className="mt-3 text-xs text-slate-400">
            Goods have arrived, so this order can no longer be cancelled. Close it if the rest will not be delivered.
          </p>
        )}
        {mutationError != null && <p className="mt-3 text-sm text-red-600">{errorMessage(mutationError)}</p>}
      </div>
    </div>
  );
}
