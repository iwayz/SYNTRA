import { useQueryClient } from "@tanstack/react-query";
import { useState, type FormEvent } from "react";
import { Link } from "react-router-dom";
import {
  getGetInventoryProductsQueryKey,
  useGetInventoryProducts,
  usePostInventoryProducts,
} from "../api/generated/products/products";
import { BatchTracking } from "../api/generated/syntraApiV1.schemas";
import { useGetInventoryUnitsOfMeasure } from "../api/generated/unit-of-measures/unit-of-measures";
import { errorMessage } from "../api/errors";
import { TRACKING_LABELS } from "../lib/batchTracking";

export function ProductsPage() {
  const queryClient = useQueryClient();
  const productsQuery = useGetInventoryProducts();
  const unitsQuery = useGetInventoryUnitsOfMeasure();

  const [sku, setSku] = useState("");
  const [name, setName] = useState("");
  const [baseUnitOfMeasureId, setBaseUnitOfMeasureId] = useState("");
  const [batchTracking, setBatchTracking] = useState<BatchTracking>(BatchTracking.None);

  const createMutation = usePostInventoryProducts({
    mutation: {
      onSuccess: () => {
        setSku("");
        setName("");
        setBaseUnitOfMeasureId("");
        setBatchTracking(BatchTracking.None);
        queryClient.invalidateQueries({ queryKey: getGetInventoryProductsQueryKey() });
      },
    },
  });

  function handleSubmit(event: FormEvent) {
    event.preventDefault();
    createMutation.mutate({ data: { sku, name, baseUnitOfMeasureId: Number(baseUnitOfMeasureId), batchTracking } });
  }

  return (
    <div className="space-y-6">
      <h2 className="text-lg font-semibold text-slate-900">Products</h2>

      <div className="rounded-xl bg-white p-6 shadow-sm ring-1 ring-slate-200">
        <table className="w-full text-left text-sm">
          <thead>
            <tr className="border-b border-slate-200 text-slate-500">
              <th className="pb-2 font-medium">SKU</th>
              <th className="pb-2 font-medium">Name</th>
              <th className="pb-2 font-medium">Base unit</th>
              <th className="pb-2 font-medium">Tracking</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            {productsQuery.data?.map((product) => (
              <tr key={product.productId} className="border-b border-slate-100 last:border-0">
                <td className="py-2 text-slate-700">{product.sku}</td>
                <td className="py-2 text-slate-700">{product.name}</td>
                <td className="py-2 text-slate-700">{product.baseUnitOfMeasure?.code}</td>
                <td className="py-2 text-slate-700">{TRACKING_LABELS[product.batchTracking ?? BatchTracking.None]}</td>
                <td className="py-2 text-right">
                  <Link to={`/products/${product.productId}`} className="text-sm font-medium text-indigo-600 hover:underline">
                    View
                  </Link>
                </td>
              </tr>
            ))}
            {productsQuery.data?.length === 0 && (
              <tr>
                <td colSpan={5} className="py-4 text-slate-400">
                  No products yet.
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>

      <div className="rounded-xl bg-white p-6 shadow-sm ring-1 ring-slate-200">
        <h3 className="mb-4 text-sm font-semibold text-slate-900">Add a product</h3>
        <form onSubmit={handleSubmit} className="flex flex-wrap items-end gap-3">
          <div>
            <label className="mb-1 block text-sm font-medium text-slate-700">SKU</label>
            <input
              value={sku}
              onChange={(e) => setSku(e.target.value)}
              required
              className="rounded-md border border-slate-300 px-3 py-2 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
            />
          </div>
          <div>
            <label className="mb-1 block text-sm font-medium text-slate-700">Name</label>
            <input
              value={name}
              onChange={(e) => setName(e.target.value)}
              required
              className="rounded-md border border-slate-300 px-3 py-2 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
            />
          </div>
          <div>
            <label className="mb-1 block text-sm font-medium text-slate-700">Base unit</label>
            <select
              value={baseUnitOfMeasureId}
              onChange={(e) => setBaseUnitOfMeasureId(e.target.value)}
              required
              className="rounded-md border border-slate-300 px-3 py-2 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
            >
              <option value="" disabled>
                Select...
              </option>
              {unitsQuery.data?.map((unit) => (
                <option key={unit.unitOfMeasureId} value={unit.unitOfMeasureId}>
                  {unit.code}
                </option>
              ))}
            </select>
          </div>
          <div>
            <label className="mb-1 block text-sm font-medium text-slate-700">Stock tracking</label>
            <select
              value={batchTracking}
              onChange={(e) => setBatchTracking(e.target.value as BatchTracking)}
              className="rounded-md border border-slate-300 px-3 py-2 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
            >
              {Object.values(BatchTracking).map((mode) => (
                <option key={mode} value={mode}>
                  {TRACKING_LABELS[mode]}
                </option>
              ))}
            </select>
          </div>
          <button
            type="submit"
            disabled={createMutation.isPending}
            className="rounded-md bg-indigo-600 px-4 py-2 text-sm font-medium text-white transition hover:bg-indigo-700 disabled:cursor-not-allowed disabled:opacity-60"
          >
            Add
          </button>
        </form>
        {createMutation.isError && (
          <p className="mt-2 text-sm text-red-600">
            {errorMessage(createMutation.error, "Could not create product - check the values and try again.")}
          </p>
        )}
      </div>
    </div>
  );
}
