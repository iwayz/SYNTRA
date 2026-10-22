import { useQueryClient } from "@tanstack/react-query";
import { useState, type FormEvent } from "react";
import {
  getGetInventoryUnitsOfMeasureQueryKey,
  useGetInventoryUnitsOfMeasure,
  usePostInventoryUnitsOfMeasure,
} from "../api/generated/unit-of-measures/unit-of-measures";

export function UnitsOfMeasurePage() {
  const queryClient = useQueryClient();
  const unitsQuery = useGetInventoryUnitsOfMeasure();
  const [code, setCode] = useState("");
  const [name, setName] = useState("");

  const createMutation = usePostInventoryUnitsOfMeasure({
    mutation: {
      onSuccess: () => {
        setCode("");
        setName("");
        queryClient.invalidateQueries({ queryKey: getGetInventoryUnitsOfMeasureQueryKey() });
      },
    },
  });

  function handleSubmit(event: FormEvent) {
    event.preventDefault();
    createMutation.mutate({ data: { code, name } });
  }

  return (
    <div className="space-y-6">
      <h2 className="text-lg font-semibold text-slate-900">Units of Measure</h2>

      <div className="rounded-xl bg-white p-6 shadow-sm ring-1 ring-slate-200">
        <table className="w-full text-left text-sm">
          <thead>
            <tr className="border-b border-slate-200 text-slate-500">
              <th className="pb-2 font-medium">Code</th>
              <th className="pb-2 font-medium">Name</th>
            </tr>
          </thead>
          <tbody>
            {unitsQuery.data?.map((unit) => (
              <tr key={unit.unitOfMeasureId} className="border-b border-slate-100 last:border-0">
                <td className="py-2 text-slate-700">{unit.code}</td>
                <td className="py-2 text-slate-700">{unit.name}</td>
              </tr>
            ))}
            {unitsQuery.data?.length === 0 && (
              <tr>
                <td colSpan={2} className="py-4 text-slate-400">
                  No units of measure yet.
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>

      <div className="rounded-xl bg-white p-6 shadow-sm ring-1 ring-slate-200">
        <h3 className="mb-4 text-sm font-semibold text-slate-900">Add a unit</h3>
        <form onSubmit={handleSubmit} className="flex items-end gap-3">
          <div>
            <label className="mb-1 block text-sm font-medium text-slate-700">Code</label>
            <input
              value={code}
              onChange={(e) => setCode(e.target.value)}
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
          <button
            type="submit"
            disabled={createMutation.isPending}
            className="rounded-md bg-indigo-600 px-4 py-2 text-sm font-medium text-white transition hover:bg-indigo-700 disabled:cursor-not-allowed disabled:opacity-60"
          >
            Add
          </button>
        </form>
        {createMutation.isError && (
          <p className="mt-2 text-sm text-red-600">Could not create unit - check the values and try again.</p>
        )}
      </div>
    </div>
  );
}
