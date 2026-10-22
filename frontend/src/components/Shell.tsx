import { NavLink, Outlet, useNavigate } from "react-router-dom";
import { useGetAuthMe } from "../api/generated/auth/auth";
import { clearToken } from "../auth/token";

const navLinkStyle = ({ isActive }: { isActive: boolean }) => ({
  marginRight: 16,
  fontWeight: isActive ? "bold" : "normal",
});

export function Shell() {
  const navigate = useNavigate();
  const me = useGetAuthMe();

  function handleLogout() {
    clearToken();
    navigate("/login");
  }

  return (
    <div style={{ fontFamily: "sans-serif" }}>
      <header
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          padding: 16,
          borderBottom: "1px solid #ddd",
        }}
      >
        <nav>
          <NavLink to="/" style={navLinkStyle}>
            Dashboard
          </NavLink>
          {/* Products/Stock/Purchasing/Sales links are added in FE-1b+ once those pages exist */}
        </nav>
        <div>
          {me.data?.email && <span style={{ marginRight: 16 }}>{me.data.email}</span>}
          <button onClick={handleLogout}>Log out</button>
        </div>
      </header>
      <main style={{ padding: 16 }}>
        <Outlet />
      </main>
    </div>
  );
}
