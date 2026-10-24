import { useQueryClient } from "@tanstack/react-query";
import { useEffect, useState, type FormEvent } from "react";
import { errorMessage } from "../api/errors";
import {
  getGetInventoryStockBalanceQueryKey,
  getGetInventoryStockMovementsQueryKey,
  useGetInventoryStockMovements,
  usePostInventoryStockAdjustments,
} from "../api/generated/stock/stock";
import { useGetInventoryProducts } from "../api/generated/products/products";
import { useGetInventoryWarehouses } from "../api/generated/warehouses/warehouses";
import { useBranch } from "../branch/BranchContext";

export function StockMovementsPage() {
  const queryClient = useQueryClient();
  const productsQuery = useGetInventoryProducts();
  const { branchId, branchName } = useBranch();
  const warehousesQuery = useGetInventoryWarehouses({ branchId });

  const [filterProductId, setFilterProductId] = useState("");
  const [filterWarehouseId, setFilterWarehouseId] = useState("");

  const movementsQuery = useGetInventoryStockMovements({
    productId: filterProductId ? Number(filterProductId) : undefined,
    warehouseId: filterWarehouseId ? Number(filterWarehouseId) : undefined,
    branchId,
  });

  const [productId, setProductId] = useState("");
  const [warehouseId, setWarehouseId] = useState("");
  const [quantity, setQuantity] = useState("");
  const [notes, setNotes] = useState("");

  // Warehouses shown depend on the header's branch, so a previously picked one may no longer exist.
  useEffect(() => {
    setFilterWarehouseId("");
    setWarehouseId("");
  }, [branchId]);

  const createMutation = usePostInventoryStockAdjustments({
    mutation: {
      onSuccess: () => {
        setProductId("");
        setWarehouseId("");
        setQuantity("");
        setNotes("");
        queryClient.invalidateQueries({ queryKey: getGetInventoryStockMovementsQueryKey() });
        queryClient.invalidateQueries({ queryKey: getGetInventoryStockBalanceQueryKey() });
      },
    },
  });

  function handleSubmit(event: FormEvent) {
    event.preventDefault();
    createMutation.mutate({
      data: {
        productId: Number(productId),
        warehouseId: Number(warehouseId),
        quantity: Number(quantity),
        notes,
      },
    });
  }

  return (
    <div className="space-y-6">
      <h2 className="text-lg font-semibold text-slate-900">Stock Movements</h2>

      <div className="rounded-xl bg-white p-6 shadow-sm ring-1 ring-slate-200">
        <div className="mb-4 flex gap-3">
          <select
            value={filterProductId}
            onChange={(e) => setFilterProductId(e.target.value)}
            className="rounded-md border border-slate-300 px-3 py-1.5 text-sm"
          >
            <option value="">All products</option>
            {productsQuery.data?.map((product) => (
              <option key={product.productId} value={product.productId}>
                {product.name}
              </option>
            ))}
          </select>
          <select
            value={filterWarehouseId}
            onChange={(e) => setFilterWarehouseId(e.target.value)}
            className="rounded-md border border-slate-300 px-3 py-1.5 text-sm"
          >
            <option value="">All warehouses</option>
            {warehousesQuery.data?.map((warehouse) => (
              <option key={warehouse.warehouseId} value={warehouse.warehouseId}>
                {warehouse.name}
              </option>
            ))}
          </select>
        </div>

        <table className="w-full text-left text-sm">
          <thead>
            <tr className="border-b border-slate-200 text-slate-500">
              <th className="pb-2 pr-4 font-medium">Date</th>
              <th className="pb-2 pr-4 font-medium">Product</th>
              <th className="pb-2 pr-4 font-medium">Branch</th>
              <th className="pb-2 pr-4 font-medium">Warehouse</th>
              <th className="pb-2 pr-4 font-medium">Type</th>
              <th className="pb-2 pr-4 text-right font-medium">Quantity</th>
              <th className="pb-2 pl-4 font-medium">Notes</th>
            </tr>
          </thead>
          <tbody>
            {movementsQuery.data?.map((movement) => (
              <tr key={movement.stockMovementId} className="border-b border-slate-100 last:border-0">
                <td className="py-2 pr-4 text-slate-500">
                  {movement.occurredAt && new Date(movement.occurredAt).toLocaleString()}
                </td>
                <td className="py-2 pr-4 text-slate-700">{movement.product?.name}</td>
                <td className="py-2 pr-4 text-slate-700">{branchName(movement.warehouse?.branchId)}</td>
                <td className="py-2 pr-4 text-slate-700">{movement.warehouse?.name}</td>
                <td className="py-2 pr-4 text-slate-700">{movement.movementType}</td>
                <td
                  className={`py-2 pr-4 text-right font-medium ${
                    Number(movement.quantity) >= 0 ? "text-emerald-600" : "text-red-600"
                  }`}
                >
                  {movement.quantity}
                </td>
                <td className="py-2 pl-4 text-slate-500">{movement.notes}</td>
              </tr>
            ))}
            {movementsQuery.data?.length === 0 && (
              <tr>
                <td colSpan={7} className="py-4 text-slate-400">
                  No movements match these filters.
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>

      <div className="rounded-xl bg-white p-6 shadow-sm ring-1 ring-slate-200">
        <h3 className="mb-4 text-sm font-semibold text-slate-900">Manual stock adjustment</h3>
        <form onSubmit={handleSubmit} className="flex flex-wrap items-end gap-3">
          <div>
            <label className="mb-1 block text-sm font-medium text-slate-700">Product</label>
            <select
              value={productId}
              onChange={(e) => setProductId(e.target.value)}
              required
              className="rounded-md border border-slate-300 px-3 py-2 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
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
          <div>
            <label className="mb-1 block text-sm font-medium text-slate-700">Warehouse</label>
            <select
              value={warehouseId}
              onChange={(e) => setWarehouseId(e.target.value)}
              required
              className="rounded-md border border-slate-300 px-3 py-2 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
            >
              <option value="" disabled>
                Select...
              </option>
              {warehousesQuery.data?.map((warehouse) => {
                const branchInactive = warehouse.branch?.isActive === false;
                return (
                  <option key={warehouse.warehouseId} value={warehouse.warehouseId} disabled={branchInactive}>
                    {warehouse.name}
                    {branchInactive ? " (inactive branch)" : ""}
                  </option>
                );
              })}
            </select>
          </div>
          <div>
            <label className="mb-1 block text-sm font-medium text-slate-700">
              Quantity <span className="font-normal text-slate-400">(negative to reduce)</span>
            </label>
            <input
              type="number"
              step="any"
              value={quantity}
              onChange={(e) => setQuantity(e.target.value)}
              required
              className="w-32 rounded-md border border-slate-300 px-3 py-2 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
            />
          </div>
          <div>
            <label className="mb-1 block text-sm font-medium text-slate-700">Notes</label>
            <input
              value={notes}
              onChange={(e) => setNotes(e.target.value)}
              required
              className="rounded-md border border-slate-300 px-3 py-2 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
            />
          </div>
          <button
            type="submit"
            disabled={createMutation.isPending}
            className="rounded-md bg-indigo-600 px-4 py-2 text-sm font-medium text-white transition hover:bg-indigo-700 disabled:cursor-not-allowed disabled:opacity-60"
          >
            Record adjustment
          </button>
        </form>
        {createMutation.isError && (
          <p className="mt-2 text-sm text-red-600">
            {errorMessage(createMutation.error, "Could not record adjustment - check the values and try again.")}
          </p>
        )}
      </div>
    </div>
  );
}
