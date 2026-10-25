import { BatchTracking } from "../api/generated/syntraApiV1.schemas";

export const TRACKING_LABELS: Record<string, string> = {
  [BatchTracking.None]: "Not tracked",
  [BatchTracking.Batch]: "By batch",
  [BatchTracking.BatchAndExpiry]: "By batch + expiry",
};

export const isTracked = (mode?: string) => mode === BatchTracking.Batch || mode === BatchTracking.BatchAndExpiry;

export type ExpiryStatus = "expired" | "soon" | "ok";

const SOON_DAYS = 30;

// `expiry` is a date-only string (yyyy-mm-dd). The server treats a batch as
// expired once that date is before today, so on the expiry date itself it is
// still sellable ("soon").
export function expiryStatus(expiry: string | null | undefined, now = new Date()): ExpiryStatus | null {
  if (!expiry) return null;

  const today = new Date(now.getFullYear(), now.getMonth(), now.getDate());
  const [year, month, day] = expiry.split("-").map(Number);
  const days = Math.round((new Date(year, month - 1, day).getTime() - today.getTime()) / 86_400_000);

  if (days < 0) return "expired";
  return days <= SOON_DAYS ? "soon" : "ok";
}
