import { useQueryClient } from "@tanstack/react-query";
import { useState, type FormEvent } from "react";
import { Link, useParams } from "react-router-dom";
import {
  getGetInventoryProductsIdQueryKey,
  useGetInventoryProductsId,
  usePostInventoryProductsIdUnitConversions,
  usePutInventoryProductsIdBatchTracking,
} from "../api/generated/products/products";
import { errorMessage } from "../api/errors";
import {
  getGetInventoryBatchesQueryKey,
  useGetInventoryBatches,
  usePutInventoryBatchesId,
} from "../api/generated/batches/batches";
import { BatchTracking } from "../api/generated/syntraApiV1.schemas";
import { useGetInventoryUnitsOfMeasure } from "../api/generated/unit-of-measures/unit-of-measures";
import { getGetInventoryStockBalanceQueryKey } from "../api/generated/stock/stock";
import { useCurrentUser } from "../auth/useCurrentUser";
import { ExpiryBadge } from "../components/ExpiryBadge";
import { isTracked, TRACKING_LABELS } from "../lib/batchTracking";

export function ProductDetailPage() {
  const { id } = useParams<{ id: string }>();
  const productId = Number(id);
  const queryClient = useQueryClient();

  const productQuery = useGetInventoryProductsId(productId);
  const unitsQuery = useGetInventoryUnitsOfMeasure();

  const [unitOfMeasureId, setUnitOfMeasureId] = useState("");
  const [conversionFactor, setConversionFactor] = useState("");

  const { isAdmin } = useCurrentUser();
  const batchesQuery = useGetInventoryBatches({ productId });
  const [newMode, setNewMode] = useState("");
  const [editingBatchId, setEditingBatchId] = useState<number | null>(null);
  const [editExpiry, setEditExpiry] = useState("");

  const trackingMutation = usePutInventoryProductsIdBatchTracking({
    mutation: {
      onSuccess: () => {
        setNewMode("");
        queryClient.invalidateQueries({ queryKey: getGetInventoryProductsIdQueryKey(productId) });
        queryClient.invalidateQueries({ queryKey: getGetInventoryBatchesQueryKey({ productId }) });
        queryClient.invalidateQueries({ queryKey: getGetInventoryStockBalanceQueryKey() });
      },
    },
  });

  const expiryMutation = usePutInventoryBatchesId({
    mutation: {
      onSuccess: () => {
        setEditingBatchId(null);
        queryClient.invalidateQueries({ queryKey: getGetInventoryBatchesQueryKey({ productId }) });
        queryClient.invalidateQueries({ queryKey: getGetInventoryStockBalanceQueryKey() });
      },
    },
  });

  const addConversionMutation = usePostInventoryProductsIdUnitConversions({
    mutation: {
      onSuccess: () => {
        setUnitOfMeasureId("");
        setConversionFactor("");
        queryClient.invalidateQueries({ queryKey: getGetInventoryProductsIdQueryKey(productId) });
      },
    },
  });

  function handleSubmit(event: FormEvent) {
    event.preventDefault();
    addConversionMutation.mutate({
      id: productId,
      data: { unitOfMeasureId: Number(unitOfMeasureId), conversionFactor: Number(conversionFactor) },
    });
  }

  const product = productQuery.data;

  if (!product) {
    return <p className="text-slate-500">Loading...</p>;
  }

  const otherUnits = unitsQuery.data?.filter((u) => u.unitOfMeasureId !== product.baseUnitOfMeasureId) ?? [];

  return (
    <div className="space-y-6">
      <div>
        <Link to="/products" className="text-sm text-indigo-600 hover:underline">
          ← Products
        </Link>
        <h2 className="mt-1 text-lg font-semibold text-slate-900">
          {product.name} <span className="font-normal text-slate-400">({product.sku})</span>
        </h2>
        <p className="text-sm text-slate-500">Base unit: {product.baseUnitOfMeasure?.code}</p>
      </div>

      <div className="rounded-xl bg-white p-6 shadow-sm ring-1 ring-slate-200">
        <h3 className="mb-1 text-sm font-semibold text-slate-900">Stock tracking</h3>
        <p className="text-sm text-slate-700">{TRACKING_LABELS[product.batchTracking ?? BatchTracking.None]}</p>

        {isAdmin && (
          <form
            onSubmit={(e) => {
              e.preventDefault();
              trackingMutation.mutate({ id: productId, data: { mode: newMode as BatchTracking } });
            }}
            className="mt-4 space-y-2"
          >
            <div className="flex items-center gap-3">
              <select
                value={newMode}
                onChange={(e) => {
                  setNewMode(e.target.value);
                  trackingMutation.reset();
                }}
                className="rounded-md border border-slate-300 px-3 py-1.5 text-sm"
              >
                <option value="">Change to...</option>
                {[BatchTracking.Batch, BatchTracking.BatchAndExpiry]
                  .filter((mode) => mode !== product.batchTracking)
                  .map((mode) => (
                    <option key={mode} value={mode}>
                      {TRACKING_LABELS[mode]}
                    </option>
                  ))}
              </select>
              <button
                type="submit"
                disabled={!newMode || trackingMutation.isPending}
                className="rounded-md bg-indigo-600 px-3 py-1.5 text-sm font-medium text-white transition hover:bg-indigo-700 disabled:cursor-not-allowed disabled:opacity-60"
              >
                Apply
              </button>
            </div>
            {!isTracked(product.batchTracking) && (
              <p className="text-xs text-slate-400">
                Switching this on cannot be undone. Stock already on hand moves into a batch called OPENING (no expiry)
                - balances do not change - and from then on every receipt, adjustment and sale of this product works by
                batch.
              </p>
            )}
            {trackingMutation.isError && (
              <p className="text-sm text-red-600">{errorMessage(trackingMutation.error)}</p>
            )}
          </form>
        )}
      </div>

      {isTracked(product.batchTracking) && (
        <div className="rounded-xl bg-white p-6 shadow-sm ring-1 ring-slate-200">
          <h3 className="mb-4 text-sm font-semibold text-slate-900">Batches</h3>
          <table className="w-full text-left text-sm">
            <thead>
              <tr className="border-b border-slate-200 text-slate-500">
                <th className="pb-2 pr-4 font-medium">Batch</th>
                <th className="pb-2 pr-4 font-medium">Expiry</th>
                <th className="pb-2 pr-4 font-medium">First received</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              {batchesQuery.data?.map((batch) => {
                const batchId = Number(batch.productBatchId);
                const isEditing = editingBatchId === batchId;

                return (
                  <tr key={batchId} className="border-b border-slate-100 last:border-0">
                    <td className="py-2 pr-4 text-slate-700">{batch.batchNumber}</td>
                    <td className="py-2 pr-4">
                      {isEditing ? (
                        <input
                          type="date"
                          value={editExpiry}
                          onChange={(e) => setEditExpiry(e.target.value)}
                          className="rounded-md border border-slate-300 px-2 py-1 text-sm"
                        />
                      ) : (
                        <ExpiryBadge expiry={batch.expiryDate} />
                      )}
                    </td>
                    <td className="py-2 pr-4 text-slate-500">
                      {batch.createdAt && new Date(batch.createdAt).toLocaleDateString()}
                    </td>
                    <td className="py-2 text-right whitespace-nowrap">
                      {isAdmin && product.batchTracking === BatchTracking.BatchAndExpiry && !isEditing && (
                        <button
                          onClick={() => {
                            expiryMutation.reset();
                            setEditingBatchId(batchId);
                            setEditExpiry(batch.expiryDate ?? "");
                          }}
                          className="text-sm font-medium text-indigo-600 hover:underline"
                        >
                          Edit expiry
                        </button>
                      )}
                      {isEditing && (
                        <>
                          <button
                            onClick={() => expiryMutation.mutate({ id: batchId, data: { expiryDate: editExpiry } })}
                            disabled={!editExpiry || expiryMutation.isPending}
                            className="mr-3 text-sm font-medium text-indigo-600 hover:underline disabled:opacity-60"
                          >
                            Save
                          </button>
                          <button onClick={() => setEditingBatchId(null)} className="text-sm text-slate-500 hover:underline">
                            Cancel
                          </button>
                        </>
                      )}
                    </td>
                  </tr>
                );
              })}
              {batchesQuery.data?.length === 0 && (
                <tr>
                  <td colSpan={4} className="py-4 text-slate-400">
                    No batches yet - they are created when stock is received or adjusted in.
                  </td>
                </tr>
              )}
            </tbody>
          </table>
          {expiryMutation.isError && <p className="mt-3 text-sm text-red-600">{errorMessage(expiryMutation.error)}</p>}
        </div>
      )}

      <div className="rounded-xl bg-white p-6 shadow-sm ring-1 ring-slate-200">
        <h3 className="mb-4 text-sm font-semibold text-slate-900">Unit conversions</h3>
        <table className="w-full text-left text-sm">
          <thead>
            <tr className="border-b border-slate-200 text-slate-500">
              <th className="pb-2 font-medium">Unit</th>
              <th className="pb-2 font-medium">1 unit equals (in {product.baseUnitOfMeasure?.code})</th>
            </tr>
          </thead>
          <tbody>
            {product.unitConversions?.map((conversion) => (
              <tr key={conversion.productUnitConversionId} className="border-b border-slate-100 last:border-0">
                <td className="py-2 text-slate-700">{conversion.unitOfMeasure?.code}</td>
                <td className="py-2 text-slate-700">{conversion.conversionFactor}</td>
              </tr>
            ))}
            {product.unitConversions?.length === 0 && (
              <tr>
                <td colSpan={2} className="py-4 text-slate-400">
                  No conversions defined - this product is only tracked in its base unit.
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>

      <div className="rounded-xl bg-white p-6 shadow-sm ring-1 ring-slate-200">
        <h3 className="mb-4 text-sm font-semibold text-slate-900">Add a conversion</h3>
        <form onSubmit={handleSubmit} className="flex items-end gap-3">
          <div>
            <label className="mb-1 block text-sm font-medium text-slate-700">Unit</label>
            <select
              value={unitOfMeasureId}
              onChange={(e) => setUnitOfMeasureId(e.target.value)}
              required
              className="rounded-md border border-slate-300 px-3 py-2 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
            >
              <option value="" disabled>
                Select...
              </option>
              {otherUnits.map((unit) => (
                <option key={unit.unitOfMeasureId} value={unit.unitOfMeasureId}>
                  {unit.code}
                </option>
              ))}
            </select>
          </div>
          <div>
            <label className="mb-1 block text-sm font-medium text-slate-700">
              1 unit = how many {product.baseUnitOfMeasure?.code}?
            </label>
            <input
              type="number"
              step="any"
              value={conversionFactor}
              onChange={(e) => setConversionFactor(e.target.value)}
              required
              className="w-40 rounded-md border border-slate-300 px-3 py-2 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
            />
          </div>
          <button
            type="submit"
            disabled={addConversionMutation.isPending}
            className="rounded-md bg-indigo-600 px-4 py-2 text-sm font-medium text-white transition hover:bg-indigo-700 disabled:cursor-not-allowed disabled:opacity-60"
          >
            Add
          </button>
        </form>
        {addConversionMutation.isError && (
          <p className="mt-2 text-sm text-red-600">Could not add conversion - check the values and try again.</p>
        )}
      </div>
    </div>
  );
}
