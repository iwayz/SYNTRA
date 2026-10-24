import { NavLink, Outlet, useNavigate } from "react-router-dom";
import { clearToken } from "../auth/token";
import { useCurrentUser } from "../auth/useCurrentUser";

function navLinkClasses({ isActive }: { isActive: boolean }) {
  return `rounded-md px-3 py-1.5 text-sm font-medium transition ${
    isActive ? "bg-indigo-50 text-indigo-700" : "text-slate-600 hover:bg-slate-100"
  }`;
}

export function Shell() {
  const navigate = useNavigate();
  const { isLoaded, email, isAdmin, branchIds } = useCurrentUser();

  function handleLogout() {
    clearToken();
    navigate("/login");
  }

  return (
    <div className="min-h-screen bg-slate-50">
      <header className="flex flex-wrap items-center justify-between gap-y-2 border-b border-slate-200 bg-white px-6 py-3">
        <div className="flex flex-wrap items-center gap-x-6 gap-y-2">
          <span className="text-lg font-semibold text-slate-900">Syntra</span>
          <nav className="flex flex-wrap gap-1">
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
            <NavLink to="/stock-balance" className={navLinkClasses}>
              Stock Balance
            </NavLink>
            <NavLink to="/stock-movements" className={navLinkClasses}>
              Stock Movements
            </NavLink>
            <NavLink to="/purchase-orders" className={navLinkClasses}>
              Purchase Orders
            </NavLink>
            <NavLink to="/sales-orders" className={navLinkClasses}>
              Sales Orders
            </NavLink>
            {isAdmin && (
              <NavLink to="/branches" className={navLinkClasses}>
                Branches
              </NavLink>
            )}
          </nav>
        </div>
        <div className="flex items-center gap-4">
          {email && (
            <NavLink to="/account" className="text-sm text-slate-500 hover:text-indigo-700 hover:underline">
              {email}
            </NavLink>
          )}
          <button
            onClick={handleLogout}
            className="rounded-md border border-slate-300 px-3 py-1.5 text-sm font-medium text-slate-700 transition hover:bg-slate-100"
          >
            Log out
          </button>
        </div>
      </header>
      {isLoaded && !isAdmin && branchIds.length === 0 && (
        <div className="border-b border-amber-200 bg-amber-50 px-6 py-2 text-sm text-amber-800">
          You have not been assigned to any branch yet, so there is nothing to show. Ask an admin to assign you one.
        </div>
      )}
      <main className="p-6">
        <Outlet />
      </main>
    </div>
  );
}
