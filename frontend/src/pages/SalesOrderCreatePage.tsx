import { useEffect } from "react";
import { useForm, useFieldArray } from "react-hook-form";
import { useNavigate } from "react-router-dom";
import { usePostSalesSalesOrders } from "../api/generated/sales-orders/sales-orders";
import { useGetInventoryProducts } from "../api/generated/products/products";
import { useGetInventoryUnitsOfMeasure } from "../api/generated/unit-of-measures/unit-of-measures";
import { useGetInventoryWarehouses } from "../api/generated/warehouses/warehouses";
import { errorMessage } from "../api/errors";
import { useBranch } from "../branch/BranchContext";
import { useGetSalesCustomers } from "../api/generated/customers/customers";

type LineFormValues = {
  productId: string;
  unitOfMeasureId: string;
  quantity: string;
  unitPrice: string;
};

type FormValues = {
  customerId: string;
  warehouseId: string;
  orderDate: string;
  lines: LineFormValues[];
};

const emptyLine: LineFormValues = { productId: "", unitOfMeasureId: "", quantity: "", unitPrice: "" };

export function SalesOrderCreatePage() {
  const navigate = useNavigate();
  const customersQuery = useGetSalesCustomers();
  const { branchId } = useBranch();
  const warehousesQuery = useGetInventoryWarehouses({ branchId });
  const productsQuery = useGetInventoryProducts();
  const unitsQuery = useGetInventoryUnitsOfMeasure();

  const { register, control, handleSubmit, setValue } = useForm<FormValues>({
    defaultValues: {
      customerId: "",
      warehouseId: "",
      orderDate: new Date().toISOString().slice(0, 10),
      lines: [emptyLine],
    },
  });

  const { fields, append, remove } = useFieldArray({ control, name: "lines" });

  // The warehouses offered depend on the header's branch, so a previously picked one may no longer exist.
  useEffect(() => {
    setValue("warehouseId", "");
  }, [branchId, setValue]);

  const createMutation = usePostSalesSalesOrders({
    mutation: {
      onSuccess: (order) => navigate(`/sales-orders/${order.salesOrderId}`),
    },
  });

  function onSubmit(values: FormValues) {
    createMutation.mutate({
      data: {
        customerId: Number(values.customerId),
        warehouseId: Number(values.warehouseId),
        orderDate: new Date(values.orderDate).toISOString(),
        lines: values.lines.map((line) => ({
          productId: Number(line.productId),
          unitOfMeasureId: Number(line.unitOfMeasureId),
          quantity: Number(line.quantity),
          unitPrice: Number(line.unitPrice),
        })),
      },
    });
  }

  return (
    <div className="space-y-6">
      <h2 className="text-lg font-semibold text-slate-900">New Sales Order</h2>

      <form onSubmit={handleSubmit(onSubmit)} className="space-y-6">
        <div className="rounded-xl bg-white p-6 shadow-sm ring-1 ring-slate-200">
          <div className="flex flex-wrap gap-4">
            <div>
              <label className="mb-1 block text-sm font-medium text-slate-700">Customer</label>
              <select
                {...register("customerId", { required: true })}
                className="rounded-md border border-slate-300 px-3 py-2 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
              >
                <option value="" disabled>
                  Select...
                </option>
                {customersQuery.data?.map((customer) => (
                  <option key={customer.customerId} value={customer.customerId}>
                    {customer.name}
                  </option>
                ))}
              </select>
            </div>
            <div>
              <label className="mb-1 block text-sm font-medium text-slate-700">Warehouse</label>
              <select
                {...register("warehouseId", { required: true })}
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
              <label className="mb-1 block text-sm font-medium text-slate-700">Order date</label>
              <input
                type="date"
                {...register("orderDate", { required: true })}
                className="rounded-md border border-slate-300 px-3 py-2 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
              />
            </div>
          </div>
        </div>

        <div className="rounded-xl bg-white p-6 shadow-sm ring-1 ring-slate-200">
          <h3 className="mb-4 text-sm font-semibold text-slate-900">Lines</h3>
          <table className="w-full text-left text-sm">
            <thead>
              <tr className="text-slate-500">
                <th className="pb-2 pr-2 font-medium">Product</th>
                <th className="pb-2 pr-2 font-medium">Unit</th>
                <th className="pb-2 pr-2 font-medium">Quantity</th>
                <th className="pb-2 pr-2 font-medium">Unit price</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              {fields.map((field, index) => (
                <tr key={field.id}>
                  <td className="py-1 pr-2">
                    <select
                      {...register(`lines.${index}.productId`, { required: true })}
                      className="rounded-md border border-slate-300 px-2 py-1.5 text-sm"
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
                  </td>
                  <td className="py-1 pr-2">
                    <select
                      {...register(`lines.${index}.unitOfMeasureId`, { required: true })}
                      className="rounded-md border border-slate-300 px-2 py-1.5 text-sm"
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
                  </td>
                  <td className="py-1 pr-2">
                    <input
                      type="number"
                      step="any"
                      {...register(`lines.${index}.quantity`, { required: true })}
                      className="w-24 rounded-md border border-slate-300 px-2 py-1.5 text-sm"
                    />
                  </td>
                  <td className="py-1 pr-2">
                    <input
                      type="number"
                      step="any"
                      {...register(`lines.${index}.unitPrice`, { required: true })}
                      className="w-28 rounded-md border border-slate-300 px-2 py-1.5 text-sm"
                    />
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
              ))}
            </tbody>
          </table>
          <button
            type="button"
            onClick={() => append(emptyLine)}
            className="mt-3 text-sm font-medium text-indigo-600 hover:underline"
          >
            + Add line
          </button>
        </div>

        <button
          type="submit"
          disabled={createMutation.isPending}
          className="rounded-md bg-indigo-600 px-4 py-2 text-sm font-medium text-white transition hover:bg-indigo-700 disabled:cursor-not-allowed disabled:opacity-60"
        >
          Create sales order
        </button>
        {createMutation.isError && (
          <p className="text-sm text-red-600">
            {errorMessage(createMutation.error, "Could not create the order - check the values and try again.")}
          </p>
        )}
      </form>
    </div>
  );
}
