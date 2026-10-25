import { useEffect } from "react";
import { useFieldArray, useForm } from "react-hook-form";
import { useNavigate } from "react-router-dom";
import { errorMessage } from "../api/errors";
import { useGetInventoryBatches } from "../api/generated/batches/batches";
import { useGetInventoryProducts } from "../api/generated/products/products";
import {
  useGetInventoryTransferDestinations,
  usePostInventoryTransfers,
} from "../api/generated/stock-transfers/stock-transfers";
import { useGetInventoryWarehouses } from "../api/generated/warehouses/warehouses";
import { useBranch } from "../branch/BranchContext";
import { isTracked } from "../lib/batchTracking";

type LineFormValues = {
  productId: string;
  quantity: string;
  batchNumber: string;
};

type FormValues = {
  fromWarehouseId: string;
  toWarehouseId: string;
  notes: string;
  lines: LineFormValues[];
};

const emptyLine: LineFormValues = { productId: "", quantity: "", batchNumber: "" };

const selectClasses =
  "rounded-md border border-slate-300 px-3 py-2 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500";
const cellClasses = "rounded-md border border-slate-300 px-2 py-1.5 text-sm";

export function TransferCreatePage() {
  const navigate = useNavigate();
  const { branchId } = useBranch();

  // The source is one of the user's own warehouses; the destination can be in any
  // active branch, so it comes from the destinations lookup (names only).
  const warehousesQuery = useGetInventoryWarehouses({ branchId });
  const destinationsQuery = useGetInventoryTransferDestinations();
  const productsQuery = useGetInventoryProducts();
  const batchesQuery = useGetInventoryBatches();

  const { register, control, handleSubmit, setValue, watch } = useForm<FormValues>({
    defaultValues: { fromWarehouseId: "", toWarehouseId: "", notes: "", lines: [emptyLine] },
  });

  const { fields, append, remove } = useFieldArray({ control, name: "lines" });

  // The sources offered depend on the header's branch, so a previous pick may be gone.
  useEffect(() => {
    setValue("fromWarehouseId", "");
  }, [branchId, setValue]);

  const createMutation = usePostInventoryTransfers({
    mutation: {
      onSuccess: (transfer) => navigate(`/transfers/${transfer.stockTransferId}`),
    },
  });

  const fromWarehouseId = watch("fromWarehouseId");
  const watchedLines = watch("lines");

  // Destination options grouped by branch, without the chosen source.
  const destinationGroups = new Map<string, NonNullable<typeof destinationsQuery.data>>();
  for (const destination of destinationsQuery.data ?? []) {
    if (String(destination.warehouseId) === fromWarehouseId) continue;
    const group = destinationGroups.get(destination.branchName) ?? [];
    group.push(destination);
    destinationGroups.set(destination.branchName, group);
  }

  function onSubmit(values: FormValues) {
    createMutation.mutate({
      data: {
        fromWarehouseId: Number(values.fromWarehouseId),
        toWarehouseId: Number(values.toWarehouseId),
        notes: values.notes.trim() || null,
        lines: values.lines.map((line) => ({
          productId: Number(line.productId),
          quantity: Number(line.quantity),
          batchNumber: line.batchNumber.trim() || null,
        })),
      },
    });
  }

  return (
    <div className="space-y-6">
      <h2 className="text-lg font-semibold text-slate-900">New Stock Transfer</h2>

      <form onSubmit={handleSubmit(onSubmit)} className="space-y-6">
        <div className="rounded-xl bg-white p-6 shadow-sm ring-1 ring-slate-200">
          <div className="flex flex-wrap gap-4">
            <div>
              <label className="mb-1 block text-sm font-medium text-slate-700">From warehouse</label>
              <select {...register("fromWarehouseId", { required: true })} className={selectClasses}>
                <option value="" disabled>
                  Select...
                </option>
                {warehousesQuery.data?.map((warehouse) => {
                  const branchInactive = warehouse.branch?.isActive === false;
                  return (
                    <option key={warehouse.warehouseId} value={warehouse.warehouseId} disabled={branchInactive}>
                      {warehouse.name} · {warehouse.branch?.name}
                      {branchInactive ? " (inactive branch)" : ""}
                    </option>
                  );
                })}
              </select>
            </div>
            <div>
              <label className="mb-1 block text-sm font-medium text-slate-700">To warehouse</label>
              <select {...register("toWarehouseId", { required: true })} className={selectClasses}>
                <option value="" disabled>
                  Select...
                </option>
                {[...destinationGroups.entries()].map(([branchName, destinations]) => (
                  <optgroup key={branchName} label={branchName}>
                    {destinations.map((destination) => (
                      <option key={destination.warehouseId} value={destination.warehouseId}>
                        {destination.warehouseName}
                      </option>
                    ))}
                  </optgroup>
                ))}
              </select>
            </div>
          </div>
          <div className="mt-4">
            <label className="mb-1 block text-sm font-medium text-slate-700">Notes (optional)</label>
            <input {...register("notes")} maxLength={500} className={`${selectClasses} w-96`} />
          </div>
        </div>

        <div className="rounded-xl bg-white p-6 shadow-sm ring-1 ring-slate-200">
          <h3 className="mb-1 text-sm font-semibold text-slate-900">Items</h3>
          <p className="mb-4 text-xs text-slate-400">
            Quantities are in each product's base unit. Stock is checked and taken out when you dispatch, not now. For
            batch-tracked products the earliest-expiring batches go first unless you name a batch.
          </p>
          <table className="w-full text-left text-sm">
            <thead>
              <tr className="text-slate-500">
                <th className="pb-2 pr-2 font-medium">Product</th>
                <th className="pb-2 pr-2 font-medium">Quantity</th>
                <th className="pb-2 pr-2 font-medium">Batch (optional)</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              {fields.map((field, index) => {
                const productId = watchedLines?.[index]?.productId;
                const product = productsQuery.data?.find((p) => String(p.productId) === productId);

                return (
                  <tr key={field.id}>
                    <td className="py-1 pr-2">
                      <select {...register(`lines.${index}.productId`, { required: true })} className={cellClasses}>
                        <option value="" disabled>
                          Select...
                        </option>
                        {productsQuery.data?.map((p) => (
                          <option key={p.productId} value={p.productId}>
                            {p.name} ({p.baseUnitOfMeasure?.code})
                          </option>
                        ))}
                      </select>
                    </td>
                    <td className="py-1 pr-2">
                      <input
                        type="number"
                        step="any"
                        min="0"
                        {...register(`lines.${index}.quantity`, { required: true })}
                        className={`${cellClasses} w-28`}
                      />
                    </td>
                    <td className="py-1 pr-2">
                      {isTracked(product?.batchTracking) ? (
                        <>
                          <input
                            {...register(`lines.${index}.batchNumber`)}
                            list={`transfer-batches-${index}`}
                            maxLength={50}
                            placeholder="auto (earliest expiry)"
                            className={`${cellClasses} w-44`}
                          />
                          <datalist id={`transfer-batches-${index}`}>
                            {batchesQuery.data
                              ?.filter((batch) => String(batch.productId) === productId)
                              .map((batch) => (
                                <option key={batch.productBatchId} value={batch.batchNumber} />
                              ))}
                          </datalist>
                        </>
                      ) : (
                        <span className="text-slate-400">-</span>
                      )}
                    </td>
                    <td className="py-1">
                      <button
                        type="button"
                        onClick={() => remove(index)}
                        disabled={fields.length === 1}
                        className="text-sm text-red-600 hover:underline disabled:cursor-not-allowed disabled:text-slate-300 disabled:no-underline"
                      >
                        Remove
                      </button>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
          <button
            type="button"
            onClick={() => append(emptyLine)}
            className="mt-3 text-sm font-medium text-indigo-600 hover:underline"
          >
            + Add item
          </button>
        </div>

        <button
          type="submit"
          disabled={createMutation.isPending}
          className="rounded-md bg-indigo-600 px-4 py-2 text-sm font-medium text-white transition hover:bg-indigo-700 disabled:cursor-not-allowed disabled:opacity-60"
        >
          Create transfer
        </button>
        {createMutation.isError && (
          <p className="text-sm text-red-600">
            {errorMessage(createMutation.error, "Could not create the transfer - check the values and try again.")}
          </p>
        )}
      </form>
    </div>
  );
}
