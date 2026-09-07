import { QueryClientProvider } from '@tanstack/react-query';
import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { RouterProvider } from 'react-router';

import { LocaleProvider } from './app/i18n/locale';
import { queryClient } from './app/queryClient';
import { router } from './app/router';
import './styles.css';
import './app/layouts/presentationShell.css';
import './app/layouts/operationPage.css';
import './app/modules/homePage.css';
import './features/processing/processingExecution.css';

const rootElement = document.getElementById('root');

if (rootElement === null) {
  throw new Error('YowThi ERP Web root element was not found.');
}

createRoot(rootElement).render(
  <StrictMode>
    <LocaleProvider>
      <QueryClientProvider client={queryClient}>
        <RouterProvider router={router} />
      </QueryClientProvider>
    </LocaleProvider>
  </StrictMode>,
);
