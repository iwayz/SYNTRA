import { isAxiosError } from "axios";

// The API answers failures with RFC 9457 problem details. Depending on the
// endpoint the useful text is in `detail` (e.g. the oversell message), `title`
// (e.g. "Branch is inactive."), or the per-field `errors` map (validation and
// password-rule failures) - surface whichever is there instead of a generic line.
export function errorMessage(error: unknown, fallback = "Something went wrong - please try again."): string {
  if (!isAxiosError(error)) return fallback;

  const data = error.response?.data;

  if (data?.errors && typeof data.errors === "object") {
    const messages = Object.values(data.errors as Record<string, unknown>)
      .flat()
      .filter((m): m is string => typeof m === "string");
    if (messages.length > 0) return messages.join(" ");
  }

  if (typeof data?.detail === "string") return data.detail;
  if (typeof data?.title === "string") return data.title;

  return fallback;
}
