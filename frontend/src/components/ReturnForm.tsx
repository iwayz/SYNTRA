import { useState, type FormEvent } from "react";
import { ReturnDisposition, type ReturnableLine } from "../api/generated/syntraApiV1.schemas";

export type ReturnEntry = {
  lineId: number;
  quantity: number;
  batchNumber: string | null;
  disposition: ReturnDisposition;
};

const inputClasses =
  "rounded-md border border-slate-300 px-2 py-1.5 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500";

type Props = {
  lines: ReturnableLine[];
  // Sales returns choose per line whether the goods go back to stock or are written off.
  withDisposition: boolean;
  isPending: boolean;
  submitLabel: string;
  intro: string;
  onSubmit: (entries: ReturnEntry[], reason: string) => Promise<unknown>;
};

// Shared by the sales and purchase order pages. An entry is "<line id>:<n>": entry 0
// is the line itself, and a batch-tracked line can take extra entries so one return
// can draw on several batches.
export function ReturnForm({ lines, withDisposition, isPending, submitLabel, intro, onSubmit }: Props) {
  const [quantities, setQuantities] = useState<Record<string, string>>({});
  const [batches, setBatches] = useState<Record<string, string>>({});
  const [dispositions, setDispositions] = useState<Record<string, ReturnDisposition>>({});
  const [extras, setExtras] = useState<Record<number, number>>({});
  const [reason, setReason] = useState("");

  const returnableLines = lines.filter((line) => Number(line.returnable) > 0);

  if (returnableLines.length === 0) {
    return <p className="text-sm text-slate-400">Everything on this order has already been returned.</p>;
  }

  const entries: ReturnEntry[] = returnableLines.flatMap((line) =>
    Array.from({ length: 1 + (extras[Number(line.lineId)] ?? 0) }, (_, n) => {
      const key = `${line.lineId}:${n}`;
      return {
        lineId: Number(line.lineId),
        quantity: Number(quantities[key] ?? 0),
        batchNumber: line.batchTracked ? batches[key] || null : null,
        disposition: dispositions[key] ?? ReturnDisposition.Restock,
      };
    }),
  );

  const chosen = entries.filter((entry) => entry.quantity > 0);
  const missingBatch = chosen.some((entry) => {
    const line = returnableLines.find((l) => Number(l.lineId) === entry.lineId);
    return line?.batchTracked && !entry.batchNumber;
  });

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();
    try {
      await onSubmit(chosen, reason.trim());
      setQuantities({});
      setBatches({});
      setDispositions({});
      setExtras({});
      setReason("");
    } catch {
      // The parent shows the error; keep what was typed so it can be corrected.
    }
  }

  return (
    <form onSubmit={handleSubmit} className="space-y-4">
      <p className="text-xs text-slate-400">{intro}</p>
      <table className="w-full text-left text-sm">
        <thead>
          <tr className="text-slate-500">
            <th className="pb-2 pr-4 font-medium">Product</th>
            <th className="pb-2 pr-4 text-right font-medium">Can still return</th>
            <th className="pb-2 pr-4 font-medium">Returning now</th>
            <th className="pb-2 pr-4 font-medium">Batch</th>
            {withDisposition && <th className="pb-2 font-medium">Goods are</th>}
          </tr>
        </thead>
        <tbody>
          {returnableLines.flatMap((line) =>
            Array.from({ length: 1 + (extras[Number(line.lineId)] ?? 0) }, (_, n) => {
              const key = `${line.lineId}:${n}`;
              const isLast = n === (extras[Number(line.lineId)] ?? 0);

              return (
                <tr key={key}>
                  <td className="py-1 pr-4 text-slate-700">
                    {n === 0 ? (
                      <>
                        {line.productName} <span className="text-slate-400">({line.unitCode})</span>
                      </>
                    ) : (
                      <span className="pl-4 text-slate-400">+ another batch</span>
                    )}
                  </td>
                  <td className="py-1 pr-4 text-right text-slate-700">{n === 0 ? line.returnable : ""}</td>
                  <td className="py-1 pr-4">
                    <input
                      type="number"
                      step="any"
                      min="0"
                      max={Number(line.returnable)}
                      value={quantities[key] ?? ""}
                      placeholder="0"
                      onChange={(e) => setQuantities({ ...quantities, [key]: e.target.value })}
                      className={`${inputClasses} w-24`}
                    />
                  </td>
                  <td className="py-1 pr-4">
                    {line.batchTracked ? (
                      <>
                        <select
                          value={batches[key] ?? ""}
                          onChange={(e) => setBatches({ ...batches, [key]: e.target.value })}
                          className={inputClasses}
                        >
                          <option value="">Select batch...</option>
                          {line.batches?.map((batch) => (
                            <option key={batch.batchId} value={batch.batchNumber}>
                              {batch.batchNumber}
                              {batch.expiryDate ? ` · expires ${batch.expiryDate}` : ""} · up to {batch.returnable}
                            </option>
                          ))}
                        </select>
                        {isLast && (
                          <button
                            type="button"
                            onClick={() => setExtras({ ...extras, [Number(line.lineId)]: (extras[Number(line.lineId)] ?? 0) + 1 })}
                            className="ml-3 text-xs font-medium text-indigo-600 hover:underline"
                          >
                            Another batch
                          </button>
                        )}
                      </>
                    ) : (
                      <span className="text-slate-400">-</span>
                    )}
                  </td>
                  {withDisposition && (
                    <td className="py-1">
                      <select
                        value={dispositions[key] ?? ReturnDisposition.Restock}
                        onChange={(e) => setDispositions({ ...dispositions, [key]: e.target.value as ReturnDisposition })}
                        className={inputClasses}
                      >
                        <option value={ReturnDisposition.Restock}>Good - back into stock</option>
                        <option value={ReturnDisposition.WriteOff}>Damaged - write off</option>
                      </select>
                    </td>
                  )}
                </tr>
              );
            }),
          )}
        </tbody>
      </table>

      <div>
        <label className="mb-1 block text-sm font-medium text-slate-700">Reason</label>
        <input
          value={reason}
          onChange={(e) => setReason(e.target.value)}
          required
          maxLength={200}
          className={`${inputClasses} w-96`}
        />
      </div>

      {missingBatch && <p className="text-xs text-amber-700">Choose a batch for each batch-tracked item being returned.</p>}

      <button
        type="submit"
        disabled={isPending || chosen.length === 0 || missingBatch || reason.trim() === ""}
        className="rounded-md bg-indigo-600 px-4 py-2 text-sm font-medium text-white transition hover:bg-indigo-700 disabled:cursor-not-allowed disabled:opacity-60"
      >
        {submitLabel}
      </button>
    </form>
  );
}
