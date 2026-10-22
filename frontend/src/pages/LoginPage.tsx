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
    <div style={{ maxWidth: 360, margin: "80px auto", fontFamily: "sans-serif" }}>
      <h1>Syntra</h1>
      <form onSubmit={handleSubmit}>
        <div style={{ marginBottom: 12 }}>
          <label>
            Email
            <br />
            <input
              type="email"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              required
              style={{ width: "100%" }}
            />
          </label>
        </div>
        <div style={{ marginBottom: 12 }}>
          <label>
            Password
            <br />
            <input
              type="password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              required
              style={{ width: "100%" }}
            />
          </label>
        </div>
        <button type="submit" disabled={loginMutation.isPending}>
          {loginMutation.isPending ? "Logging in..." : "Log in"}
        </button>
        {loginMutation.isError && (
          <p style={{ color: "crimson" }}>Invalid email or password.</p>
        )}
      </form>
    </div>
  );
}
