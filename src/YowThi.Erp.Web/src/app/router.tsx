import { Navigate, createBrowserRouter } from 'react-router';

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
    ],
  },
]);
