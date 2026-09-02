import { QueryClientProvider } from '@tanstack/react-query';
import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { RouterProvider } from 'react-router';

import { queryClient } from './app/queryClient';
import { router } from './app/router';
import './styles.css';
import './features/processing/processingExecution.css';

const rootElement = document.getElementById('root');

if (rootElement === null) {
  throw new Error('YowThi ERP Web root element was not found.');
}

createRoot(rootElement).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
    </QueryClientProvider>
  </StrictMode>,
);