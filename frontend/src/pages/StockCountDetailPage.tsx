import { useQueryClient } from "@tanstack/react-query";
import { useState, type FormEvent } from "react";
import { Link, useParams } from "react-router-dom";
import { errorMessage } from "../api/errors";
import { getGetInventoryBatchesQueryKey, useGetInventoryBatches } from "../api/generated/batches/batches";
import { useGetInventoryProducts } from "../api/generated/products/products";
import {
  getGetInventoryStockCountsIdQueryKey,
  getGetInventoryStockCountsQueryKey,
  useGetInventoryStockCountsId,
  usePostInventoryStockCountsIdCancel,
  usePostInventoryStockCountsIdPost,
  usePostInventoryStockCountsIdRows,
  usePutInventoryStockCountsIdRowsRowId,
} from "../api/generated/stock-counts/stock-counts";
import { getGetInventoryStockBalanceQueryKey, getGetInventoryStockMovementsQueryKey } from "../api/generated/stock/stock";
import { StockCountStatus } from "../api/generated/syntraApiV1.schemas";
import { useCurrentUser } from "../auth/useCurrentUser";
import { ExpiryBadge } from "../components/ExpiryBadge";
import { StatusBadge } from "../components/StatusBadge";
import { isTracked } from "../lib/batchTracking";

const inputClasses =
  "rounded-md border border-slate-300 px-2 py-1.5 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500";

const signed = (n: number) => (n > 0 ? `+${n}` : String(n));

