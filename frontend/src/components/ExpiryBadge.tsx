import { expiryStatus } from "../lib/batchTracking";

// An expiry date, coloured when it needs attention: red once expired (it can no
// longer be sold), amber within 30 days.
export function ExpiryBadge({ expiry }: { expiry?: string | null }) {
  if (!expiry) return <span className="text-slate-400">-</span>;

  const status = expiryStatus(expiry);

  if (status === "expired") {
    return (
      <span className="rounded-full bg-red-100 px-2.5 py-0.5 text-xs font-medium text-red-700">{expiry} · expired</span>
    );
  }

  if (status === "soon") {
    return (
      <span className="rounded-full bg-amber-100 px-2.5 py-0.5 text-xs font-medium text-amber-700">
        {expiry} · expiring soon
      </span>
    );
  }

  return <span className="text-slate-700">{expiry}</span>;
}
