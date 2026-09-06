import { Navigate, createBrowserRouter } from 'react-router';

import { OutsourcedSupplyDetailPage } from '../features/outsourced/OutsourcedSupplyDetailPage';
import { FinanceSettlementPage } from '../features/finance/FinanceSettlementPage';
import { InventoryOperationsPage } from '../features/inventory/InventoryOperationsPage';
import { InfrastructureLifecyclePage } from '../features/infrastructure/InfrastructureLifecyclePage';
import { LaborDailyWagePage } from '../features/labor/LaborDailyWagePage';
import { PartyLifecyclePage } from '../features/party/PartyLifecyclePage';
import { ProcessingExecutionPage } from '../features/processing/ProcessingExecutionPage';
import { ProcurementEntryPage } from '../features/procurement/ProcurementEntryPage';
import { SalesConfirmationPage } from '../features/sales/SalesConfirmationPage';
import { SalesPackagingWorkPage } from '../features/sales-handling/SalesPackagingWorkPage';
import { App } from './App';
import { ModuleIndexPage, ModuleSkeletonPage } from './modules/ModulePages';
import { skeletonModules } from './modules/moduleRegistry';

export const router = createBrowserRouter([
  {
    path: '/',
    element: <App />,
    children: [
      {
        index: true,
        element: <Navigate to="/procurement/entries/new" replace />,
      },
      {
        path: 'procurement/entries/new',
        element: <ProcurementEntryPage />,
      },
      {
        path: 'outsourced/supply-details/new',
        element: <OutsourcedSupplyDetailPage />,
      },
      {
        path: 'processing/executions/new',
        element: <ProcessingExecutionPage />,
      },
      {
        path: 'sales',
        element: <SalesConfirmationPage />,
      },
      {
        path: 'sales-handling',
        element: <SalesPackagingWorkPage />,
      },
      {
        path: 'labor',
        element: <LaborDailyWagePage />,
      },
      {
        path: 'finance',
        element: <FinanceSettlementPage />,
      },
      {
        path: 'inventory',
        element: <InventoryOperationsPage />,
      },
      {
        path: 'party',
        element: <PartyLifecyclePage />,
      },
      {
        path: 'infrastructure',
        element: <InfrastructureLifecyclePage />,
      },
      {
        path: 'modules',
        element: <ModuleIndexPage />,
      },
      ...skeletonModules.map((module) => ({
        path: module.route.slice(1),
        element: <ModuleSkeletonPage moduleKey={module.key} />,
      })),
    ],
  },
]);