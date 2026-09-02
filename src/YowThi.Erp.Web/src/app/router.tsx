import { Navigate, createBrowserRouter } from 'react-router';

import { OutsourcedSupplyDetailPage } from '../features/outsourced/OutsourcedSupplyDetailPage';
import { ProcurementEntryPage } from '../features/procurement/ProcurementEntryPage';
import { App } from './App';

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
    ],
  },
]);