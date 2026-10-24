import { Navigate, Outlet } from "react-router-dom";
import { useCurrentUser } from "./useCurrentUser";

// UX only: the server enforces admin-only endpoints regardless. This just keeps
// non-admins from landing on screens whose actions would all be refused.
export function RequireAdmin() {
  const { isLoaded, isAdmin } = useCurrentUser();

  if (!isLoaded) {
    return <p className="text-slate-500">Loading...</p>;
  }

  if (!isAdmin) {
    return <Navigate to="/" replace />;
  }

  return <Outlet />;
}
