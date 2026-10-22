import { useState, type FormEvent } from "react";
import { useNavigate } from "react-router-dom";
import { usePostAuthLogin } from "../api/generated/auth/auth";
import { setToken } from "../auth/token";

export function LoginPage() {
  const navigate = useNavigate();
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");

  const loginMutation = usePostAuthLogin({
    mutation: {
      onSuccess: (response) => {
        setToken(response.token);
        navigate("/");
      },
    },
  });

  function handleSubmit(event: FormEvent) {
    event.preventDefault();
    loginMutation.mutate({ data: { email, password } });
  }

  return (
    <div className="flex min-h-screen items-center justify-center bg-slate-50 px-4">
      <div className="w-full max-w-sm rounded-xl bg-white p-8 shadow-sm ring-1 ring-slate-200">
        <h1 className="mb-6 text-2xl font-semibold text-slate-900">Syntra</h1>
        <form onSubmit={handleSubmit} className="space-y-4">
          <div>
            <label className="mb-1 block text-sm font-medium text-slate-700">Email</label>
            <input
              type="email"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              required
              className="w-full rounded-md border border-slate-300 px-3 py-2 text-sm text-slate-900 focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
            />
          </div>
          <div>
            <label className="mb-1 block text-sm font-medium text-slate-700">Password</label>
            <input
              type="password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              required
              className="w-full rounded-md border border-slate-300 px-3 py-2 text-sm text-slate-900 focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
            />
          </div>
          <button
            type="submit"
            disabled={loginMutation.isPending}
            className="w-full rounded-md bg-indigo-600 px-4 py-2 text-sm font-medium text-white transition hover:bg-indigo-700 disabled:cursor-not-allowed disabled:opacity-60"
          >
            {loginMutation.isPending ? "Logging in..." : "Log in"}
          </button>
          {loginMutation.isError && (
            <p className="text-sm text-red-600">Invalid email or password.</p>
          )}
        </form>
      </div>
    </div>
  );
}
