import { Navigate, Route, Routes } from "react-router-dom";
import { RequireAuth } from "./auth/RequireAuth";
import { Shell } from "./components/Shell";
import { CustomersPage } from "./pages/CustomersPage";
import { DashboardPage } from "./pages/DashboardPage";
import { LoginPage } from "./pages/LoginPage";
import { ProductDetailPage } from "./pages/ProductDetailPage";
import { ProductsPage } from "./pages/ProductsPage";
import { PurchaseOrderCreatePage } from "./pages/PurchaseOrderCreatePage";
import { PurchaseOrderDetailPage } from "./pages/PurchaseOrderDetailPage";
import { PurchaseOrdersPage } from "./pages/PurchaseOrdersPage";
import { SalesOrderCreatePage } from "./pages/SalesOrderCreatePage";
import { SalesOrderDetailPage } from "./pages/SalesOrderDetailPage";
import { SalesOrdersPage } from "./pages/SalesOrdersPage";
import { StockBalancePage } from "./pages/StockBalancePage";
import { StockMovementsPage } from "./pages/StockMovementsPage";
import { SuppliersPage } from "./pages/SuppliersPage";
import { UnitsOfMeasurePage } from "./pages/UnitsOfMeasurePage";
import { WarehousesPage } from "./pages/WarehousesPage";

function App() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route element={<RequireAuth />}>
        <Route element={<Shell />}>
          <Route path="/" element={<DashboardPage />} />
          <Route path="/products" element={<ProductsPage />} />
          <Route path="/products/:id" element={<ProductDetailPage />} />
          <Route path="/units-of-measure" element={<UnitsOfMeasurePage />} />
          <Route path="/warehouses" element={<WarehousesPage />} />
          <Route path="/customers" element={<CustomersPage />} />
          <Route path="/suppliers" element={<SuppliersPage />} />
          <Route path="/stock-balance" element={<StockBalancePage />} />
          <Route path="/stock-movements" element={<StockMovementsPage />} />
          <Route path="/purchase-orders" element={<PurchaseOrdersPage />} />
          <Route path="/purchase-orders/new" element={<PurchaseOrderCreatePage />} />
          <Route path="/purchase-orders/:id" element={<PurchaseOrderDetailPage />} />
          <Route path="/sales-orders" element={<SalesOrdersPage />} />
          <Route path="/sales-orders/new" element={<SalesOrderCreatePage />} />
          <Route path="/sales-orders/:id" element={<SalesOrderDetailPage />} />
        </Route>
      </Route>
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  );
}

export default App;