export function StockCountDetailPage() {
  const { id } = useParams<{ id: string }>();
  const countId = Number(id);
  const queryClient = useQueryClient();
  const { isAdmin } = useCurrentUser();

  const countQuery = useGetInventoryStockCountsId(countId);
  const productsQuery = useGetInventoryProducts();
  const batchesQuery = useGetInventoryBatches();

  // What has been typed per row but not saved yet.
  const [drafts, setDrafts] = useState<Record<string, string>>({});

  const [foundProductId, setFoundProductId] = useState("");
  const [foundBatch, setFoundBatch] = useState("");
  const [foundQuantity, setFoundQuantity] = useState("");

  function refresh() {
    queryClient.invalidateQueries({ queryKey: getGetInventoryStockCountsIdQueryKey(countId) });
    queryClient.invalidateQueries({ queryKey: getGetInventoryStockCountsQueryKey() });
  }

  const setCountMutation = usePutInventoryStockCountsIdRowsRowId({
    mutation: {
      onSuccess: (_, variables) => {
        setDrafts((current) => {
          const next = { ...current };
          delete next[String(variables.rowId)];
          return next;
        });
        refresh();
      },
    },
  });

  const addRowMutation = usePostInventoryStockCountsIdRows({
    mutation: {
      onSuccess: () => {
        setFoundProductId("");
        setFoundBatch("");
        setFoundQuantity("");
        refresh();
      },
    },
  });

  const postMutation = usePostInventoryStockCountsIdPost({
    mutation: {
      onSuccess: () => {
        refresh();
        queryClient.invalidateQueries({ queryKey: getGetInventoryStockBalanceQueryKey() });
        queryClient.invalidateQueries({ queryKey: getGetInventoryStockMovementsQueryKey() });
        queryClient.invalidateQueries({ queryKey: getGetInventoryBatchesQueryKey() });
      },
    },
  });

  const cancelMutation = usePostInventoryStockCountsIdCancel({ mutation: { onSuccess: refresh } });

  const detail = countQuery.data;

  if (!detail?.summary) {
    return <p className="text-slate-500">Loading...</p>;
  }

  const summary = detail.summary;
  const rows = detail.rows ?? [];
  const isOpen = summary.status === StockCountStatus.Open;
  const isPending =
    setCountMutation.isPending || addRowMutation.isPending || postMutation.isPending || cancelMutation.isPending;

  const counted = rows.filter((row) => row.countedQuantity != null);
  const stale = counted.filter((row) => row.isStale);
  const adjusting = counted.filter((row) => Number(row.variance ?? 0) !== 0);
  const net = adjusting.reduce((sum, row) => sum + Number(row.variance ?? 0), 0);
  const uncounted = rows.length - counted.length;

  const foundProduct = productsQuery.data?.find((product) => String(product.productId) === foundProductId);

  function handleAddFound(event: FormEvent) {
    event.preventDefault();
    addRowMutation.mutate({
      id: countId,
      data: {
        productId: Number(foundProductId),
        batchNumber: isTracked(foundProduct?.batchTracking) ? foundBatch.trim() : null,
        countedQuantity: Number(foundQuantity),
      },
    });
  }

  const mutationError = setCountMutation.error ?? addRowMutation.error ?? postMutation.error ?? cancelMutation.error;

  return (
    <div className="space-y-6">
      <div>
        <Link to="/stock-counts" className="text-sm text-indigo-600 hover:underline">
          ← Stock Counts
        </Link>
        <div className="mt-1 flex items-center gap-3">
          <h2 className="text-lg font-semibold text-slate-900">
            Count <span className="font-normal text-slate-400">#{summary.stockCountId}</span>
          </h2>
          <StatusBadge status={summary.status} />
        </div>
        <p className="text-sm text-slate-500">
          {summary.warehouseName} · {summary.branchName} · {summary.countedCount} of {summary.rowCount} counted
        </p>
      </div>

      <div className="rounded-xl bg-white p-6 shadow-sm ring-1 ring-slate-200">
        <table className="w-full text-left text-sm">
          <thead>
            <tr className="border-b border-slate-200 text-slate-500">
              <th className="pb-2 pr-4 font-medium">Product</th>
              <th className="pb-2 pr-4 font-medium">Batch</th>
              <th className="pb-2 pr-4 font-medium">Expiry</th>
              <th className="pb-2 pr-4 text-right font-medium">At start</th>
              <th className="pb-2 pr-4 text-right font-medium">In books now</th>
              <th className="pb-2 pr-4 font-medium">Counted</th>
              <th className="pb-2 pr-4 text-right font-medium">{isOpen ? "Difference" : "Applied"}</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            {rows.map((row) => {
              const key = String(row.stockCountLineId);
              const draft = drafts[key];
              const shown = draft ?? (row.countedQuantity == null ? "" : String(row.countedQuantity));
              const difference = isOpen ? row.variance : row.postedVariance;

              return (
                <tr
                  key={key}
                  className={`border-b border-slate-100 last:border-0 ${row.isStale ? "bg-amber-50" : ""}`}
                >
                  <td className="py-2 pr-4 text-slate-700">{row.productName}</td>
                  <td className="py-2 pr-4 text-slate-700">{row.batchNumber ?? <span className="text-slate-400">-</span>}</td>
                  <td className="py-2 pr-4">
                    <ExpiryBadge expiry={row.expiryDate} />
                  </td>
                  <td className="py-2 pr-4 text-right text-slate-500">{row.expectedQuantity}</td>
                  <td className="py-2 pr-4 text-right text-slate-700">{row.currentQuantity}</td>
                  <td className="py-2 pr-4">
                    {isOpen ? (
                      <input
                        type="number"
                        step="any"
                        min="0"
                        value={shown}
                        placeholder="not counted"
                        onChange={(e) => {
                          setCountMutation.reset();
                          setDrafts({ ...drafts, [key]: e.target.value });
                        }}
                        className={`${inputClasses} w-28`}
                      />
                    ) : (
                      <span className="text-slate-700">{row.countedQuantity ?? "-"}</span>
                    )}
                  </td>
                  <td
                    className={`py-2 pr-4 text-right font-medium ${
                      difference == null || Number(difference) === 0
                        ? "text-slate-400"
                        : Number(difference) > 0
                          ? "text-emerald-600"
                          : "text-red-600"
                    }`}
                  >
                    {difference == null ? "-" : signed(Number(difference))}
                  </td>
                  <td className="py-2 text-right whitespace-nowrap">
                    {isOpen && (
                      <>
                        {row.isStale && <span className="mr-3 text-xs font-medium text-amber-700">stock moved - recount</span>}
                        <button
                          onClick={() =>
                            setCountMutation.mutate({
                              id: countId,
                              rowId: Number(row.stockCountLineId),
                              data: { countedQuantity: Number(shown) },
                            })
                          }
                          disabled={isPending || shown === "" || (draft === undefined && !row.isStale)}
                          className="text-sm font-medium text-indigo-600 hover:underline disabled:cursor-not-allowed disabled:text-slate-300 disabled:no-underline"
                        >
                          {row.isStale ? "Count again" : "Save"}
                        </button>
                        {row.countedQuantity != null && (
                          <button
                            onClick={() =>
                              setCountMutation.mutate({
                                id: countId,
                                rowId: Number(row.stockCountLineId),
                                data: { countedQuantity: null },
                              })
                            }
                            disabled={isPending}
                            className="ml-3 text-sm text-slate-500 hover:underline disabled:opacity-60"
                          >
                            Clear
                          </button>
                        )}
                      </>
                    )}
                  </td>
                </tr>
              );
            })}
            {rows.length === 0 && (
              <tr>
                <td colSpan={8} className="py-4 text-slate-400">
                  This warehouse held no stock when the count started. Add anything you find below.
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>

      {isOpen && (
        <div className="rounded-xl bg-white p-6 shadow-sm ring-1 ring-slate-200">
          <h3 className="mb-1 text-sm font-semibold text-slate-900">Add something found</h3>
          <p className="mb-4 text-xs text-slate-400">
            For stock that is on the shelf but not on the sheet. A batch-tracked product needs an existing batch number;
            create a brand-new batch with a normal stock adjustment first.
          </p>
          <form onSubmit={handleAddFound} className="flex flex-wrap items-end gap-3">
            <div>
              <label className="mb-1 block text-sm font-medium text-slate-700">Product</label>
              <select
                value={foundProductId}
                onChange={(e) => {
                  setFoundProductId(e.target.value);
                  setFoundBatch("");
                  addRowMutation.reset();
                }}
                required
                className={inputClasses}
              >
                <option value="" disabled>
                  Select...
                </option>
                {productsQuery.data?.map((product) => (
                  <option key={product.productId} value={product.productId}>
                    {product.name}
                  </option>
                ))}
              </select>
            </div>
            {isTracked(foundProduct?.batchTracking) && (
              <div>
                <label className="mb-1 block text-sm font-medium text-slate-700">Batch</label>
                <input
                  value={foundBatch}
                  onChange={(e) => setFoundBatch(e.target.value)}
                  list="found-batches"
                  required
                  maxLength={50}
                  className={`${inputClasses} w-40`}
                />
                <datalist id="found-batches">
                  {batchesQuery.data
                    ?.filter((batch) => String(batch.productId) === foundProductId)
                    .map((batch) => (
                      <option key={batch.productBatchId} value={batch.batchNumber} />
                    ))}
                </datalist>
              </div>
            )}
            <div>
              <label className="mb-1 block text-sm font-medium text-slate-700">Counted</label>
              <input
                type="number"
                step="any"
                min="0"
                value={foundQuantity}
                onChange={(e) => setFoundQuantity(e.target.value)}
                required
                className={`${inputClasses} w-28`}
              />
            </div>
            <button
              type="submit"
              disabled={isPending}
              className="rounded-md border border-slate-300 px-4 py-2 text-sm font-medium text-slate-700 transition hover:bg-slate-100 disabled:opacity-60"
            >
              Add
            </button>
          </form>
        </div>
      )}

      <div className="rounded-xl bg-white p-6 shadow-sm ring-1 ring-slate-200">
        <h3 className="mb-4 text-sm font-semibold text-slate-900">Actions</h3>

        {isOpen && (
          <div className="space-y-3">
            <p className="text-sm text-slate-600">
              {counted.length} row{counted.length === 1 ? "" : "s"} counted, {adjusting.length} will change stock (net{" "}
              {signed(net)}).
              {uncounted > 0 && ` ${uncounted} uncounted row${uncounted === 1 ? "" : "s"} will be left unchanged.`}
            </p>
            {stale.length > 0 && (
              <p className="text-sm font-medium text-amber-700">
                {stale.length} row{stale.length === 1 ? "" : "s"} changed since counted - count{" "}
                {stale.length === 1 ? "it" : "them"} again before posting.
              </p>
            )}
            <div className="flex flex-wrap gap-3">
              {isAdmin ? (
                <button
                  onClick={() => postMutation.mutate({ id: countId })}
                  disabled={isPending || counted.length === 0 || stale.length > 0}
                  className="rounded-md bg-indigo-600 px-4 py-2 text-sm font-medium text-white transition hover:bg-indigo-700 disabled:cursor-not-allowed disabled:opacity-60"
                >
                  Post count
                </button>
              ) : (
                <p className="self-center text-sm text-slate-400">An admin reviews and posts the count.</p>
              )}
              <button
                onClick={() => cancelMutation.mutate({ id: countId })}
                disabled={isPending}
                className="rounded-md border border-slate-300 px-4 py-2 text-sm font-medium text-slate-700 transition hover:bg-slate-100 disabled:opacity-60"
              >
                Cancel count
              </button>
            </div>
          </div>
        )}

        {!isOpen && (
          <p className="text-sm text-slate-400">
            {summary.status === StockCountStatus.Posted
              ? `Posted ${summary.postedAt ? new Date(summary.postedAt).toLocaleString() : ""} - the differences above were applied to stock.`
              : "This count was cancelled; nothing changed in stock."}
          </p>
        )}

        {mutationError != null && <p className="mt-3 text-sm text-red-600">{errorMessage(mutationError)}</p>}
      </div>
    </div>
  );
}
