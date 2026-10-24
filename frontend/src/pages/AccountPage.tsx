import { useState, type FormEvent } from "react";
import { errorMessage } from "../api/errors";
import { usePostAuthChangePassword } from "../api/generated/auth/auth";
import { useCurrentUser } from "../auth/useCurrentUser";

const inputClasses =
  "w-72 rounded-md border border-slate-300 px-3 py-2 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500";

export function AccountPage() {
  const { email } = useCurrentUser();
  const [currentPassword, setCurrentPassword] = useState("");
  const [newPassword, setNewPassword] = useState("");
  const [confirmPassword, setConfirmPassword] = useState("");

  const mismatch = confirmPassword !== "" && newPassword !== confirmPassword;

  const changeMutation = usePostAuthChangePassword({
    mutation: {
      onSuccess: () => {
        setCurrentPassword("");
        setNewPassword("");
        setConfirmPassword("");
      },
    },
  });

  function handleSubmit(event: FormEvent) {
    event.preventDefault();
    if (newPassword !== confirmPassword) return;
    changeMutation.mutate({ data: { currentPassword, newPassword } });
  }

  return (
    <div className="space-y-6">
      <h2 className="text-lg font-semibold text-slate-900">Account</h2>

      <div className="rounded-xl bg-white p-6 shadow-sm ring-1 ring-slate-200">
        <h3 className="text-sm font-semibold text-slate-900">Change password</h3>
        {email && <p className="mb-4 text-sm text-slate-500">Signed in as {email}</p>}

        <form onSubmit={handleSubmit} className="space-y-4">
          <div>
            <label className="mb-1 block text-sm font-medium text-slate-700">Current password</label>
            <input
              type="password"
              value={currentPassword}
              onChange={(e) => setCurrentPassword(e.target.value)}
              required
              autoComplete="current-password"
              className={inputClasses}
            />
          </div>
          <div>
            <label className="mb-1 block text-sm font-medium text-slate-700">New password</label>
            <input
              type="password"
              value={newPassword}
              onChange={(e) => setNewPassword(e.target.value)}
              required
              autoComplete="new-password"
              className={inputClasses}
            />
          </div>
          <div>
            <label className="mb-1 block text-sm font-medium text-slate-700">Confirm new password</label>
            <input
              type="password"
              value={confirmPassword}
              onChange={(e) => setConfirmPassword(e.target.value)}
              required
              autoComplete="new-password"
              className={inputClasses}
            />
            {mismatch && <p className="mt-1 text-sm text-red-600">Passwords do not match.</p>}
          </div>
          <button
            type="submit"
            disabled={changeMutation.isPending || mismatch}
            className="rounded-md bg-indigo-600 px-4 py-2 text-sm font-medium text-white transition hover:bg-indigo-700 disabled:cursor-not-allowed disabled:opacity-60"
          >
            Change password
          </button>
        </form>

        {changeMutation.isSuccess && <p className="mt-3 text-sm text-emerald-700">Password changed.</p>}
        {changeMutation.isError && (
          <p className="mt-3 text-sm text-red-600">
            {errorMessage(changeMutation.error, "Could not change the password.")}
          </p>
        )}
      </div>
    </div>
  );
}
