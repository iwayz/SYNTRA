import { NavLink, Outlet, useNavigate } from "react-router-dom";
import { useGetAuthMe } from "../api/generated/auth/auth";
import { clearToken } from "../auth/token";

function navLinkClasses({ isActive }: { isActive: boolean }) {
  return `rounded-md px-3 py-1.5 text-sm font-medium transition ${
    isActive ? "bg-indigo-50 text-indigo-700" : "text-slate-600 hover:bg-slate-100"
  }`;
}

export function Shell() {
  const navigate = useNavigate();
  const me = useGetAuthMe();

  function handleLogout() {
    clearToken();
    navigate("/login");
  }

  return (
    <div className="min-h-screen bg-slate-50">
      <header className="flex items-center justify-between border-b border-slate-200 bg-white px-6 py-3">
        <div className="flex items-center gap-6">
          <span className="text-lg font-semibold text-slate-900">Syntra</span>
          <nav className="flex gap-1">
            <NavLink to="/" end className={navLinkClasses}>
              Dashboard
            </NavLink>
            <NavLink to="/products" className={navLinkClasses}>
              Products
            </NavLink>
            <NavLink to="/units-of-measure" className={navLinkClasses}>
              Units of Measure
            </NavLink>
            <NavLink to="/warehouses" className={navLinkClasses}>
              Warehouses
            </NavLink>
            <NavLink to="/customers" className={navLinkClasses}>
              Customers
            </NavLink>
            <NavLink to="/suppliers" className={navLinkClasses}>
              Suppliers
            </NavLink>
            {/* Stock/Purchasing/Sales links are added in FE-1c+ once those pages exist */}
          </nav>
        </div>
        <div className="flex items-center gap-4">
          {me.data?.email && <span className="text-sm text-slate-500">{me.data.email}</span>}
          <button
            onClick={handleLogout}
            className="rounded-md border border-slate-300 px-3 py-1.5 text-sm font-medium text-slate-700 transition hover:bg-slate-100"
          >
            Log out
          </button>
        </div>
      </header>
      <main className="p-6">
        <Outlet />
      </main>
    </div>
  );
}
