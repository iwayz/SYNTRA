import { useQueryClient } from "@tanstack/react-query";
import { useState, type FormEvent } from "react";
import { Link, useParams } from "react-router-dom";
import {
  getGetInventoryProductsIdQueryKey,
  useGetInventoryProductsId,
  usePostInventoryProductsIdUnitConversions,
} from "../api/generated/products/products";
import { useGetInventoryUnitsOfMeasure } from "../api/generated/unit-of-measures/unit-of-measures";

export function ProductDetailPage() {
  const { id } = useParams<{ id: string }>();
  const productId = Number(id);
  const queryClient = useQueryClient();

  const productQuery = useGetInventoryProductsId(productId);
  const unitsQuery = useGetInventoryUnitsOfMeasure();

  const [unitOfMeasureId, setUnitOfMeasureId] = useState("");
  const [conversionFactor, setConversionFactor] = useState("");

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
