const STATUS_STYLES: Record<string, string> = {
  Draft: "bg-slate-100 text-slate-700",
  Confirmed: "bg-blue-100 text-blue-700",
  InTransit: "bg-sky-100 text-sky-700",
  PartiallyReceived: "bg-amber-100 text-amber-700",
  Received: "bg-emerald-100 text-emerald-700",
  Cancelled: "bg-red-100 text-red-700",
};

export function StatusBadge({ status }: { status?: string }) {
  if (!status) return null;

  return (
    <span
      className={`rounded-full px-2.5 py-0.5 text-xs font-medium ${STATUS_STYLES[status] ?? "bg-slate-100 text-slate-700"}`}
    >
      {/* "PartiallyReceived" -> "Partially Received" */}
      {status.replace(/([a-z])([A-Z])/g, "$1 $2")}
    </span>
  );
}
