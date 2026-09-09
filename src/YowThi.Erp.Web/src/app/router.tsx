import { Navigate, createBrowserRouter } from 'react-router';

import { DataProtectionPage } from '../features/data-protection/DataProtectionPage';
import { FinanceSettlementPage } from '../features/finance/FinanceSettlementPage';
import { InfrastructureLifecyclePage } from '../features/infrastructure/InfrastructureLifecyclePage';
import { InventoryOperationsPage } from '../features/inventory/InventoryOperationsPage';
import { InventoryPositionPage } from '../features/inventory/InventoryPositionPage';
import { StorageLocationMasterPage } from '../features/inventory/StorageLocationMasterPage';
import { WarehouseMasterPage } from '../features/inventory/WarehouseMasterPage';
import { LaborDailyWagePage } from '../features/labor/LaborDailyWagePage';
import { OutsourcedLifecycleWorkspace } from '../features/outsourced/OutsourcedLifecycleWorkspace';
import { OutsourcedSupplyDetailPage } from '../features/outsourced/OutsourcedSupplyDetailPage';
import { CustomerMasterPage } from '../features/party/CustomerMasterPage';
import { EmployeeMasterPage } from '../features/party/EmployeeMasterPage';
import { FarmerMasterPage } from '../features/party/FarmerMasterPage';
import { OutsourcedVendorMasterPage } from '../features/party/OutsourcedVendorMasterPage';
import { PartyLifecyclePage } from '../features/party/PartyLifecyclePage';
import { SupplierMasterPage } from '../features/party/SupplierMasterPage';
import { ProcessingExecutionPage } from '../features/processing/ProcessingExecutionPage';
import { ProcurementEntryPage } from '../features/procurement/ProcurementEntryPage';
import { ProcurementWorkspacePage } from '../features/procurement/ProcurementWorkspacePage';
import { ProcurementProductMasterPage } from '../features/product/ProcurementProductMasterPage';
import { SalesProductGroupLifecyclePage } from '../features/product/SalesProductGroupLifecyclePage';
import { SalesProductGroupMasterPage } from '../features/product/SalesProductGroupMasterPage';
import { SalesProductMasterPage } from '../features/product/SalesProductMasterPage';
import { SalesPackagingItemMasterPage } from '../features/sales-handling/SalesPackagingItemMasterPage';
import { SalesPackagingWorkPage } from '../features/sales-handling/SalesPackagingWorkPage';
import { SalesWorkspacePage } from '../features/sales/SalesWorkspacePage';
import { SecurityAccountPage } from '../features/security/SecurityAccountPage';
import { App } from './App';
import { ModuleIndexPage, ModuleSkeletonPage } from './modules/ModulePages';
import { skeletonModules } from './modules/moduleRegistry';

export const router = createBrowserRouter([
  {
    path: '/',
    element: <App />,
    children: [
      { index: true, element: <Navigate to="/modules" replace /> },
      { path: 'procurement', element: <ProcurementWorkspacePage /> },
      { path: 'procurement/entries/new', element: <ProcurementEntryPage /> },
      { path: 'outsourced', element: <OutsourcedLifecycleWorkspace /> },
      { path: 'outsourced/supply-details/new', element: <OutsourcedSupplyDetailPage /> },
      { path: 'processing/executions/new', element: <ProcessingExecutionPage /> },
      { path: 'sales', element: <SalesWorkspacePage /> },
      { path: 'sales-handling', element: <SalesPackagingWorkPage /> },
      { path: 'sales-handling/packaging-items', element: <SalesPackagingItemMasterPage /> },
      { path: 'labor', element: <LaborDailyWagePage /> },
      { path: 'finance', element: <FinanceSettlementPage /> },
      { path: 'inventory', element: <InventoryPositionPage /> },
      { path: 'inventory/operations', element: <InventoryOperationsPage /> },
      { path: 'inventory/warehouses', element: <WarehouseMasterPage /> },
      { path: 'inventory/storage-locations', element: <StorageLocationMasterPage /> },
      { path: 'party', element: <SupplierMasterPage /> },
      { path: 'party/customers', element: <CustomerMasterPage /> },
      { path: 'party/outsourced-vendors', element: <OutsourcedVendorMasterPage /> },
      { path: 'party/farmers', element: <FarmerMasterPage /> },
      { path: 'party/employees', element: <EmployeeMasterPage /> },
      { path: 'party/lifecycle', element: <PartyLifecyclePage /> },
      { path: 'infrastructure', element: <InfrastructureLifecyclePage /> },
      { path: 'product', element: <ProcurementProductMasterPage /> },
      { path: 'product/sales-products', element: <SalesProductMasterPage /> },
      { path: 'product/sales-product-groups', element: <SalesProductGroupMasterPage /> },
      { path: 'product/lifecycle', element: <SalesProductGroupLifecyclePage /> },
      { path: 'security/accounts', element: <SecurityAccountPage /> },
      { path: 'data-protection', element: <DataProtectionPage /> },
      { path: 'data-protection/hard-delete', element: <Navigate to="/data-protection" replace /> },
      { path: 'modules', element: <ModuleIndexPage /> },
      ...skeletonModules.map((module) => ({
        path: module.route.slice(1),
        element: <ModuleSkeletonPage moduleKey={module.key} />,
      })),
    ],
  },
]);
